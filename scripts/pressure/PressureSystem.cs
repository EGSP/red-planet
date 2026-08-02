using System.Collections.Generic;
using Godot;

/// <summary>
/// Постоянное давление: сколько противника стоит на карте и когда приходит следующий.
///
/// БЮДЖЕТ ВМЕСТО ПОСТОЯННОЙ ЧИСЛЕННОСТИ. Раньше система держала неизменные четыре единицы
/// вне зависимости от того, что игрок успел построить, — то есть стрелка «Экспансия →
/// Давление» игрового цикла существовала только на бумаге. Теперь потолок выводится из
/// террора: очки показателя переводятся в допустимую суммарную мощь коэффициентом
/// <see cref="PressureSettings.PowerPerTerror"/>, а занятым считается то, что уже ходит
/// по карте. Заспавнить вид, который в остаток не помещается, нельзя.
///
/// ДВА РЕГУЛЯТОРА, И ОНИ РАЗНЫЕ. Бюджет задаёт, СКОЛЬКО противника стоит на карте
/// одновременно; интервал задаёт, КАК БЫСТРО восполняются потери. Смешивать их не нужно:
/// первый отвечает за напряжение, второй за темп, и подбираются они порознь.
///
/// ПОКАЗАТЕЛЬ БЕРЁТСЯ СГЛАЖЕННЫЙ, а не мгновенный. Он для того и заведён: без задержки
/// снос собственных построек перед волной мгновенно снижал бы давление, и появилась бы
/// вырожденная тактика.
///
/// ВОЛНЫ БЮДЖЕТОМ НЕ ОГРАНИЧЕНЫ и здесь пока не реализованы. Когда они появятся, их юниты
/// будут отмечены признаком <see cref="PressureOrigin.Wave"/> и в занятое место не войдут:
/// волна есть напряжение в моменте, а не часть фона.
///
/// Появляются юниты на окружности вокруг базы радиусом с внешнее кольцо руды, взятым
/// с запасом (Const.EnemyRingFactor): дальние месторождения оказываются на линии подхода,
/// и добыча на краю кольца перестаёт быть безопасной.
/// </summary>
public partial class PressureSystem : GameSystem
{
    /// <summary>Коэффициент перевода, пол бюджета и темп восполнения.</summary>
    [Export] public PressureSettings Settings;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<UnitDefinition> _choices = new();

    private float _timer;

    /// <summary>Потолок суммарной мощи фона на этот миг. Показывается в отладочной панели.</summary>
    public float Budget { get; private set; }

    /// <summary>Сколько мощи занято живыми юнитами фона.</summary>
    public float Used { get; private set; }

    public float Available => Mathf.Max(Budget - Used, 0f);

    protected override void OnRegister()
    {
        _rng.Randomize();

        if (Settings == null)
        {
            // Без настроек система обязана работать: незаполненное поле в инспекторе
            // не должно оставлять карту пустой на всю партию
            GD.PushWarning("[PressureSystem] настройки не назначены, взяты умолчания");
            Settings = new PressureSettings();
        }

        _timer = Settings.FirstDelay;
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null)
            return;

        _timer -= (float)dt;
        if (_timer > 0f)
            return;

        _timer = Mathf.Max(0.1f, Settings.SpawnInterval);

        Budget = Cap();
        Used = AmbientPower();

        var def = PickType(Available);

        if (def != null)
            Spawn(def);
    }

    /// <summary>
    /// Потолок мощи. Очки террора переводятся в мощь одним коэффициентом; пол задан
    /// настройкой и нужен на первые секунды партии, пока показатель ещё около нуля.
    /// </summary>
    private float Cap()
    {
        float terror = GM.System<TerrorSystem>()?.Smoothed ?? 0f;

        return Mathf.Max(terror * Settings.PowerPerTerror, Settings.MinimumPower);
    }

    /// <summary>
    /// Занятая часть бюджета: сумма мощи живых юнитов противника, пришедших фоном.
    /// Волновые пропускаются — см. заголовок класса.
    /// </summary>
    private float AmbientPower()
    {
        float sum = 0f;

        foreach (var unit in GM.Units[Faction.Hostile])
            if (unit.Origin == PressureOrigin.Ambient)
                sum += unit.Definition?.ArmyPower ?? 0f;

        return sum;
    }

    private void Spawn(UnitDefinition def)
    {
        float angle = _rng.RandfRange(0f, Mathf.Tau);
        var position = Heading.Forward(angle) * Const.EnemySpawnRadiusPx;

        var enemy = GM.Spawn.SpawnUnit(def, position, Faction.Hostile);
        enemy.Origin = PressureOrigin.Ambient;

        // Появился — уже смотрит на базу: иначе первый ход выглядит как разворот на месте
        enemy.Rotation = Heading.AngleTo(position, Vector2.Zero);

        Used += def.ArmyPower;

        GM.Events.Append(new EnemySpawned
        {
            EntityId = enemy.Id,
            DefinitionId = def.Id,
            Pos = position,
            Origin = PressureOrigin.Ambient,
        });
    }

    /// <summary>
    /// Вид выбирается равновероятно среди тех, кто помещается в остаток бюджета. Отдельного
    /// веса появления у вида больше нет, и это осознанная потеря: частота теперь есть
    /// следствие цены, а не самостоятельная настройка. Пока бюджет мал, проходят только
    /// дешёвые виды, а с его ростом к ним добавляются дорогие — то есть состав давления
    /// меняется сам, без второго регулятора, который пришлось бы согласовывать с первым.
    ///
    /// Вид с нулевой мощью не выставляется никогда: он занимал бы нулевое место и потому
    /// шёл бы бесконечно. Такое значение означает «в бюджете не участвует» и пригодится
    /// для того, что выставляется скриптом волны.
    /// </summary>
    private UnitDefinition PickType(float available)
    {
        if (available <= 0f)
            return null;

        _choices.Clear();

        foreach (var def in GM.Catalog.Enemies)
        {
            float power = def.ArmyPower;

            if (power > 0f && power <= available)
                _choices.Add(def);
        }

        return _choices.Count > 0
            ? _choices[_rng.RandiRange(0, _choices.Count - 1)]
            : null;
    }
}
