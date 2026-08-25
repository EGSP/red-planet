using System.Collections.Generic;
using Godot;

/// <summary>
/// Приказы игрока, выделение и режим строительства.
/// Клиент отдаёт намерения, применяет их симуляция — привычка на будущее.
///
/// Исполняется в графическом цикле (<see cref="UpdateCycle.Process"/>) после
/// <see cref="CursorSystem"/>: курсор, рамка и призрак застройки должны совпадать
/// с отрисовкой и движением камеры, а не с шагом физики. Симуляция от этого не учащается.
/// Мировые координаты мыши считаются только в <see cref="CursorSystem"/>.
///
/// ЧТО ПРИКАЗАТЬ, РЕШАЕТ ЦЕЛЬ ПОД КУРСОРОМ, а не заранее выбранный режим: щёлкнули по врагу —
/// атака, по каркасу или плану — стройка, по повреждённому своему — ремонт, по здоровому
/// своему — сопровождение с помощью, по земле — движение. Одна кнопка на всё, как в PA.
///
/// КАК ПРИКАЗАТЬ, РЕШАЕТ ЖЕСТ. Нажатие правой кнопки начинает <see cref="OrderGesture"/>,
/// а вид приказа задаёт способ указания: круг у атаки и помощи строительству, линия
/// у движения, щелчок у остального. Режим приказа на это не влияет — он решает лишь, откуда
/// взят вид: из клавиши или из цели под указателем.
///
/// КОМУ приказ уйдёт, решает выделение, а вид приказа отсеет набор самой сущности: копателю
/// не уйдёт атака, турели — движение. Поэтому здесь не нужно разбираться, кто выделен, —
/// достаточно предложить приказ каждому.
///
/// ЧТО ВЫНЕСЕНО ОТСЮДА. Система держит состояние ввода, разбор нажатий, выделение и выдачу
/// приказов; всё, что имеет собственный предмет, живёт рядом отдельными сущностями:
/// <see cref="OrderGesture"/> — что нарисовано между нажатием и отпусканием,
/// <see cref="CursorTargets"/> — что лежит под указателем и во что это превращается,
/// <see cref="Assignment"/> — как один приказ достаётся отряду с разными очередями.
/// Ни одна из них о состоянии ввода не знает, поэтому проверять их можно по отдельности.
/// </summary>
public partial class CommandSystem : GameSystem
{
    [Export] public PackedScene BlueprintScene;

    /// <summary>
    /// Промежуток между щелчками, в который повтор считается двойным выбором, секунды.
    /// Вынесено в инспектор: привычный темп у игроков разный, а системная настройка
    /// двойного щелчка движку недоступна.
    /// </summary>
    [Export] public float DoubleTapInterval = 0.35f;

    /// <summary>Дальше этого протаскивания клик считается рамкой, а не выбором одного.</summary>
    private const float BandThreshold = 8f;

    /// <summary>Как часто пересчитывается вид курсора в обычном состоянии, секунды.</summary>
    private const float CursorPollInterval = 0.05f;

    /// <summary>Сколько осталось до следующего пересчёта курсора.</summary>
    private float _cursorPoll;

    private CursorSystem _cursor;
    private PlacementGhost _ghost;
    private OrderOverlay _overlay;

    // Служебная графика отладки. Заводится здесь же, где призрак и очереди приказов:
    // все они живут в слоях мира и создаются один раз при сборке площадки
    private NavGridOverlay _navigation;
    private PathOverlay _paths;
    private BoidsOverlay _boids;

    private SpatialOverlay _spatial;
    private DebugDraw _debugDraw;

    private readonly List<IOrderable> _selected = new();

    /// <summary>
    /// Двойной выбор. Про мышь не знает: любой другой источник — палец, геймпад,
    /// горячая клавиша — зовёт тот же Register со своей целью.
    /// </summary>
    private readonly DoubleTap _doubleTap = new();

    /// <summary>Боевые группы. Читает полоса групп внизу экрана.</summary>
    public ControlGroups Groups { get; } = new();

    private Vector2 _bandStart;

    /// <summary>
    /// Чем занят ввод прямо сейчас. Читают наведение и отрисовка рамки; сам разбор
    /// нажатий ведётся по нему же, а не по порядку ветвей.
    /// </summary>
    public CommandState State { get; private set; } = CommandState.Idle;

    /// <summary>Вид приказа, выбранный клавишей. Значим только в состоянии Aiming.</summary>
    private OrderKind _aimed;

    /// <summary>
    /// Жест указания: что игрок растягивает или рисует прямо сейчас. Значим в состояниях
    /// Sweeping и Drawing, а между жестами хранит пустоту.
    /// </summary>
    private readonly OrderGesture _gesture = new();

    /// <summary>Разбор того, что лежит под указателем. Заводится при связывании системы.</summary>
    private CursorTargets _targets;

    /// <summary>
    /// Вид приказа, который отдаст нажатие прямо сейчас, либо null. Читает панель приказов:
    /// форма курсора — не единственный признак того, что произойдёт, подсветка в панели
    /// говорит то же самое словами.
    ///
    /// ВО ВРЕМЯ ЖЕСТА ЭТО ВИД ЖЕСТА, А НЕ ВЫБОР КЛАВИШЕЙ. Жест начинается и в режиме приказа,
    /// и без него — тогда вид выведен из цели под указателем, — а показать игроку нужно
    /// то, что сейчас указывается, независимо от того, откуда вид взялся.
    /// </summary>
    public OrderKind? Aimed => State switch
    {
        CommandState.Aiming => _aimed,
        CommandState.Sweeping or CommandState.Drawing => _gesture.Kind,
        _ => null,
    };

    /// <summary>Идёт ли жест указания области или линии.</summary>
    private bool Gesturing => State is CommandState.Sweeping or CommandState.Drawing;

    /// <summary>Центр указываемой области. Читает оверлей, пока идёт жест.</summary>
    public Vector2 AreaCenter => _gesture.Anchor;

    /// <summary>Радиус указываемой области прямо сейчас.</summary>
    public float AreaRadius => _cursor == null
        ? 0f
        : _gesture.RadiusTo(_cursor.VisualWorldPosition);

    /// <summary>
    /// Достаточно ли растянута область, чтобы приказ ушёл областью, а не точкой. Читает
    /// предпоказ: круг, который по отпусканию превратится в щелчок, обязан выглядеть иначе,
    /// иначе игрок узнаёт о пороге только по итогу.
    /// </summary>
    public bool AreaReady => AreaRadius >= OrderGesture.MinRadius;

    /// <summary>Нарисованная линия для отрисовки. Тот же список, что и у выдачи приказа.</summary>
    public IReadOnlyList<Vector2> Path => _gesture.Path;

    /// <summary>Места вдоль линии для указанного числа исполнителей — предпоказ строя.</summary>
    public IReadOnlyList<Vector2> Spots(int count) => _gesture.Spread(count);

    /// <summary>
    /// Сколько выделенных примут приказ движения. Читает предпоказ линии: мест на ней
    /// столько, сколько исполнителей приказ получит, а не сколько их выделено вообще.
    /// </summary>
    public int MoverCount
    {
        get
        {
            int count = 0;

            foreach (var actor in _selected)
                if (actor.Orders.Allows(OrderKind.Move))
                    count++;

            return count;
        }
    }

    /// <summary>Что выбрано в строительной панели. Держится до отмены выбора.</summary>
    public UnitDefinition Pending { get; private set; }

    /// <summary>
    /// Сторона, за которую расставляет песочница, либо null у обычной постройки.
    ///
    /// ПОЧЕМУ ПРИЗНАК ЖИВЁТ ЗДЕСЬ. Песочница отличается от постройки только тем, что
    /// происходит по отпусканию кнопки: вместо плана в мир сразу входит готовая сущность,
    /// и получатель приказа ей не нужен. Всё остальное — призрак, раскладка, поворот,
    /// серия под Shift, отмена — совпадает дословно, и заводить ради песочницы второй разбор
    /// нажатий значило бы держать две реализации одного жеста, которые разойдутся.
    /// </summary>
    private Faction? _sandbox;

    /// <summary>Поставлен ли хоть один каркас с нынешнего выбора.</summary>
    private bool _placed;

    /// <summary>
    /// Точка, где нажали кнопку, пока её держат. Она задаёт и место первой постройки,
    /// и начало вектора, по которому считаются угол и раскладка.
    /// </summary>
    private Vector2 _buildAnchor;

    /// <summary>План застройки: то, что нарисовано призраком, и то, что встанет по отпусканию.</summary>
    private readonly List<BuildSpot> _plan = new();

    /// <summary>
    /// Ставит ли щелчок каркас прямо сейчас. Выбор в панели и готовность строить —
    /// разные вещи: первый каркас ставится сразу, а дальше режим ждёт Shift.
    ///
    /// ПОЧЕМУ ТАК. Одиночная постройка — обычный случай, и после неё игрок хочет
    /// вернуться к управлению отрядом, а не отменять режим отдельным действием.
    /// Серия же строится под зажатым Shift — тем же, которым в очередь ставятся
    /// приказы. Shift здесь именно переключатель: нажимать и отпускать его можно
    /// сколько угодно, пока выбор в панели не снят.
    ///
    /// Клавиатура здесь спрашивается опросом, а не читается из события, потому что
    /// признак решает не переход, а показ призрака, и пересчитывается он каждый кадр.
    /// Переход же решается тем же правилом, но по модификатору самого нажатия.
    /// </summary>
    private bool ReadyToPlace =>
        State == CommandState.Placing && (!_placed || Input.IsKeyPressed(Key.Shift));

    /// <summary>Кто сейчас выделен — читают оверлей и HUD.</summary>
    public IReadOnlyList<IOrderable> Selected => _selected;

    /// <summary>
    /// Показать очереди всех своих, а не только выделенных — как CapsLock в PA.
    /// Переключается клавишей C.
    /// </summary>
    public bool ShowAllOrders { get; private set; }

    /// <summary>
    /// Рамка для отрисовки: конец берётся из визуальной позиции курсора, чтобы при
    /// включённом прогнозе рамка совпадала с призраком, а не с запаздывающей фактической
    /// точкой. Игровое завершение рамки передаёт точную позицию события отдельно.
    /// </summary>
    public Rect2 Band => _cursor == null
        ? default
        : new Rect2(_bandStart, _cursor.VisualWorldPosition - _bandStart).Abs();

    /// <summary>
    /// Вторая фаза: площадка мира к этому мигу собрана, поэтому служебную графику
    /// заводим здесь, один раз, а не проверяем её наличие в каждом кадре.
    /// Проверка в EnsureNodes при этом остаётся: ноды могут быть освобождены позже.
    /// </summary>
    protected override void OnLink()
    {
        _cursor = GM.System<CursorSystem>();

        if (_cursor == null)
            GD.PushError("[CommandSystem] CursorSystem не найдена: мировые координаты мыши недоступны");

        _doubleTap.Interval = DoubleTapInterval;
        _targets = new CursorTargets(GM);

        if (GM.Playground != null)
            EnsureNodes();
    }

    /// <summary>
    /// Выбор постройки в панели. Зовёт <see cref="Buildbar"/>, поэтому состояние ставится
    /// безусловно: щелчок по панели до систем не доходит, и жест мышью в мире к этому мигу
    /// не начат.
    /// </summary>
    public void BeginBuild(UnitDefinition def) => Begin(def, null);

    /// <summary>
    /// Выбор вида в панели песочницы: тот же режим постановки, но по отпусканию кнопки
    /// вместо плана в мир входит готовая сущность указанной стороны.
    /// </summary>
    public void BeginSandbox(UnitDefinition def, Faction faction) => Begin(def, faction);

    private void Begin(UnitDefinition def, Faction? sandbox)
    {
        Pending = def;
        _sandbox = sandbox;
        _placed = false;
        State = CommandState.Placing;
        EnsureNodes();
        _ghost.Definition = def;
        _ghost.Visible = true;
    }

    /// <summary>
    /// Снять выбор песочницы, если он есть. Зовёт панель песочницы при закрытии: выбор,
    /// переживший панель, ставил бы юнитов по щелчку без всякого объяснения на экране.
    /// Обычный выбор постройки не трогается — его сделала строительная панель.
    /// </summary>
    public void CancelSandbox()
    {
        if (_sandbox == null)
            return;

        CancelBuild();

        if (State is CommandState.Placing or CommandState.Laying)
            State = CommandState.Idle;
    }

    /// <summary>
    /// Убрать всё, что относится к постройке. Состояния не меняет: переход — дело того
    /// места, где он решается, и делать его заодно с уборкой значило бы иметь два
    /// источника состояния.
    /// </summary>
    private void CancelBuild()
    {
        Pending = null;
        _sandbox = null;
        _placed = false;
        _plan.Clear();

        if (_ghost != null)
        {
            _ghost.Visible = false;
            _ghost.StretchRadius = 0f;
        }
    }

    /// <summary>
    /// Отменить текущее состояние — по шагу за вызов: сначала режим постройки, затем
    /// выделение. Возвращает false, когда отменять было нечего.
    ///
    /// Escape ловит меню паузы и спрашивает нас первыми: пока есть что сбрасывать,
    /// клавиша работает по-старому, и только на пустом месте открывает паузу.
    /// Отсюда и пошаговость — сбрасывать разом режим и выделение значило бы, что игрок,
    /// отменяя постройку, заодно теряет отряд.
    /// </summary>
    public bool CancelContext()
    {
        switch (State)
        {
            // Раскладка отменяется, а выбор в панели остаётся: следующее нажатие начинает
            // её заново. Прежде Escape снимал заодно и выбор, тогда как правая кнопка
            // в том же положении его сохраняла; поведение приведено к одному
            case CommandState.Laying:
                _plan.Clear();
                State = CommandState.Placing;
                return true;

            case CommandState.Placing:
                CancelBuild();
                State = CommandState.Idle;
                return true;

            // Жест отменяется, а режим приказа остаётся, если он был включён: следующее
            // нажатие начинает указание заново — то же правило, что у раскладки построек.
            // Жест, начатый без режима, возвращает в обычное состояние: возвращаться некуда
            case CommandState.Sweeping:
            case CommandState.Drawing:
                State = _gesture.Forced ? CommandState.Aiming : CommandState.Idle;
                _gesture.End();
                return true;

            case CommandState.Aiming:
                State = CommandState.Idle;
                return true;

            case CommandState.Banding:
                State = CommandState.Idle;
                return true;
        }

        if (_selected.Count > 0)
        {
            ClearSelection();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Снять выделение целиком. Выбранная группа при этом перестаёт быть выбранной:
    /// подсветка в полосе групп обязана показывать то, что действительно выделено.
    /// Сами группы не трогаются — они переживают любую смену выделения.
    /// </summary>
    private void ClearSelection()
    {
        _selected.Clear();
        Groups.Current = -1;
    }

    /// <summary>
    /// Заменить исполнителя преемником в выделении и боевых группах.
    ///
    /// Каркас достраивается в готовую сущность: ветка приказов и очередь производства
    /// уже переехали через <see cref="Blueprint.Bequeath"/>, а выделение и группы обязаны
    /// переехать вместе с ними. Иначе строительная панель завода гаснет в миг готовности,
    /// хотя состав заказов у преемника тот же.
    ///
    /// Если преемник уже выделен (или уже состоит в том же слоте группы), каркас просто
    /// снимается — дублировать одного исполнителя нельзя.
    /// </summary>
    public void Succeed(IOrderable from, IOrderable to)
    {
        if (from == null || to == null || ReferenceEquals(from, to))
            return;

        for (int i = 0; i < _selected.Count; i++)
        {
            if (!ReferenceEquals(_selected[i], from))
                continue;

            if (_selected.Contains(to))
                _selected.RemoveAt(i);
            else
                _selected[i] = to;

            break;
        }

        Groups.Succeed(from, to);
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null || _cursor == null)
            return;

        EnsureNodes();

        // Выделенное могло погибнуть или достроиться — держим списки живыми
        _selected.RemoveAll(actor => !Alive.Is(actor as Node));
        Groups.Sweep();

        // Выделение могло погибнуть целиком: приказывать стало некому, и ни режим приказа,
        // ни начатый жест держаться не на чем
        if (_selected.Count == 0 && (Gesturing || State == CommandState.Aiming))
        {
            _gesture.End();
            State = CommandState.Idle;
        }

        // Условия показа областей: Ctrl при выделении и покрытие турелей при стройке
        GizmoGate.Refresh(_selected, Pending);

        RefreshCursor(dt);

        if (State == CommandState.Drawing)
            TrackDrawing();

        if (Pending == null)
            return;

        // Призрак показывает не выбор, а готовность поставить: пока режим спит,
        // место под курсором не подсвечивается, и щелчок обещает обычное выделение
        _ghost.Visible = ReadyToPlace || State == CommandState.Laying;

        if (!_ghost.Visible)
        {
            _ghost.StretchRadius = 0f;
            return;
        }

        // Представление читает визуальную позицию: при прогнозе призрак упреждает задержку,
        // при движении камеры без мыши точку уже пересчитал CursorSystem
        var visual = _cursor.VisualWorldPosition;
        var anchor = State == CommandState.Laying ? _buildAnchor : visual;
        bool alt = Input.IsKeyPressed(Key.Alt);

        BuildLayout.Compute(GM, Pending, anchor, visual, alt, _plan, SandboxPattern(alt));
        UpdateStretchGhost(anchor, visual, alt);

        _ghost.QueueRedraw();
    }

    /// <summary>
    /// Обновить курсор, но не в каждом кадре.
    ///
    /// ПОЧЕМУ С ПРОМЕЖУТКОМ. В обычном состоянии вид курсора выводится из того, что лежит
    /// под указателем, а разбор цели обходит врагов, планы, своих и всё выделяемое — пять
    /// проходов по разрезам. Делать их шестьдесят раз в секунду ради картинки, которую глаз
    /// всё равно не различит чаще двадцати, было бы платой ни за что. Режим приказа при этом
    /// отзывается сразу: там разбирать нечего, вид уже выбран.
    /// </summary>
    private void RefreshCursor(double dt)
    {
        if (Aimed is { } aimed)
        {
            _cursorPoll = 0f;
            _cursor.Kind = CursorArt.Of(aimed);
            return;
        }

        _cursorPoll -= (float)dt;

        if (_cursorPoll > 0f)
            return;

        _cursorPoll = CursorPollInterval;
        _cursor.Kind = DesiredCursor();
    }

    /// <summary>
    /// Круг охвата залежей у призрака. Радиус и центр совпадают с тем, что считает
    /// <see cref="BuildLayout"/> для <see cref="BuildPattern.MetalArea"/>: иначе игрок
    /// видел бы помеченные экстракторы, но не границу, по которой они отобраны.
    /// </summary>
    private void UpdateStretchGhost(Vector2 anchor, Vector2 cursor, bool alt)
    {
        _ghost.StretchRadius = 0f;

        if (BuildLayout.PatternOf(Pending, alt, SandboxPattern(alt)) != BuildPattern.MetalArea)
            return;

        float radius = anchor.DistanceTo(cursor);

        if (radius < BuildLayout.AngleThreshold || _plan.Count == 0)
            return;

        _ghost.StretchCenter = _plan[0].Center;
        _ghost.StretchRadius = radius;
    }

    /// <summary>
    /// Разбор ввода ведётся ПО СОСТОЯНИЮ, а не по порядку ветвей. Прежде правило «нажатие
    /// в режиме постройки начинает раскладку, а не рамку» нигде не было записано — оно
    /// следовало из того, что одна строка стояла выше другой. Теперь у каждого состояния
    /// свой разбор, и что означает кнопка, видно прямо в нём.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (GM?.Playground == null || _cursor == null)
            return;

        if (@event is InputEventKey key)
        {
            if (HandleKey(key))
                GetViewport().SetInputAsHandled();

            return;
        }

        if (@event is not InputEventMouseButton mouse)
            return;

        // Кнопки берут точную позицию события и никогда не прогнозируемую:
        // приказ обязан уйти туда, куда ткнули
        var point = _cursor.WorldFromEvent(mouse);

        switch (State)
        {
            case CommandState.Idle:
                IdleInput(mouse, point);
                break;

            case CommandState.Banding:
                BandingInput(mouse, point);
                break;

            case CommandState.Placing:
                PlacingInput(mouse, point);
                break;

            case CommandState.Laying:
                LayingInput(mouse, point);
                break;

            case CommandState.Aiming:
                AimingInput(mouse, point);
                break;

            case CommandState.Sweeping:
                SweepingInput(mouse, point);
                break;

            case CommandState.Drawing:
                DrawingInput(mouse, point);
                break;
        }
    }

    // ── разбор по состояниям ───────────────────────────────────────────────────

    private void IdleInput(InputEventMouseButton mouse, Vector2 point)
    {
        // Рамку начинаем сразу: одиночный щелчок — её вырожденный случай, и различаются
        // они только тем, сколько мышь успела проехать
        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _bandStart = point;
            State = CommandState.Banding;
            return;
        }

        // Правая кнопка начинает жест и без выбранного режима: вид выводится из цели
        // под указателем, а способ указания у вида свой — щелчок, круг или линия
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
            BeginGesture(ContextKind(point), point, mouse.ShiftPressed, forced: false);
    }

    /// <summary>
    /// Вид приказа, выведенный из цели под указателем, для начала жеста. Пустота означает,
    /// что приказывать некому или нечем, и вырождается в движение: жест всё равно не найдёт
    /// получателя, а разбирать этот случай отдельно значило бы завести четвёртую ветвь ради
    /// ничего.
    /// </summary>
    private OrderKind ContextKind(Vector2 point) =>
        _targets.KindAt(point, Recipients()) ?? OrderKind.Move;

    private void BandingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            FinishBand(point, mouse.ShiftPressed);
            State = CommandState.Idle;
            return;
        }

        // Правая кнопка посреди рамки отменяет её. Прежде она выдавала приказ, а рамка
        // оставалась висеть до отпускания левой, — но это следовало из порядка ветвей,
        // а не из решения
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
            State = CommandState.Idle;
    }

    private void PlacingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            // Постройка ставится не по нажатию, а по отпусканию: между ними игрок задаёт
            // угол и раскладку, и до отпускания решение не принято
            if (!_placed || mouse.ShiftPressed)
            {
                _buildAnchor = point;
                State = CommandState.Laying;
                return;
            }

            // Уснувший выбор панели снимается этим же щелчком: игрок вернулся
            // к управлению отрядом, и держать за ним постройку больше незачем
            CancelBuild();
            _bandStart = point;
            State = CommandState.Banding;
            return;
        }

        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            CancelBuild();
            State = CommandState.Idle;
        }
    }

    private void LayingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            PlaceBatch(point, mouse.ShiftPressed);
            State = CommandState.Placing;
            return;
        }

        // Правая кнопка посреди протаскивания отменяет только его: выбор в панели
        // остаётся, и следующее нажатие начинает раскладку заново
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _plan.Clear();
            State = CommandState.Placing;
        }
    }

    /// <summary>
    /// Режим приказа: ПРАВАЯ кнопка указывает цель, ЛЕВАЯ отменяет режим.
    ///
    /// РАСПРЕДЕЛЕНИЕ ТО ЖЕ, ЧТО И БЕЗ РЕЖИМА, и в этом весь смысл. В обычном управлении
    /// правая кнопка заставляет действовать, левая — выделяет и сбрасывает; поменяй их
    /// местами в режиме приказа — и рука игрока, привыкшая отдавать приказы правой, стала бы
    /// отменять то, что он только что выбрал. Режим меняет, КАКОЙ приказ уйдёт, а не то,
    /// какой кнопкой его отдают.
    ///
    /// Левая кнопка при этом не выделяет, а только снимает режим: щелчок отменяет выбор,
    /// и понимать его вторым способом разом было бы двусмысленно.
    ///
    /// Режим снимается после выдачи, если не зажат Shift, — то же правило, что у постройки:
    /// одиночный приказ есть обычный случай, серия набирается под зажатым Shift.
    /// </summary>
    private void AimingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            BeginGesture(_aimed, point, mouse.ShiftPressed, forced: true);
            return;
        }

        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
            State = CommandState.Idle;
    }

    /// <summary>
    /// Нажатие правой кнопки начинает ЖЕСТ, а не отдаёт приказ.
    ///
    /// ПОЧЕМУ РЕШЕНИЕ ОТЛОЖЕНО ДО ОТПУСКАНИЯ. У приказа два способа указания — точка
    /// и растягивание, — и различает их то, сколько мышь проехала между нажатием
    /// и отпусканием. Пока кнопка нажата, игрок ещё не сказал, чего хочет, поэтому щелчок
    /// есть вырожденный случай жеста, ровно как одиночный выбор есть вырожденный случай рамки.
    ///
    /// РЕЖИМ ПРИКАЗА ДЛЯ ЖЕСТА НЕОБЯЗАТЕЛЕН. Вид приходит сюда либо выбранным клавишей, либо
    /// выведенным из цели под указателем, а дальше разницы нет: способ указания принадлежит
    /// самому виду (<see cref="OrderGesture.FormOf"/>), а не тому, как игрок этот вид назвал.
    /// Поэтому растянуть круг по скоплению противника можно и без нажатия клавиши атаки —
    /// требовать её значило бы, что альтернативный способ указания доступен только тому, кто
    /// сперва вошёл в режим.
    ///
    /// Виды, у которых растягивания не бывает, отдаются сразу по нажатию: ремонт
    /// и сопровождение требуют цели под указателем, и тянуть здесь нечего. Заводить жест ради
    /// того, чтобы в конце отдать тот же приказ, значило бы задержать отклик без всякой пользы.
    /// </summary>
    private void BeginGesture(OrderKind kind, Vector2 point, bool queue, bool forced)
    {
        _gesture.Begin(kind, point, queue, forced);

        switch (_gesture.Form)
        {
            case GestureForm.Line:
                State = CommandState.Drawing;
                return;

            case GestureForm.Area:
                State = CommandState.Sweeping;
                return;

            default:
                IssuePoint(point);
                Settle();
                return;
        }
    }

    /// <summary>
    /// Растягивание области: правая кнопка завершает жест, левая его отменяет.
    /// </summary>
    private void SweepingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: false })
        {
            FinishSweep(point);
            Settle();
            return;
        }

        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
            CancelGesture();
    }

    private void DrawingInput(InputEventMouseButton mouse, Vector2 point)
    {
        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: false })
        {
            FinishDrawing(point);
            Settle();
            return;
        }

        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
            CancelGesture();
    }

    /// <summary>
    /// Куда перейти после отданного приказа. Режим приказа держится под зажатым Shift
    /// и снимается без него — то же правило, что у постройки; жест, начатый без режима,
    /// в режим и не переходит: игрок его не включал.
    /// </summary>
    private void Settle()
    {
        State = _gesture.Forced && _gesture.Queue ? CommandState.Aiming : CommandState.Idle;
        _gesture.End();
    }

    /// <summary>Отказ от жеста: приказ не отдаётся, режим приказа сохраняется, если он был.</summary>
    private void CancelGesture()
    {
        State = _gesture.Forced ? CommandState.Aiming : CommandState.Idle;
        _gesture.End();
    }

    // ── клавиши ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Смысл, в котором разбирается нажатие. Вычисляется, а не хранится: отдельное поле
    /// было бы вторым источником истины, который пришлось бы согласовывать с состоянием
    /// при каждом переходе.
    /// </summary>
    private InputContext Context => State switch
    {
        CommandState.Banding or CommandState.Placing or CommandState.Laying
            or CommandState.Sweeping or CommandState.Drawing
            => InputContext.Gesture,
        CommandState.Aiming => InputContext.Aiming,
        _ => _selected.Count > 0 ? InputContext.Orders : InputContext.Selection,
    };

    /// <summary>
    /// Разбор клавиши. Возвращает true, если нажатие израсходовано, — только тогда событие
    /// помечается обработанным, и клавиша, ничего не сделавшая, достаётся другим слушателям.
    ///
    /// Боевые группы и показ очередей читаются в любом контексте: они не спорят ни с приказами,
    /// ни с отбором и означают одно и то же всегда.
    /// </summary>
    private bool HandleKey(InputEventKey key)
    {
        if (key.IsActionPressed(InputActions.ViewOrdersAll))
        {
            ShowAllOrders = !ShowAllOrders;
            return true;
        }

        // Ctrl читается у самого события, а не опросом клавиатуры: состояние клавиш
        // спрашивается в момент разбора, а событие несёт то, что было в момент нажатия
        if (InputActions.GroupSlot(key) is var slot and >= 0)
        {
            if (key.CtrlPressed)
                AssignGroup(slot);
            else
                SelectGroup(slot);

            return true;
        }

        return Context switch
        {
            InputContext.Selection => SelectByKey(key),
            InputContext.Orders or InputContext.Aiming => OrderByKey(key),
            _ => false,
        };
    }

    /// <summary>
    /// Клавиши приказов. Снос выдаётся сразу и цели не требует, остальные включают режим
    /// указания цели.
    /// </summary>
    private bool OrderByKey(InputEventKey key)
    {
        if (key.IsActionPressed(InputActions.UnitDelete))
        {
            IssueDelete();
            return true;
        }

        if (AimedKind(key) is not { } kind)
            return false;

        _aimed = kind;
        State = CommandState.Aiming;
        return true;
    }

    private static OrderKind? AimedKind(InputEventKey key)
    {
        foreach (var (kind, action) in InputActions.OrderActions)
            if (key.IsActionPressed(action))
                return kind;

        return null;
    }

    /// <summary>
    /// Клавиши отбора. Читаются только при пустом выделении: приказывать некому, и клавиша
    /// достаётся второму своему смыслу.
    ///
    /// ДОБАВЛЕНИЯ ПО SHIFT ЗДЕСЬ НЕТ, и не по недосмотру: раз отбор возможен только при
    /// пустом выделении, добавлять ему не к чему. Стоит отобрать хоть кого-то — и та же
    /// клавиша означает уже приказ, потому что смысл её решает наличие выделения, а не его
    /// состав. Иначе предсказать, что сделает нажатие, игрок бы не смог.
    /// </summary>
    private bool SelectByKey(InputEventKey key)
    {
        if (key.IsActionPressed(InputActions.SelectArmy))
        {
            SelectVisible(SelectionFilter.Army);
            return true;
        }

        if (key.IsActionPressed(InputActions.SelectBuilders))
        {
            SelectVisible(SelectionFilter.Builders);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Какой курсор показать, когда вид приказа не выбран и не указывается: тот, что уйдёт
    /// по нажатию в нынешней точке, — поэтому курсор меняется сам, стоит навести его
    /// на врага, на каркас или на подбитого своего.
    ///
    /// Во время рамки и раскладки построек курсор обычный: они показывают себя сами,
    /// а приказ в это время не отдаётся.
    /// </summary>
    private CursorKind DesiredCursor()
    {
        if (State != CommandState.Idle)
            return CursorKind.Arrow;

        // Над элементом интерфейса приказа не будет, и обещать его курсором нельзя.
        // Спрашивается тот элемент, который принимает мышь, поэтому панели
        // с MouseFilter.Ignore курсор не гасят — сквозь них мир виден по-прежнему
        if (GetViewport()?.GuiGetHoveredControl() != null)
            return CursorKind.Arrow;

        return _targets.KindAt(_cursor.ActualWorldPosition, Recipients()) is { } kind
            ? CursorArt.Of(kind)
            : CursorKind.Arrow;
    }

    // ── боевые группы ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ctrl+цифра — записать нынешнее выделение в слот. Тем же сочетанием группа
    /// и пополняется: добрали в выделение кого нужно, повторили — состав переписан.
    /// Отдельного «добавить в группу» поэтому не требуется.
    ///
    /// Слот сразу становится выбранным: игрок только что подтвердил, что держит именно
    /// этот отряд, и подсветка обязана это показать.
    /// </summary>
    private void AssignGroup(int slot)
    {
        Groups.Assign(slot, _selected);
        Groups.Current = _selected.Count > 0 ? slot : -1;
    }

    /// <summary>
    /// Цифра — выбрать группу. Выбор заменяет прежнее выделение целиком: группа и есть
    /// готовый отряд, а не добавка к тому, что под рукой.
    ///
    /// Пустой слот не трогает ничего. Стереть выделение промахом по незанятой цифре —
    /// потеря без всякой пользы, а роспуск группы делается сочетанием с Ctrl.
    /// </summary>
    private void SelectGroup(int slot)
    {
        var members = Groups.Members(slot);

        if (members.Count == 0)
            return;

        _selected.Clear();
        _selected.AddRange(members);
        Groups.Current = slot;
    }

    // ── выделение ──────────────────────────────────────────────────────────────

    private void FinishBand(Vector2 point, bool add)
    {
        // Свободное выделение и выбор группы взаимно исключают друг друга: набирая
        // отряд заново, игрок уходит от группы, и подсветка гаснет. А вот добавление
        // по Shift группу не рушит — добранные попадают в выделение, но не в состав
        // слота, пока его не перезапишут сочетанием с Ctrl
        if (!add)
            ClearSelection();

        if (_bandStart.DistanceTo(point) < BandThreshold)
        {
            var one = _targets.ActorAt(point);

            // Повтор по той же цели означает «и всех таких же». Прямое указание игрока,
            // поэтому ни родство, ни преобладание здесь не применяются
            if (_doubleTap.Register(one))
                SelectSameKind(one);
            else if (one != null && !_selected.Contains(one))
                _selected.Add(one);

            return;
        }

        var band = new Rect2(_bandStart, point - _bandStart).Abs();
        var caught = new List<IOrderable>();

        foreach (var actor in GM.Index.All<IOrderable>())
            if (CursorTargets.Commandable(actor) && band.HasPoint(actor.GlobalPosition))
                caught.Add(actor);

        // Два разных правила на два разных случая. Если игрок уже что-то держит, рамка
        // добирает родню — намерение высказано первым щелчком, и спорить с ним незачем.
        // Если выделение пустое, судить можно только по улову: рамка почти всегда
        // захватывает лишнее, поэтому оставляем преобладающий род.
        //
        // Прежнее выделение оба правила не пересматривают: отсеивается только улов
        if (_selected.Count > 0)
            SelectionGroups.KeepAkin(caught, _selected);
        else
            SelectionGroups.KeepDominant(caught);

        foreach (var actor in caught)
            if (!_selected.Contains(actor))
                _selected.Add(actor);
    }

    /// <summary>
    /// Все такие же на экране. Тип опознаём по DisplayName — по той же строке, которой
    /// подписана панель выделения.
    ///
    /// ОБЛАСТЬ ОГРАНИЧЕНА ВИДИМОЙ ЧАСТЬЮ КАРТЫ. Прежде она не ограничивалась намеренно,
    /// и «выделить всех копателей» означало всех до единого, где бы они ни были. Решение
    /// отменено: правило теперь одно на все выделения, сделанные не мышью, — жест мыши
    /// задаёт свои границы сам, а выбор по признаку их не имеет вовсе и без ограничения
    /// приводит в отряд юнитов с другого конца карты.
    /// </summary>
    private void SelectSameKind(IOrderable sample)
    {
        string kind = sample.DisplayName;

        if (!_selected.Contains(sample))
            _selected.Add(sample);

        SelectVisible(actor => actor.DisplayName == kind, add: true);
    }

    /// <summary>Отбор по признаку в пределах видимой части карты.</summary>
    private void SelectVisible(SelectionFilter filter) =>
        SelectVisible(filter.Matches, add: false);

    private void SelectVisible(System.Func<IOrderable, bool> match, bool add)
    {
        if (!add)
            ClearSelection();

        var view = _cursor.VisibleWorldRect;

        foreach (var actor in GM.Index.All<IOrderable>())
        {
            if (!CursorTargets.Commandable(actor) || !match(actor) || !view.HasPoint(actor.GlobalPosition))
                continue;

            if (!_selected.Contains(actor))
                _selected.Add(actor);
        }
    }

    // ── приказы ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Кому уйдёт приказ: ровно выделенным, и никому больше.
    ///
    /// НЕЯВНОГО ПОЛУЧАТЕЛЯ ЗДЕСЬ НЕТ НАМЕРЕННО. Раньше приказ при пустом выделении уходил
    /// коммандеру — приём из TA и PA, где коммандер служит юнитом по умолчанию. От него
    /// отказались: интерфейс о таком получателе умалчивает, потому что панели выделения
    /// и приказов при пустом выделении скрыты, и щелчок отзывался движением на другом конце
    /// карты без всякого объяснения. Правило теперь одно и видимое — приказ получает тот,
    /// кого видно выделенным.
    /// </summary>
    private List<IOrderable> Recipients() => _selected;

    /// <summary>
    /// Приказ по точке всему выделению.
    ///
    /// ОЧЕРЕДЬ ОДНА НА ВСЕХ, КТО ПРИКАЗ ПРИНЯЛ. Список приказов заводится здесь и общий
    /// для получателей: отряд видит работу друг друга — подошедший вторым включается
    /// в начатое, а приказ движения дожидается отставших. Видов при одном щелчке бывает
    /// несколько: вооружённые атакуют, строитель идёт строить, безоружный просто идёт, —
    /// поэтому очередей заводится ровно столько, сколько видов нашло себе получателя.
    ///
    /// ПОРЯДОК РАЗБОРА — ОТ САМОГО ОПРЕДЕЛЁННОГО К САМОМУ ОБЩЕМУ, и сопровождение стоит
    /// предпоследним, перед движением. Щелчок по подбитому своему означает «почини», а не
    /// «иди за ним», поэтому ремонт разбирается раньше; тот, кто чинить не умеет, ветку
    /// ремонта не примет и дойдёт до сопровождения сам — фильтр набора приказов устроен
    /// именно так. Сопровождение включает помощь: строитель, приставленный к строителю,
    /// берётся за то же дело — этим занят сам юнит, а не раздача приказов.
    ///
    /// ПРИКАЗ НА САМОГО СЕБЯ НЕ ВЫДАЁТСЯ НИ ОДНОГО ВИДА. Цель под курсором бывает и в самом
    /// выделении: игрок щёлкает по подбитому юниту, которого держит, по каркасу, который
    /// выделил, по своему же отряду. Ни чинить себя, ни строить себя, ни идти за собой
    /// нельзя, и приказ, который исполнять нечем, лучше не выдавать вовсе: он повиснет
    /// в очереди, а исполнитель будет считаться занятым. Отсев делает <see cref="Other"/>;
    /// у сопровождения он стоит раньше, у самого разбора цели (<see cref="Leader"/>), потому
    /// что там приказ обязан быть либо у всех получателей, либо ни у кого.
    ///
    /// Снос под правило не подпадает: он и означает «уничтожь себя», цели у него нет вовсе.
    /// </summary>
    private void IssueResolved(Vector2 point, bool queue)
    {
        var recipients = Recipients();
        if (recipients.Count == 0)
            return;

        // Цель разбираем один раз на всех: она общая, а вид приказа у каждого свой
        var (victim, site, damaged, leader) = _targets.Resolve(point, recipients);

        var attack = new Assignment(recipients, queue);
        var build = new Assignment(recipients, queue);
        var repair = new Assignment(recipients, queue);
        var follow = new Assignment(recipients, queue);
        var move = new Assignment(recipients, queue);

        foreach (var actor in recipients)
        {
            if (victim != null && CursorTargets.Other(victim, actor)
                               && attack.Give(actor, () => Order.Attack(victim)))
                continue;

            if (site != null && CursorTargets.Other(site, actor)
                             && build.Give(actor, () => Order.Work(OrderKind.Build, site)))
                continue;

            if (damaged != null && CursorTargets.Other(damaged, actor)
                               && repair.Give(actor, () => Order.Repair(damaged)))
                continue;

            if (leader != null && follow.Give(actor, () => Order.Follow(leader)))
                continue;

            move.Give(actor, () => Order.MoveTo(point));
        }
    }

    /// <summary>
    /// Приказ ЗАДАННОГО вида: игрок выбрал его клавишей, и цель под курсором даёт лишь
    /// аргумент. Обратный порядок по отношению к <see cref="IssueResolved"/>, где вид
    /// выводится из цели.
    ///
    /// ЦЕЛЬ БЕРЁТСЯ ТОЛЬКО ТА, ЧТО ВИДУ ПОДХОДИТ. Ремонт и сопровождение без цели
    /// не выдаются вовсе: приказ, которому нечего исполнять, повис бы в очереди, а
    /// исполнитель считался бы занятым. Атака же по пустому месту осмысленна и означает
    /// «идти с боем» — приказ отдельного вида (<see cref="OrderKind.AttackMove"/>).
    ///
    /// Отсев цели, совпавшей с получателем, тот же, что и при выводе из контекста.
    /// </summary>
    private void IssueForced(OrderKind kind, Vector2 point, bool queue)
    {
        var recipients = Recipients();
        if (recipients.Count == 0)
            return;

        var assignment = new Assignment(recipients, queue);

        if (kind == OrderKind.Move)
        {
            assignment.Deal(recipients, null, () => Order.MoveTo(point));
            return;
        }

        if (kind == OrderKind.Attack)
        {
            var victim = _targets.EnemyAt(point);

            if (victim != null)
                assignment.Deal(recipients, victim, () => Order.Attack(victim));
            else
                assignment.Deal(recipients, null, () => Order.AttackMove(point));

            return;
        }

        // Патруль по точке: цели не требует вовсе — точка обхода и есть весь приказ
        if (kind == OrderKind.Patrol)
        {
            assignment.Deal(recipients, null, () => Order.Patrol(point));
            return;
        }

        // Помощь строительству по точке: план или каркас под указателем. Без цели приказ
        // не выдаётся — строить на пустом месте нечего, а вид постройки задаётся панелью
        if (kind == OrderKind.Build)
        {
            if (_targets.SiteAt(point) is { } site)
                assignment.Deal(recipients, site, () => Order.Work(OrderKind.Build, site));

            return;
        }

        if (kind == OrderKind.Repair)
        {
            var damaged = _targets.DamagedAt(point);

            if (damaged != null)
                assignment.Deal(recipients, damaged, () => Order.Repair(damaged));

            return;
        }

        if (kind == OrderKind.Follow && _targets.Leader(point, recipients) is { } leader)
            assignment.Deal(recipients, leader, () => Order.Follow(leader));
    }

    // ── приказы по области и по линии ──────────────────────────────────────────

    /// <summary>
    /// Приказ по точке, каким его отдаёт нынешний жест. Путей два, и различает их
    /// происхождение вида: заданный клавишей идёт мимо разбора цели, выведенный из контекста
    /// разбирается заново по точке отпускания. Разбирать заново нужно потому, что между
    /// нажатием и отпусканием указатель успевает сойти с цели, а игрок судит по тому,
    /// где отпустил.
    /// </summary>
    private void IssuePoint(Vector2 point)
    {
        if (_gesture.Forced)
            IssueForced(_gesture.Kind, point, _gesture.Queue);
        else
            IssueResolved(point, _gesture.Queue);
    }

    /// <summary>
    /// Жест растягивания закончен: короткий означает приказ по точке, длинный — по области.
    /// </summary>
    private void FinishSweep(Vector2 point)
    {
        // СХЛОПНУТАЯ ОБЛАСТЬ УКАЗЫВАЕТ НА СВОЙ ЦЕНТР, а не на точку отпускания. Игрок целился
        // серединой круга — там стоит противник, каркас или место, которое он хочет
        // патрулировать, — а отпустил кнопку там, где кончился радиус. Взять точку отпускания
        // значило бы, что приказ уходит мимо того, во что целились, и тем дальше, чем шире
        // был неудавшийся круг
        if (!_gesture.Stretched(point))
        {
            IssuePoint(_gesture.Anchor);
            return;
        }

        float radius = _gesture.RadiusTo(point);

        switch (_gesture.Kind)
        {
            case OrderKind.Attack:
                IssueAttackArea(_gesture.Anchor, radius, _gesture.Queue);
                return;

            case OrderKind.Patrol:
                IssuePatrolArea(_gesture.Anchor, radius, _gesture.Queue);
                return;

            default:
                IssueBuildArea(_gesture.Anchor, radius, _gesture.Queue);
                return;
        }
    }

    /// <summary>
    /// Патруль по области: круг, внутри которого исполнители бегают по случайным местам.
    /// Раздаётся общей веткой, как и всякий приказ по области, — а вот случайное место
    /// и отсчёт пребывания у каждого свои, потому что приходят в круг они порознь.
    /// </summary>
    private void IssuePatrolArea(Vector2 center, float radius, bool queue)
    {
        var recipients = Recipients();

        if (recipients.Count == 0)
            return;

        var assignment = new Assignment(recipients, queue);
        assignment.Deal(recipients, null, () => Order.Patrol(center, radius));
    }

    /// <summary>
    /// Атака по области. Круг не разворачивается в набор целей и остаётся приказом:
    /// что стоит внутри в момент исполнения, то исполнитель и бьёт, — а стоять там может
    /// уже не то, что стояло в момент указания.
    ///
    /// Приказ раздаётся общей веткой, как и всякий другой: область одна на весь отряд,
    /// и делить её на личные приказы нечем.
    /// </summary>
    private void IssueAttackArea(Vector2 center, float radius, bool queue)
    {
        var recipients = Recipients();

        if (recipients.Count == 0)
            return;

        var assignment = new Assignment(recipients, queue);
        assignment.Deal(recipients, null, () => Order.Area(center, radius));
    }

    /// <summary>
    /// Помощь строительству по области. В отличие от атаки, ОБЛАСТЬ РАЗВОРАЧИВАЕТСЯ СРАЗУ:
    /// то, что строится, пересчитывается в момент указания и превращается в цепочку приказов
    /// стройки. Разница с атакой не произвольна — стройка кончается сама, и приказ на неё
    /// снимается по готовности места работы, тогда как область атаки живёт, пока в ней есть
    /// кого бить. Держать вместо цепочки живой круг означало бы, что исполнителям придётся
    /// каждый кадр перебирать все планы карты, а очередь не показывала бы игроку, что именно
    /// будет достроено.
    ///
    /// Порядок — от центра области наружу: игрок целится в то, что ему важнее, серединой
    /// жеста, а не его краем.
    /// </summary>
    private void IssueBuildArea(Vector2 center, float radius, bool queue)
    {
        var recipients = Recipients();

        if (recipients.Count == 0 || !recipients.Exists(actor => actor.Orders.Allows(OrderKind.Build)))
            return;

        var sites = _targets.SitesWithin(center, radius);

        if (sites.Count == 0)
            return;

        // Раздача на всю область одна: указанное одним жестом — это одна задача из многих
        // шагов, и ветка приказов у неё одна, как и у партии построек
        var assignment = new Assignment(recipients, queue);

        foreach (var site in sites)
        {
            assignment.Continue();

            foreach (var actor in recipients)
                assignment.Give(actor, () => Order.Work(OrderKind.Build, site));
        }
    }

    /// <summary>
    /// Записать положение указателя в линию и повести за ним одиночного исполнителя.
    ///
    /// ДО ПОРОГА ПРИКАЗА НЕ ВОЗНИКАЕТ. Пока линия коротка, жест неотличим от щелчка, а щелчок
    /// правой кнопкой обязан остаться обычным приказом по точке: выдай приказ сразу — и всякий
    /// щелчок при одном выделенном юните оборачивался бы свободным движением, то есть ходом
    /// сквозь застройку без поиска пути.
    ///
    /// ПРИКАЗ ВЫДАЁТСЯ ЗАНОВО, ЕСЛИ ОН УЖЕ ИСПОЛНЕН. Игрок ведёт указатель медленнее, чем
    /// едет юнит, поэтому тот успевает дойти до заданной точки, и приказ снимается с очереди
    /// сам. Правка точки у снятого приказа не значила бы ничего, и юнит замер бы посреди
    /// жеста, хотя линия продолжает вестись.
    /// </summary>
    private void TrackDrawing()
    {
        var visual = _cursor.VisualWorldPosition;
        _gesture.Track(visual);

        if (!_gesture.Stretched(visual))
            return;

        if (!_gesture.Led)
        {
            LeadDrawing(visual);
            return;
        }

        if (_gesture.Drawn == null || _gesture.Actor == null)
            return;

        _gesture.Drawn.Pos = visual;

        if (Queued(_gesture.Actor, _gesture.Drawn))
            return;

        // Дописыванием, а не заменой: у исполнителя мог остаться хвост очереди, набранный
        // тем же жестом под Shift, и сносить его здесь не за что
        _gesture.Drawn = GiveOne(_gesture.Actor, Order.Drawn(visual), queue: true)
                         ?? _gesture.Drawn;
    }

    /// <summary>
    /// Назначить исполнителя, которого линия ведёт прямо по ходу жеста. Так бывает только
    /// у единственного получателя: строй из одного не строится, и линия для него означает
    /// не место в строю, а непрерывно уточняемую точку назначения. Всем прочим составом
    /// места достаются по отпусканию — до тех пор линия только рисуется.
    /// </summary>
    private void LeadDrawing(Vector2 point)
    {
        _gesture.Led = true;

        var movers = Movers();

        if (movers.Count != 1)
            return;

        _gesture.Actor = movers[0];
        _gesture.Drawn = GiveOne(_gesture.Actor, Order.Drawn(point), _gesture.Queue);
    }

    /// <summary>Стоит ли приказ у исполнителя в очереди — под указателем или впереди него.</summary>
    private static bool Queued(IOrderable actor, Order order)
    {
        foreach (var item in actor.Orders.Remaining)
            if (ReferenceEquals(item, order))
                return true;

        return false;
    }

    /// <summary>
    /// Рисование закончено: исполнители распределяются по линии равными отрезками.
    ///
    /// БЛИЖНИЙ К НАЧАЛУ ЛИНИИ ЗАНИМАЕТ ПЕРВОЕ МЕСТО. Пересечения путей этим не исключаются —
    /// игрок вправе нарисовать петлю, и тогда они неизбежны, — но отряд, идущий вдоль прямой,
    /// не разворачивается задом наперёд только потому, что порядок выделения оказался иным.
    ///
    /// Короткий жест означает обычный приказ по точке: игрок щёлкнул, а не рисовал.
    /// </summary>
    private void FinishDrawing(Vector2 point)
    {
        _gesture.Track(point);

        // Одиночного вели весь жест — остаётся закрепить за ним последнюю точку
        if (_gesture.Drawn != null)
        {
            _gesture.Drawn.Pos = point;
            return;
        }

        if (!_gesture.Stretched(point))
        {
            IssuePoint(point);
            return;
        }

        var movers = Movers();

        if (movers.Count == 0)
            return;

        var start = _gesture.Path[0];

        movers.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(start)
            .CompareTo(b.GlobalPosition.DistanceSquaredTo(start)));

        var spots = _gesture.Spread(movers.Count);

        for (int i = 0; i < movers.Count; i++)
            GiveOne(movers[i], Order.Drawn(spots[i]), _gesture.Queue);
    }

    /// <summary>Выделенные, принимающие движение: только им есть что делать с линией.</summary>
    private List<IOrderable> Movers()
    {
        var movers = new List<IOrderable>();

        foreach (var actor in Recipients())
            if (actor.Orders.Allows(OrderKind.Move))
                movers.Add(actor);

        return movers;
    }

    /// <summary>
    /// Личный приказ одному исполнителю.
    ///
    /// ОБЩЕЙ ВЕТКИ ЗДЕСЬ БЫТЬ НЕ МОЖЕТ, и в этом всё отличие рисования от щелчка. Приказ,
    /// розданный одним объектом, ведёт весь отряд в одну точку — на то он и общий, — а
    /// рисование ставит каждого на своё место. Поэтому раздача идёт по одному, и состав
    /// получателей у неё из одного участника: пристёгиваться такой приказ должен к своей
    /// очереди, а не к чужой.
    /// </summary>
    private Order GiveOne(IOrderable actor, Order order, bool queue)
    {
        var single = new List<IOrderable> { actor };
        var assignment = new Assignment(single, queue);

        return assignment.Give(actor, () => order) ? order : null;
    }

    /// <summary>
    /// Снос выделенных: приказ Delete вместо прежней цепочки.
    ///
    /// Shift здесь не читается намеренно — снос всегда заменяет очередь, а не дописывается
    /// в хвост. Иначе Del после цепочки движения оставил бы снос на потом, и постройка
    /// ещё успела бы отработать шаги, которые игрок уже отменил намерением снести.
    /// </summary>
    private void IssueDelete()
    {
        var recipients = Recipients();
        if (recipients.Count == 0)
            return;

        var demolish = new Assignment(recipients, queue: false);

        foreach (var actor in recipients)
            demolish.Give(actor, Order.Delete);
    }

    /// <summary>
    /// Разметить всю размеченную партию планами. Негодные места пропускаются молча: игрок
    /// видел их красными всё протаскивание, и отказывать за всю партию из-за одного занятого
    /// места значило бы требовать безошибочного ведения мыши.
    ///
    /// СТАВИТСЯ ПЛАН, А НЕ КАРКАС. Каркас появится на месте плана, когда до него дойдёт
    /// исполнитель, — см. <see cref="BuildPlan"/>. Поэтому щелчок больше не создаёт
    /// препятствий на другом конце карты и ничего не даёт противнику под обстрел.
    ///
    /// Раскладка считается заново по точной позиции отпускания кнопки, а не по визуальному
    /// прогнозу и не по последнему кадру представления: между кадром и отпусканием курсор
    /// успевает сдвинуться, и поставить нужно то, куда игрок ткнул фактически.
    /// </summary>
    private void PlaceBatch(Vector2 point, bool queue)
    {
        var def = Pending;

        if (def == null || BlueprintScene == null)
            return;

        if (_sandbox is { } faction)
        {
            SpawnBatch(def, faction, point);
            return;
        }

        // Разметка без получателя не состоится. Строить пойдут выделенные — как и с любым
        // другим приказом, — а план, которого никто не принял, исполнять некому: подвижный
        // строитель стройку сам не берёт, и такой план остался бы в мире навсегда, занимая
        // место для правила постановки. Выделение к этому мигу бывает и вовсе другим:
        // строитель мог погибнуть, пока игрок вёл мышь, а цифра боевой группы режим
        // постройки не снимает
        if (!Recipients().Exists(actor => actor.Orders.Allows(OrderKind.Build)))
            return;

        BuildLayout.Compute(GM, def, _buildAnchor, point, Input.IsKeyPressed(Key.Alt), _plan);

        // Раздача на всю партию одна: размеченное одним протаскиванием — это одна задача,
        // и ветка приказов у неё одна. Поэтому Shift здесь решает только то, заменяет ли
        // партия прежние дела или пристёгивается к ним
        var assignment = new Assignment(Recipients(), queue);

        foreach (var spot in _plan)
        {
            if (!spot.Valid)
                continue;

            var plan = PlaceOne(def, spot);

            assignment.Continue();

            foreach (var actor in Recipients())
                assignment.Give(actor, () => Order.Work(OrderKind.Build, plan));

            _placed = true;
        }
    }

    /// <summary>
    /// Раскладка, назначенная песочницей вместо записанной в справочнике, либо null.
    ///
    /// Назначается только там, где своей раскладки нет вовсе, — то есть подвижным сущностям:
    /// строем их расставляет завод, а не игрок, и в справочнике раскладке взяться неоткуда.
    /// Постройка же свою раскладку имеет, и подменять её значило бы, что песочница
    /// показывает не то поведение, которое будет в игре.
    ///
    /// Квадрат под обычным протаскиванием и цепочка под Alt: отряд чаще нужен кучей,
    /// а ряд — реже, поэтому под пальцем стоит первое.
    /// </summary>
    private BuildPattern? SandboxPattern(bool alt)
    {
        if (_sandbox == null || Pending == null)
            return null;

        if (Pending.Pattern != BuildPattern.None || Pending.PatternAlt != BuildPattern.None)
            return null;

        return alt ? BuildPattern.Line : BuildPattern.Square;
    }

    /// <summary>
    /// Расстановка песочницей: та же размеченная партия, но каждое годное место немедленно
    /// занимает готовая сущность. Ни стоимости, ни стройки, ни приказа игрока здесь нет —
    /// панель песочницы служит проверке вида и раскладки, а не игре.
    ///
    /// СОЮЗНОМУ ЮНИТУ ЯКОРЬ СТАВИТСЯ СРАЗУ. Без него <see cref="PlayerAiSystem"/> счёл бы
    /// его свободным и назначил бы сопровождение коммандера. То же делает завод без точки
    /// сбора (см. <c>Plant.CopyRally</c>): место появления и есть пост. Противнику якорь
    /// не нужен: <see cref="EnemyAiSystem"/> выдаёт атаку как обычно, без предела дальности.
    ///
    /// СТОРОНА ЗАДАЁТСЯ ТОЛЬКО ПОДВИЖНОЙ СУЩНОСТИ. Постройка в этой игре принадлежит игроку
    /// всегда (см. <c>Building.Faction</c>), поэтому выбор стороны на неё не действует,
    /// и молчаливо ставить вражеский завод, который окажется своим, панель не позволяет —
    /// постройки собраны в союзном разделе.
    /// </summary>
    private void SpawnBatch(UnitDefinition def, Faction faction, Vector2 point)
    {
        bool alt = Input.IsKeyPressed(Key.Alt);
        BuildLayout.Compute(GM, def, _buildAnchor, point, alt, _plan, SandboxPattern(alt));

        foreach (var spot in _plan)
        {
            if (!spot.Valid)
                continue;

            if (def.IsStructure)
            {
                GM.Spawn.SpawnBuilding(def, spot.Center, spot.Facing);
            }
            else
            {
                var unit = GM.Spawn.SpawnUnit(def, spot.Center, faction);

                if (faction == Faction.Player)
                    unit.SetAnchor(spot.Center);
            }

            _placed = true;
        }
    }

    private BuildPlan PlaceOne(UnitDefinition def, BuildSpot spot)
    {
        var plan = GM.Spawn.SpawnPlan(def, spot.Center, spot.Facing, BlueprintScene);

        GM.Events.Append(new BuildPlanned
        {
            EntityId = plan.Id,
            DefinitionId = def.Id,
            Pos = spot.Center,
            Facing = spot.Facing,
        });

        return plan;
    }

    private void EnsureNodes()
    {
        // Порядок внутри слоя задан тем, кто заведён первым. Растр навигации идёт первым
        // и потому лежит ниже всей служебной графики, но ВЫШЕ мира: под постройками
        // он был бы не виден именно там, где важнее всего — на них самих. Полупрозрачность
        // делает это допустимым, а отладка без вида на растеризованное здание бесполезна
        if (_navigation == null || !IsInstanceValid(_navigation))
            _navigation = GM.Playground.Add(WorldLayer.Overlay, new NavGridOverlay());

        if (_ghost == null || !IsInstanceValid(_ghost))
        {
            _ghost = GM.Playground.Add(WorldLayer.Overlay, new PlacementGhost());

            // План принадлежит системе, а призрак получает его ссылкой: показанное
            // и поставленное обязаны быть одним и тем же списком
            _ghost.Spots = _plan;
        }

        if (_overlay == null || !IsInstanceValid(_overlay))
            _overlay = GM.Playground.Add(WorldLayer.Overlay, new OrderOverlay());

        if (_paths == null || !IsInstanceValid(_paths))
            _paths = GM.Playground.Add(WorldLayer.Overlay, new PathOverlay());

        if (_boids == null || !IsInstanceValid(_boids))
            _boids = GM.Playground.Add(WorldLayer.Overlay, new BoidsOverlay());

        if (_spatial == null || !IsInstanceValid(_spatial))
            _spatial = GM.Playground.Add(WorldLayer.Overlay, new SpatialOverlay());

        // Произвольные маркеры — поверх доменных оверлеев; доступ из кода через DebugDraw.Current.
        if (_debugDraw == null || !IsInstanceValid(_debugDraw))
            _debugDraw = GM.Playground.Add(WorldLayer.Overlay, new DebugDraw());
    }
}
