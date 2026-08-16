using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// Сбор наблюдаемых величин партии: раз в такт опрашивает системы, публикует замеры
/// документами и пишет отчёт в файл.
///
/// ПОЧЕМУ ОТДЕЛЬНАЯ СИСТЕМА. Раньше замеры публиковал <see cref="TerrorSystem"/>: он владел
/// шагом ряда и сам решал, что попадёт в регистр сведений. Пока рядов было шесть и все они
/// принадлежали террору, это ещё выглядело последовательным, но экономика, состав сил и счёт
/// боя к террору отношения не имеют, и складывать их в систему давления значило бы дать ей
/// вторую роль. Здесь же система террора становится обычным поставщиком величин наравне
/// с прочими, а владельцем такта — эта система.
///
/// ПАУЗА НЕ УЧИТЫВАЕТСЯ САМА СОБОЙ, без единой проверки: на паузе <see cref="Session"/>
/// отключает обработку у ветки систем, поэтому шаг сюда не приходит вовсе. Отсюда следует,
/// что номер такта равен игровому времени партии, делённому на шаг, — и связь «номер такта —
/// минута партии» остаётся точной на всём протяжении отчёта.
///
/// ПОЛНЫЙ ОПРОС, А НЕ ДЕЛЬТА. Обход состава сторон идёт раз в секунду по нескольким сотням
/// сущностей, то есть стоит околонуля. Подписка на изменения состава дала бы выигрыш,
/// несопоставимый со сложностью учёта выбывших.
/// </summary>
public partial class MetricsSystem : GameSystem
{
    /// <summary>
    /// Шаг замера, секунд игрового времени. Задаётся один раз при связывании: номер такта
    /// служит в регистре сведений отметкой времени, и менять шаг по ходу партии нельзя.
    /// </summary>
    [Export] public float Interval = 1f;

    /// <summary>Писать ли отчёт партии в файл. Отключается, когда файлы мешают — например, в песочнице.</summary>
    [Export] public bool WriteReport = true;

    private readonly MetricsRecorder _recorder = new();

    private float[] _values;
    private float _elapsed;
    private Session _session;
    private int _wavesWritten;
    private bool _closed;

    /// <summary>Номер следующего замера. Он же отметка времени в регистре сведений.</summary>
    public int Tick { get; private set; }

    /// <summary>Шаг замера, приведённый к допустимому значению.</summary>
    public float SampleStep => Mathf.Max(0.01f, Interval);

    /// <summary>Длительность партии без пауз, секунд.</summary>
    public float Seconds => Tick * SampleStep;

    /// <summary>Путь к файлу отчёта или null, если запись не ведётся.</summary>
    public string ReportPath => _recorder.Path;

    protected override void OnLink()
    {
        _values = new float[MetricChannels.Count];

        var metrics = GM?.Metrics;

        if (metrics != null)
            metrics.Step = SampleStep;

        _session = this.Ancestor<Session>();

        if (_session != null)
            _session.OutcomeChanged += OnOutcomeChanged;

        if (WriteReport)
            _recorder.TryStart(SampleStep, out _);
    }

    /// <summary>
    /// Снос сессии закрывает отчёт, если тот ещё не закрыт исходом партии. Этим же путём
    /// отчёт закрывается при выходе из игры: движок освобождает дерево сцены, и выход
    /// из него приходит сюда.
    /// </summary>
    public override void _ExitTree()
    {
        Close(_session != null ? _session.Outcome : SessionOutcome.None);

        if (_session != null && Alive.Is(_session))
            _session.OutcomeChanged -= OnOutcomeChanged;

        base._ExitTree();
    }

    private void OnOutcomeChanged(int outcome) => Close((SessionOutcome)outcome);

    private void Close(SessionOutcome outcome)
    {
        if (_closed || !_recorder.Active)
            return;

        _closed = true;
        _recorder.Stop(Tick, Seconds, outcome);
    }

    public override void Step(double dt)
    {
        float interval = SampleStep;

        _elapsed += (float)dt;

        if (_elapsed < interval)
            return;

        _elapsed -= interval;
        Sample();
    }

    private void Sample()
    {
        Collect();
        Publish();
        WriteWaves();

        _recorder.Sample(Tick, Seconds, _values);

        Tick++;
    }

    /// <summary>
    /// Опрос всех поставщиков величин. Отсутствующая система не является ошибкой: сцена
    /// сессии может не содержать её вовсе, и тогда её каналы остаются нулевыми.
    /// </summary>
    private void Collect()
    {
        for (int i = 0; i < _values.Length; i++)
            _values[i] = 0f;

        Terror();
        Economy();
        Forces();
        Pressure();
    }

    private void Terror()
    {
        var terror = GM.System<TerrorSystem>();

        if (terror == null)
            return;

        Set("terror.raw", terror.Raw);
        Set("terror.smoothed", terror.Smoothed);
        Set("terror.production", terror.Production);
        Set("terror.expansion", terror.Expansion);
        Set("terror.army", terror.Army);
        Set("terror.time", terror.Time);
    }

    /// <summary>
    /// Экономика берётся из баланса кадра, а запас — из проекции. Спрос и расход различаются
    /// при просадке производительности, поэтому пишутся оба: по одному лишь расходу нельзя
    /// понять, сколько работы игра не выполнила.
    /// </summary>
    private void Economy()
    {
        var ledger = GM.Economy;

        Set("metal.income", ledger.MetalIncome);
        Set("metal.demand", ledger.MetalDemand);
        Set("metal.spending", ledger.MetalSpending);
        Set("energy.income", ledger.EnergyIncome);
        Set("energy.demand", ledger.EnergyDemand);
        Set("energy.spending", ledger.EnergySpending);

        Set("efficiency.total", ledger.Efficiency);
        Set("efficiency.metal", ledger.MetalEfficiency);
        Set("efficiency.energy", ledger.EnergyEfficiency);

        var stockpile = GM.Stockpile;

        if (stockpile == null)
            return;

        Set("metal.stored", stockpile.Get(ResourceKind.Metal));
        Set("metal.capacity", stockpile.Capacity(ResourceKind.Metal));
        Set("energy.stored", stockpile.Get(ResourceKind.Energy));
        Set("energy.capacity", stockpile.Capacity(ResourceKind.Energy));
    }

    /// <summary>
    /// Состав сторон одним проходом на сторону. Строители отбираются тем же признаком, что
    /// и выделение с клавиатуры (<see cref="SelectionFilter.Builders"/>): иначе счётчик
    /// показывал бы не то множество, которое игрок считает своими строителями.
    /// </summary>
    private void Forces()
    {
        int army = 0;
        float armyPower = 0f;
        int builders = 0;
        int busy = 0;

        foreach (var unit in GM.Units[Faction.Player])
        {
            var definition = unit.Definition;

            if (definition == null)
                continue;

            armyPower += definition.ArmyPower;

            if (unit.SelectionGroup == SelectionGroup.Army)
                army++;

            if (SelectionFilter.Builders.Matches(unit))
            {
                builders++;

                if (!unit.Orders.Idle)
                    busy++;
            }
        }

        Set("army.count", army);
        Set("army.power", armyPower);
        Set("builders.count", builders);
        Set("builders.busy", busy);

        int enemies = 0;
        float enemyPower = 0f;

        foreach (var unit in GM.Units[Faction.Hostile])
        {
            enemies++;
            enemyPower += unit.Definition?.ArmyPower ?? 0f;
        }

        Set("enemy.count", enemies);
        Set("enemy.power", enemyPower);

        Set("buildings.count", GM.Index.All<Building>().Count);
        Set("buildings.sites", GM.Index.All<WorkNode>().Count);

        var combat = GM.Combat;

        if (combat == null)
            return;

        Set("combat.spawned", combat.EnemiesSpawned);
        Set("combat.destroyed", combat.EnemiesDestroyed);
        Set("combat.losses", combat.LossesTaken);
    }

    private void Pressure()
    {
        if (GM.System<PressureSystem>() is { } pressure)
        {
            Set("pressure.budget", pressure.Budget);
            Set("pressure.used", pressure.Used);
        }

        if (GM.System<WaveSystem>() is { } waves)
        {
            Set("wave.terror", waves.Terror);
            Set("wave.budget", waves.HasLaunched ? waves.Budget : 0f);
        }
    }

    /// <summary>
    /// Замеры уходят в журнал документами — оттуда их забирает <see cref="TimeSeriesProjection"/>,
    /// а из неё читают и панель террора, и экран отчёта. Прямая запись в проекцию нарушила бы
    /// общее правило: производное состояние меняется только через документы.
    /// </summary>
    private void Publish()
    {
        var channels = MetricChannels.All;

        for (int i = 0; i < channels.Count; i++)
        {
            GM.Events.Append(new MetricSampled
            {
                Channel = channels[i].Id,
                Value = _values[i],
                Tick = Tick,
            });
        }
    }

    /// <summary>
    /// Запуски волн уходят в отчёт отдельными строками: волна существует не в каждый такт,
    /// а лишь в один, и рядом такое хранить нельзя. Новые записи опознаются по длине истории —
    /// она только растёт.
    /// </summary>
    private void WriteWaves()
    {
        if (!_recorder.Active)
            return;

        var waves = GM.System<WaveSystem>();

        if (waves == null)
            return;

        var history = waves.History;

        for (; _wavesWritten < history.Count; _wavesWritten++)
        {
            var record = history[_wavesWritten];

            _recorder.Event(Tick, Seconds, "wave", new JsonObject
            {
                ["wave"] = record.WaveId,
                ["game_time"] = Mathf.Round(record.GameTime * 100f) / 100f,
                ["terror"] = Mathf.Round(record.Terror * 100f) / 100f,
                ["budget"] = Mathf.Round(record.Budget * 100f) / 100f,
                ["spent"] = Mathf.Round(record.Spent * 100f) / 100f,
                ["composition"] = record.Composition,
            });
        }
    }

    private void Set(string channel, float value)
    {
        int index = MetricChannels.IndexOf(channel);

        if (index >= 0)
            _values[index] = value;
    }

    public override void CaptureSnapshot(JsonObject data)
    {
        data["tick"] = Tick;
        data["seconds"] = Mathf.Round(Seconds * 10f) / 10f;
        data["report"] = _recorder.Active;
        data["dropped"] = _recorder.Dropped;
    }
}
