using System.Collections.Generic;
using Godot;

/// <summary>
/// Композиционный корень сессии: держит журнал, регистры, справочники, сетку и планировщик,
/// а системы висят на нём детьми — так «система принадлежит менеджеру» перестаёт быть
/// договорённостью на словах и становится фактом дерева сцены.
///
/// ПОЧЕМУ НЕ АВТОЗАГРУЗКА. Раньше менеджер был именно ею, и срок его жизни равнялся сроку
/// жизни приложения, тогда как системы и мир пересоздавались вместе со сценой. Стоило бы
/// появиться перезапуску сессии — и в новый мир перешли бы старый склад, занятые клетки
/// и разошедшийся счётчик идентификаторов. Теперь менеджер живёт ровно столько, сколько
/// сессия: снесли ветку <see cref="Session"/> — и всё производное состояние ушло с ней.
/// Заодно даром досталась пауза: <c>ProcessMode</c> на этой ветке останавливает кадр
/// симуляции, не трогая меню и камеру.
///
/// Статическая ссылка <see cref="I"/> осталась как удобный ярлык для сущностей мира, но
/// теперь она отпускается при выходе из дерева — и только если уходит текущий владелец.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager I { get; private set; }

    /// <summary>Мир сессии: сюда Spawner складывает всё рождённое.</summary>
    [Export] public Playground Playground;

    /// <summary>Журнал документов — шина, через которую системы говорят друг с другом.</summary>
    public EventStore Events { get; } = new();

    /// <summary>Производное состояние, собранное из документов.</summary>
    public ProjectionStore Projections { get; } = new();

    /// <summary>Справочники живут дольше сессии — они лежат в автозагрузке контента.</summary>
    public Catalog Catalog => Content.Catalog;

    public EntityStore Entities { get; } = new();

    /// <summary>Всё живое в мире и разрезы над ним — так системы находят, с кем работать.</summary>
    public Index Index { get; } = new();

    public WorldGrid Grid { get; } = new();
    public Scheduler Scheduler { get; } = new();

    /// <summary>Баланс текущего кадра: доход, спрос и производительность базы.</summary>
    public EconomyLedger Economy { get; } = new();

    /// <summary>Фабрика сущностей — единственное место, где рождаются объекты мира.</summary>
    public Spawner Spawn { get; private set; }

    /// <summary>Коммандер игрока — цель приказов.</summary>
    public Commander Commander { get; set; }

    /// <summary>
    /// Система по типу: <c>GM.System&lt;CommandSystem&gt;()</c>. Раньше каждая система,
    /// нужная другим, получала здесь собственное свойство, и корень разрастался с каждой
    /// новой. Ответом может быть null — систему в сцену могли и не положить.
    /// </summary>
    public T System<T>() where T : GameSystem => Scheduler.Get<T>();

    /// <summary>Ярлык к самой спрашиваемой системе — к ней обращается интерфейс строительства.</summary>
    public CommandSystem Command => System<CommandSystem>();

    /// <summary>
    /// Вторая фаза инициализации пройдена. По этому признаку система, добавленная
    /// в дерево уже после сборки сессии, связывается сразу при регистрации.
    /// </summary>
    internal bool SystemsLinked { get; private set; }

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

        // Площадка — сестринская ветка, и к этому мигу она уже собрана: дерево сцены
        // создаётся целиком до того, как хоть кто-то в нём получит _EnterTree
        Playground ??= GetNodeOrNull<Playground>("../Playground");

        if (Playground == null)
            GD.PushError("[GameManager] не найдена площадка мира");

        Spawn = new Spawner(this);

        // Постоянные разрезы заводим здесь же, где и проекции: состав производного
        // состояния должен быть виден в одном месте, а не всплывать по коду систем
        Targets = Index.SliceBy<IDamageable, Faction>(target => target.Faction);

        // Порядок держим явным: если проекция читает другую, зависимость идёт раньше.
        // Сначала собираем состав, потом подписываем — тогда они могут ссылаться друг на друга.
        Projections.Add(new StockpileProjection());
        Projections.Add(new CombatProjection());
        Projections.Add(new TimeSeriesProjection());
        Projections.SubscribeAll(Events);
    }

    /// <summary>
    /// Вторая фаза инициализации систем. Наш _Ready приходит после _Ready всех дочерних
    /// узлов, поэтому здесь состав систем заведомо полон — это и есть та единственная
    /// общая точка «все зарегистрированы», ради которой иначе понадобился бы пул
    /// с разбором графа зависимостей. Взаимные ссылки работают сами собой.
    /// </summary>
    public override void _Ready()
    {
        // Список копируем: связывание может добавить систему в дерево, и тогда обход
        // живого состава сорвался бы прямо посреди фазы
        var systems = new List<GameSystem>(Scheduler.Systems);

        foreach (var system in systems)
            system.Link();

        SystemsLinked = true;
    }

    /// <summary>
    /// Сверка на «текущего владельца» обязательна: при пересборке сессии новый менеджер
    /// успевает встать на место раньше, чем догорит старый, и безусловное обнуление
    /// стёрло бы живую ссылку.
    /// </summary>
    public override void _ExitTree()
    {
        if (I == this)
            I = null;
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

    /// <summary>Ряды наблюдаемых величин по ходу партии: террор, а позже и доход.</summary>
    public TimeSeriesProjection Metrics => Projections.Get<TimeSeriesProjection>();
}
