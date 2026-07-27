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
    public WorldGrid Grid { get; } = new();
    public Scheduler Scheduler { get; } = new();

    /// <summary>Фабрика сущностей — единственное место, где рождаются объекты мира.</summary>
    public Spawner Spawn { get; private set; }

    /// <summary>Контейнер игровых сущностей в мире, назначает Main.</summary>
    public Node2D WorldRoot { get; set; }

    /// <summary>Коммандер игрока — цель приказов.</summary>
    public Commander Commander { get; set; }

    /// <summary>Система приказов — к ней обращается интерфейс строительства.</summary>
    public CommandSystem Command { get; set; }

    private int _lastEntityId;

    public int NewId() => ++_lastEntityId;

    public override void _EnterTree()
    {
        I = this;

        Spawn = new Spawner(this);
        Catalog.LoadAll();

        // Порядок держим явным: если проекция читает другую, зависимость идёт раньше.
        // Сначала собираем состав, потом подписываем — тогда они могут ссылаться друг на друга.
        Projections.Add(new StockpileProjection());
        Projections.Add(new CombatProjection());
        Projections.SubscribeAll(Events);
    }

    public override void _PhysicsProcess(double dt)
    {
        Scheduler.RunFrame(dt);
        Events.ClearTransient();
    }

    /// <summary>Ярлык к самой ходовой проекции — общему хранилищу базы.</summary>
    public StockpileProjection Stockpile => Projections.Get<StockpileProjection>();

    /// <summary>Счёт боя: пришло, уничтожено, потеряно.</summary>
    public CombatProjection Combat => Projections.Get<CombatProjection>();
}
