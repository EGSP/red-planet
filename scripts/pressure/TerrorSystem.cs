using Godot;

/// <summary>
/// Террор: числовая мера того, насколько игрок себя обнаружил. Складывается из четырёх
/// слагаемых — мощности производства, экспансии, мощи армии и времени партии, — каждое
/// из которых проходит через свою кривую насыщения и берётся со своим весом.
///
/// ВРЕМЯ — ЕДИНСТВЕННОЕ СЛАГАЕМОЕ, НА КОТОРОЕ ИГРОК НЕ ВЛИЯЕТ. Оно и не про обнаружение,
/// а про пол, ниже которого давление не опускается: иначе выгодной оказалась бы стратегия
/// не строить вовсе, и первый узел петли обесценился бы. Вес его держится ниже прочих,
/// чтобы показатель по-прежнему отвечал прежде всего за действия игрока.
///
/// КУДА ЭТО ВСТРАИВАЕТСЯ. В стрелку «Экспансия → Давление» игрового цикла. Она объявлена
/// обязательной, но до сих пор работала неявно и геометрически: чем длиннее периметр, тем
/// больше точек соприкосновения. Террор делает эту зависимость измеримой и, что важнее,
/// видимой игроку — без показанного числа решение «не расти сейчас» принимать не из чего.
///
/// ПОЧЕМУ ПРОИЗВОДСТВО ВХОДИТ, ХОТЯ ЭНЕРГИЯ ЕСТЬ ВАЛЮТА ПОБЕДЫ. Вход считается по ВЫРАБОТКЕ,
/// а не по потреблению, поэтому сам портал в показатель не входит — входят генераторы,
/// построенные ради него. Это не наказание за требуемое, а вторая цена главного решения
/// партии: начать портал значит поднять давление ровно тогда, когда база и так ослаблена.
/// Вес производства при этом заметно ниже весов экспансии и армии, иначе выгодной стала бы
/// стратегия отказа от экономики, а она обесценивает первый узел петли.
///
/// ПОЧЕМУ ПОЛНЫЙ ПЕРЕСЧЁТ, А НЕ ДЕЛЬТА. Общее правило проекций — обновляться дельтой —
/// здесь сознательно не применяется. Пересчёт идёт раз в секунду по нескольким сотням
/// сущностей, то есть стоит околонуля, а дельта потребовала бы держать вклад каждой
/// сущности в словаре и вычитать его в момент выбытия. К этому мигу нода обычно уже
/// освобождена движком, и читать её определение нельзя — понадобился бы ещё и словарь,
/// переживающий смерть ключа. Цена сложности выше цены обхода.
///
/// КУДА ПОКАЗАТЕЛЬ УХОДИТ. Сглаженное значение читают две системы, и читают по-разному.
/// <see cref="PressureSystem"/> переводит его в бюджет постоянного давления одним коэффициентом.
/// <see cref="WaveSystem"/> отбирает по нему применимые волны, а бюджет отдельной волны от
/// показателя зависит лишь тогда, когда это задано в её файле: волна есть напряжение в моменте
/// и приходит поверх бюджета фона, а не внутри него.
/// </summary>
public partial class TerrorSystem : GameSystem
{
    /// <summary>Кривые, веса и опорные значения. Не назначено — берутся умолчания класса.</summary>
    [Export] public TerrorSettings Settings;

    private float _elapsed;
    private bool _sampled;

    /// <summary>Номер следующего замера. Он же отметка времени в регистре сведений.</summary>
    public int Tick { get; private set; }

    // ── Сырые величины, до кривых ─────────────────────────────────────────────────

    /// <summary>Поток добычи, приведённый к металу.</summary>
    public float RawProduction { get; private set; }

    /// <summary>Сумма весов построек, умноженных на коэффициент их зоны удалённости.</summary>
    public float RawExpansion { get; private set; }

    /// <summary>Сумма мощи подвижных сущностей игрока.</summary>
    public float RawArmy { get; private set; }

    /// <summary>Сколько идёт партия, секунд игрового времени.</summary>
    public float RawTime { get; private set; }

    // ── Вклады, после кривых ──────────────────────────────────────────────────────

    public float Production { get; private set; }
    public float Expansion { get; private set; }
    public float Army { get; private set; }
    public float Time { get; private set; }

    /// <summary>Показатель на этот миг. Его видит игрок.</summary>
    public float Raw { get; private set; }

    /// <summary>
    /// Показатель с задержкой. На него будет смотреть давление — см. SmoothingSeconds
    /// в настройках. Игроку не показывается: это внутренняя величина.
    /// </summary>
    public float Smoothed { get; private set; }

    protected override void OnRegister()
    {
        if (Settings != null)
            return;

        // Без настроек система обязана работать: незаполненное поле в инспекторе не должно
        // ронять подсчёт целиком. Умолчания класса дают осмысленные числа и запасную кривую
        GD.PushWarning("[TerrorSystem] настройки не назначены, взяты умолчания");
        Settings = new TerrorSettings();
    }

    protected override void OnLink()
    {
        // Шаг ряда задаётся один раз и с этого мига постоянен: номер замера служит
        // в регистре отметкой времени, и менять шаг по ходу партии нельзя
        var metrics = GM?.Metrics;

        if (metrics != null)
            metrics.Step = Settings.SampleInterval;
    }

    public override void Step(double dt)
    {
        float interval = Mathf.Max(0.01f, Settings.SampleInterval);

        _elapsed += (float)dt;

        if (_elapsed < interval)
            return;

        _elapsed -= interval;
        Sample(interval);
    }

    private void Sample(float interval)
    {
        var settings = Settings;

        RawProduction = Produce(settings);
        RawExpansion = Expand();
        RawArmy = Arm();
        RawTime += interval;

        Production = settings.Production(RawProduction);
        Expansion = settings.Expansion(RawExpansion);
        Army = settings.Army(RawArmy);
        Time = settings.Time(RawTime);

        Raw = Production + Expansion + Army + Time;

        Smooth(settings, interval);
        Publish();

        Tick++;
    }

    /// <summary>
    /// Мощность производства: валовая выработка, приведённая к металу по курсу экстрактора.
    ///
    /// Именно валовая, а не сальдо. По сальдо портал, потребляющий энергию, СНИЖАЛ бы
    /// показатель — то есть чем ближе победа, тем спокойнее, а требуется обратное.
    /// </summary>
    private float Produce(TerrorSettings settings)
    {
        var ledger = GM.Economy;
        float rate = Mathf.Max(0.01f, settings.EnergyPerMetal);

        return ledger.MetalIncome + ledger.EnergyIncome / rate;
    }

    /// <summary>
    /// Экспансия: вес каждой постройки, умноженный на множитель кольца мира
    /// (<see cref="WorldSettings.TerrorMultiplierAt"/>).
    ///
    /// Каркасы сюда не входят — разрез набирается по готовым постройкам. Это намеренно:
    /// поставленный, но не достроенный каркас ещё ничего не занял и решением не является,
    /// его в любой миг можно отменить.
    /// </summary>
    private float Expand()
    {
        float sum = 0f;
        var landing = Const.LandingPoint;

        foreach (var building in GM.Index.All<Building>())
        {
            var def = building.Definition;

            if (def == null)
                continue;

            float weight = def.TerrorExpansion;

            if (weight <= 0f)
                continue;

            float cells = building.Position.DistanceTo(landing) / Const.Unit;
            sum += weight * World.Settings.TerrorMultiplierAt(cells);
        }

        return sum;
    }

    /// <summary>
    /// Мощь армии: сумма мощи подвижных сущностей игрока, без географии.
    ///
    /// Разрез берётся по СТОРОНЕ, а не по типу узла. Раньше противник отсекался сам собой,
    /// поскольку имел отдельный класс; после слияния классов такого отсева нет, и полагаться
    /// на него было бы уже нельзя: показатель меряет игрока, и войди сюда противник, он рос
    /// бы от собственных волн и разгонял бы себя.
    /// </summary>
    private float Arm()
    {
        float sum = 0f;

        foreach (var unit in GM.Units[Faction.Player])
            sum += unit.Definition?.ArmyPower ?? 0f;

        return sum;
    }

    /// <summary>
    /// Экспоненциальное сглаживание. Первый замер задаёт начальное значение напрямую:
    /// иначе показатель полминуты подтягивался бы к истине с нуля и первые волны
    /// считались бы по величине, которой никогда не было.
    /// </summary>
    private void Smooth(TerrorSettings settings, float interval)
    {
        if (!_sampled)
        {
            Smoothed = Raw;
            _sampled = true;
            return;
        }

        float tau = Mathf.Max(0.01f, settings.SmoothingSeconds);
        Smoothed += (Raw - Smoothed) * (1f - Mathf.Exp(-interval / tau));
    }

    private void Publish()
    {
        Metric("terror.raw", Raw);
        Metric("terror.smoothed", Smoothed);
        Metric("terror.production", Production);
        Metric("terror.expansion", Expansion);
        Metric("terror.army", Army);
        Metric("terror.time", Time);
    }

    private void Metric(string channel, float value) =>
        GM.Events.Append(new MetricSampled { Channel = channel, Value = value, Tick = Tick });
}
