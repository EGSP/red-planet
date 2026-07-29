using Godot;

/// <summary>
/// Композиционный корень: держит журнал, регистры, справочники, сетку и планировщик.
/// Автозагрузка — единая точка, где видны все зависимости.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager I { get; private set; }

    /// <summary>Журнал документов — шина, через которую системы говорят друг с другом.</summary>
    public EventStore Events { get; } = new();

    /// <summary>Производное состояние, собранное из документов.</summary>
    public ProjectionStore Projections { get; } = new();

    public Catalog Catalog { get; } = new();
    public EntityStore Entities { get; } = new();

    /// <summary>Всё живое в мире и разрезы над ним — так системы находят, с кем работать.</summary>
    public Index Index { get; } = new();

    public WorldGrid Grid { get; } = new();
    public Scheduler Scheduler { get; } = new();

    /// <summary>Баланс текущего кадра: доход, спрос и производительность базы.</summary>
    public EconomyLedger Economy { get; } = new();

    /// <summary>Фабрика сущностей — единственное место, где рождаются объекты мира.</summary>
    public Spawner Spawn { get; private set; }

    /// <summary>Контейнер игровых сущностей в мире, назначает Main.</summary>
    public Node2D WorldRoot { get; set; }

    /// <summary>Коммандер игрока — цель приказов.</summary>
    public Commander Commander { get; set; }

    /// <summary>Система приказов — к ней обращается интерфейс строительства.</summary>
    public CommandSystem Command { get; set; }

    /// <summary>
    /// Цели, разложенные по сторонам. Самый горячий разрез в игре: в него смотрит
    /// каждый ствол на каждом выстреле и каждый снаряд на каждом кадре полёта.
    /// </summary>
    public KeySlice<Faction, IDamageable> Targets { get; private set; }

    private int _lastEntityId;

    public int NewId() => ++_lastEntityId;

    public override void _EnterTree()
    {
        I = this;

        Spawn = new Spawner(this);
        Catalog.LoadAll();

        // Постоянные разрезы заводим здесь же, где и проекции: состав производного
        // состояния должен быть виден в одном месте, а не всплывать по коду систем
        Targets = Index.SliceBy<IDamageable, Faction>(target => target.Faction);

        // Порядок держим явным: если проекция читает другую, зависимость идёт раньше.
        // Сначала собираем состав, потом подписываем — тогда они могут ссылаться друг на друга.
        Projections.Add(new StockpileProjection());
        Projections.Add(new CombatProjection());
        Projections.SubscribeAll(Events);
    }

    public override void _PhysicsProcess(double dt)
    {
        Scheduler.RunFrame(dt);

        // Состав мира меняется здесь, после всех систем: рождённое за кадр входит в разрезы
        // разом, погибшее разом выметается. Иначе одни системы видели бы новичка, а другие нет
        Index.Sweep();

        Events.ClearTransient();
    }

    /// <summary>Ярлык к самой ходовой проекции — общему хранилищу базы.</summary>
    public StockpileProjection Stockpile => Projections.Get<StockpileProjection>();

    /// <summary>Счёт боя: пришло, уничтожено, потеряно.</summary>
    public CombatProjection Combat => Projections.Get<CombatProjection>();
}
