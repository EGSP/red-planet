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
/// КОМУ приказ уйдёт, решает выделение, а вид приказа отсеет набор самой сущности: копателю
/// не уйдёт атака, турели — движение. Поэтому здесь не нужно разбираться, кто выделен, —
/// достаточно предложить приказ каждому.
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

    /// <summary>Насколько промахивается мышь: припуск к радиусу цели, в пикселях.</summary>
    private const float PickSlack = Const.Unit * 0.25f;

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

    /// <summary>Вид приказа, выбранный игроком. Значим только в состоянии Aiming.</summary>
    private OrderKind _aimed;

    /// <summary>
    /// Выбранный вид приказа либо null, если режим не включён. Читает панель приказов:
    /// форма курсора — не единственный признак включённого режима, подсветка в панели
    /// говорит то же самое словами.
    /// </summary>
    public OrderKind? Aimed => State == CommandState.Aiming ? _aimed : null;

    /// <summary>Что выбрано в строительной панели. Держится до отмены выбора.</summary>
    public UnitDefinition Pending { get; private set; }

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

        if (GM.Playground != null)
            EnsureNodes();
    }

    /// <summary>
    /// Выбор постройки в панели. Зовёт <see cref="Buildbar"/>, поэтому состояние ставится
    /// безусловно: щелчок по панели до систем не доходит, и жест мышью в мире к этому мигу
    /// не начат.
    /// </summary>
    public void BeginBuild(UnitDefinition def)
    {
        Pending = def;
        _placed = false;
        State = CommandState.Placing;
        EnsureNodes();
        _ghost.Definition = def;
        _ghost.Visible = true;
    }

    /// <summary>
    /// Убрать всё, что относится к постройке. Состояния не меняет: переход — дело того
    /// места, где он решается, и делать его заодно с уборкой значило бы иметь два
    /// источника состояния.
    /// </summary>
    private void CancelBuild()
    {
        Pending = null;
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

    public override void Step(double dt)
    {
        if (GM.Playground == null || _cursor == null)
            return;

        EnsureNodes();

        // Выделенное могло погибнуть или достроиться — держим списки живыми
        _selected.RemoveAll(actor => !Alive.Is(actor as Node));
        Groups.Sweep();

        // Выделение могло погибнуть целиком: приказывать стало некому, и режим приказа
        // держаться не на чем
        if (State == CommandState.Aiming && _selected.Count == 0)
            State = CommandState.Idle;

        // Условия показа областей: Ctrl при выделении и покрытие турелей при стройке
        GizmoGate.Refresh(_selected, Pending);

        RefreshCursor(dt);

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

        BuildLayout.Compute(GM, Pending, anchor, visual, alt, _plan);
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
        if (State == CommandState.Aiming)
        {
            _cursorPoll = 0f;
            _cursor.Kind = CursorArt.Of(_aimed);
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

        if (BuildLayout.PatternOf(Pending, alt) != BuildPattern.MetalArea)
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

        if (mouse is { ButtonIndex: MouseButton.Right, Pressed: true })
            IssueResolved(point, mouse.ShiftPressed);
    }

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
            IssueForced(_aimed, point, mouse.ShiftPressed);

            if (!mouse.ShiftPressed)
                State = CommandState.Idle;

            return;
        }

        if (mouse is { ButtonIndex: MouseButton.Left, Pressed: true })
            State = CommandState.Idle;
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
    /// Какой курсор показать. В режиме приказа — выбранный вид, в обычном — тот, что уйдёт
    /// по нажатию в нынешней точке, поэтому курсор меняется сам, стоит навести его на врага,
    /// на каркас или на подбитого своего.
    ///
    /// Во время жеста мышью курсор обычный: рамка и раскладка построек показывают себя сами,
    /// а приказ в это время не отдаётся.
    /// </summary>
    private CursorKind DesiredCursor()
    {
        if (State == CommandState.Aiming)
            return CursorArt.Of(_aimed);

        if (State != CommandState.Idle)
            return CursorKind.Arrow;

        // Над элементом интерфейса приказа не будет, и обещать его курсором нельзя.
        // Спрашивается тот элемент, который принимает мышь, поэтому панели
        // с MouseFilter.Ignore курсор не гасят — сквозь них мир виден по-прежнему
        if (GetViewport()?.GuiGetHoveredControl() != null)
            return CursorKind.Arrow;

        return ResolvedKind(_cursor.ActualWorldPosition) is { } kind
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
            var one = ActorAt(point);

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
            if (Commandable(actor) && band.HasPoint(actor.GlobalPosition))
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
            if (!Commandable(actor) || !match(actor) || !view.HasPoint(actor.GlobalPosition))
                continue;

            if (!_selected.Contains(actor))
                _selected.Add(actor);
        }
    }

    /// <summary>
    /// Выделяем своё с непустым видимым набором приказов: принятые плюс мягкие
    /// (умеет, но в определении не разрешено). Мягкие нужны, чтобы сущность с забытым
    /// <c>[orders]</c> оставалась доступной для проверки в панели. Юнит, ещё выезжающий
    /// из корпуса завода, некликабелен.
    /// </summary>
    private static bool Commandable(IOrderable actor) =>
        actor.Faction == Faction.Player
        && (actor.AllowedOrders.Any || actor.SoftOrders.Any)
        && !Targeting.Leaving(actor);

    private IOrderable ActorAt(Vector2 point) =>
        GM.Index.All<IOrderable>()
            .Where(actor => Commandable(actor) && Hit(actor, point))
            .Nearest(point, actor => actor.GlobalPosition);

    private static bool Hit(IOrderable actor, Vector2 point)
    {
        float reach = ((actor as IDamageable)?.HitRadius ?? Const.Unit * 0.5f) + PickSlack;
        return actor.GlobalPosition.DistanceTo(point) <= reach;
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
        var (victim, site, damaged, leader) = ResolveTargets(point, recipients);

        var attack = new Assignment(recipients, queue);
        var build = new Assignment(recipients, queue);
        var repair = new Assignment(recipients, queue);
        var follow = new Assignment(recipients, queue);
        var move = new Assignment(recipients, queue);

        foreach (var actor in recipients)
        {
            if (victim != null && Other(victim, actor)
                               && attack.Give(actor, () => Order.Attack(victim)))
                continue;

            if (site != null && Other(site, actor)
                             && build.Give(actor, () => Order.Work(OrderKind.Build, site)))
                continue;

            if (damaged != null && Other(damaged, actor)
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
            Deal(assignment, recipients, null, () => Order.MoveTo(point));
            return;
        }

        if (kind == OrderKind.Attack)
        {
            var victim = EnemyAt(point);

            if (victim != null)
                Deal(assignment, recipients, victim, () => Order.Attack(victim));
            else
                Deal(assignment, recipients, null, () => Order.AttackMove(point));

            return;
        }

        if (kind == OrderKind.Repair)
        {
            var damaged = Repairable(GM.Obstacles.At(point) as Node2D) ?? DamagedUnitAt(point);

            if (damaged != null)
                Deal(assignment, recipients, damaged, () => Order.Repair(damaged));

            return;
        }

        if (kind == OrderKind.Follow && Leader(point, recipients) is { } leader)
            Deal(assignment, recipients, leader, () => Order.Follow(leader));
    }

    /// <summary>
    /// Раздать один и тот же приказ всем получателям, пропустив того, кто сам оказался
    /// целью. Вид приказа здесь уже выбран, поэтому перебора видов нет: не принявший
    /// его получатель просто остаётся без приказа.
    /// </summary>
    private static void Deal(Assignment assignment, List<IOrderable> recipients,
        object target, System.Func<Order> compose)
    {
        foreach (var actor in recipients)
            if (target == null || Other(target, actor))
                assignment.Give(actor, compose);
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
    /// Раздача одного вида приказа: заводит общую очередь на первом получателе и подписывает
    /// на неё остальных.
    ///
    /// ЧТО ДЕЛАЕТ ДОПИСЫВАНИЕ ПО SHIFT. Получатели заняты разным: у одного своя очередь,
    /// у второго своя, третий свободен. Приказ заводится ОДИН и в одной ветке, а хвосты
    /// разных очередей к ней пристёгиваются: каждый доделывает своё и переходит в общую.
    /// Так одно намерение хранится один раз, сколько бы очередей в него ни сошлось, —
    /// а значит и вставка в него потом будет одна (см. <see cref="BuildPlan"/>).
    ///
    /// Приказ создаётся отложенно: видов разбирается четыре, а находит получателя не всякий,
    /// и заводить ветку под несостоявшийся вид незачем.
    /// </summary>
    private sealed class Assignment
    {
        private readonly List<IOrderable> _recipients;
        private readonly bool _queue;

        /// <summary>Хвосты, уже приведённые в общую ветку. Пристёгивать второй раз нечего.</summary>
        private readonly HashSet<OrderList> _linked = new();

        private OrderList _branch;
        private Order _order;

        public Assignment(List<IOrderable> recipients, bool queue)
        {
            _recipients = recipients;
            _queue = queue;
        }

        public bool Give(IOrderable actor, System.Func<Order> compose)
        {
            _order ??= compose();

            if (!actor.Orders.Allows(_order.Kind))
                return false;

            bool taken = _queue ? Enqueue(actor) : Adopt(actor);

            if (taken && actor is Unit unit)
                unit.SetAnchor(_order.Point);

            return taken;
        }

        /// <summary>
        /// Приказ вместо прежних: получатель подписывается на общую ветку, бросая свою.
        /// Ветка заводится на первом получателе и достаётся всем остальным той же самой.
        /// </summary>
        private bool Adopt(IOrderable actor)
        {
            actor.Orders.Adopt(Branch());
            return true;
        }

        /// <summary>
        /// Приказ в дополнение к прежним: хвост очереди получателя ПРИСТЁГИВАЕТСЯ к общей
        /// ветке. Получатель доделывает своё и переходит в неё, а само намерение хранится
        /// один раз — сколько бы разных очередей ни сошлось в эту ветку.
        ///
        /// Свободному пристёгивать нечего, и он подписывается на ветку напрямую.
        ///
        /// Если конец цепочки общий с теми, кого игрок не выделял, получатель сперва
        /// забирает свой остаток себе (<see cref="OrderQueue.Fork"/>): приказ, отданный
        /// части отряда, делает из неё другой отряд, и навязывать его остальным нельзя.
        /// </summary>
        private bool Enqueue(IOrderable actor)
        {
            if (actor.Orders.List == null)
                return Adopt(actor);

            if (!Within(actor.Orders.List.Tail))
                actor.Orders.Fork();

            var tail = actor.Orders.List.Tail;

            // Хвост уже ведёт в эту ветку — второй раз его пристёгивать нечем и незачем
            if (tail == Branch() || !_linked.Add(tail))
                return true;

            tail.LinkNext(Branch());
            return true;
        }

        /// <summary>
        /// Следующий приказ той же раздачи. Ложится в ту же ветку, что и предыдущий:
        /// партия планов, размеченная одним протаскиванием, — это одна задача из многих
        /// шагов, а не сотня отдельных веток, сцепленных в цепочку.
        /// </summary>
        public void Continue() => _order = null;

        /// <summary>Общая ветка раздачи. Приказ ложится в неё при первом же получателе.</summary>
        private OrderList Branch()
        {
            _branch ??= OrderList.Open();

            if (_branch.IndexOf(_order) < 0)
                _branch.Add(_order);

            return _branch;
        }

        /// <summary>Все ли, кто способен дойти до ветки, — из числа получателей приказа.</summary>
        private bool Within(OrderList list) => list.Within(_recipients);
    }

    /// <summary>
    /// Всё, что лежит в точке и может стать целью приказа. Разбор один на два потребителя:
    /// выдачу приказа и курсор, который показывает, что произойдёт по нажатию. Разойтись
    /// им нельзя — курсор, обещающий не то, что случится, хуже отсутствия курсора.
    /// </summary>
    private (Node2D Victim, IWorkSite Site, Node2D Damaged, Node2D Leader) ResolveTargets(
        Vector2 point, List<IOrderable> recipients)
    {
        var occupant = GM.Obstacles.At(point) as Node;

        return (
            EnemyAt(point),
            occupant is Blueprint { NeedsWork: true } frame ? frame : PlanAt(point),
            Repairable(occupant as Node2D) ?? DamagedUnitAt(point),
            Leader(point, recipients));
    }

    /// <summary>
    /// Какой приказ уйдёт по нажатию в этой точке, если вид его не задан игроком.
    ///
    /// ПОРЯДОК ТОТ ЖЕ, ЧТО У ВЫДАЧИ, и повторён он здесь не по недосмотру: выдача
    /// перебирает получателей, у каждого спрашивая набор, а курсору нужен один ответ
    /// на весь отряд. Общим у них остаётся разбор цели (<see cref="ResolveTargets"/>),
    /// то есть та часть, расхождение в которой было бы враньём; выбор же вида различается
    /// ровно тем, что здесь довольно одного согласного получателя.
    /// </summary>
    private OrderKind? ResolvedKind(Vector2 point)
    {
        var recipients = Recipients();

        if (recipients.Count == 0)
            return null;

        var (victim, site, damaged, leader) = ResolveTargets(point, recipients);

        if (victim != null && AnyTakes(recipients, OrderKind.Attack, victim))
            return OrderKind.Attack;

        if (site != null && AnyTakes(recipients, OrderKind.Build, site))
            return OrderKind.Build;

        if (damaged != null && AnyTakes(recipients, OrderKind.Repair, damaged))
            return OrderKind.Repair;

        if (leader != null && AnyTakes(recipients, OrderKind.Follow, leader))
            return OrderKind.Follow;

        return AnyTakes(recipients, OrderKind.Move, null) ? OrderKind.Move : null;
    }

    /// <summary>Возьмётся ли за такой приказ хоть кто-то из получателей.</summary>
    private static bool AnyTakes(List<IOrderable> recipients, OrderKind kind, object target)
    {
        foreach (var actor in recipients)
            if (actor.Orders.Allows(kind) && (target == null || Other(target, actor)))
                return true;

        return false;
    }

    /// <summary>
    /// Годится ли цель этому получателю: она не он сам.
    ///
    /// Одна проверка на все виды приказов, хотя сейчас сработать она может не у каждого:
    /// врагом получатель не бывает никогда, а вот местом работы и повреждённой целью бывает —
    /// каркас выделяется и принимает приказы наравне с постройкой, а подбитый ремонтник
    /// стоит в том же отряде, которому игрок отдаёт приказ. Проверка стоит у всех потому,
    /// что правило одно, а не три, и держать его в стольких местах, сколько видов приказов,
    /// значило бы забыть о нём при появлении следующего.
    /// </summary>
    private static bool Other(object target, IOrderable actor) => !ReferenceEquals(target, actor);

    /// <summary>
    /// За кем идти: своя сущность под курсором, не входящая в само выделение.
    ///
    /// ВЫДЕЛЕННЫЙ ВЕДУЩИМ НЕ БЫВАЕТ. Щелчок по своему же отряду — обычное указание идти
    /// туда, где он стоит, и превращать его в сопровождение нельзя: отряд принялся бы ходить
    /// сам за собой, а половина его при этом получила бы приказ, которого игрок не отдавал.
    /// Поэтому проверка стоит здесь, у разбора цели, а не у раздачи: приказ сопровождения
    /// либо есть у всех получателей, либо его нет вовсе.
    /// </summary>
    private Node2D Leader(Vector2 point, List<IOrderable> recipients) =>
        ActorAt(point) is { } found && !recipients.Contains(found) ? found as Node2D : null;

    /// <summary>
    /// План под курсором. Спрашивается отдельно от карты препятствий, потому что план
    /// в ней не значится: место он держит только для правила постановки.
    /// </summary>
    private BuildPlan PlanAt(Vector2 point)
    {
        foreach (var plan in GM.Index.All<BuildPlan>())
            if (plan.NeedsWork && plan.Footprint.HasPoint(point))
                return plan;

        return null;
    }

    // ПОДХОД ОТДЕЛЬНЫМ ПРИКАЗОМ БОЛЬШЕ НЕ СТАВИТСЯ.
    //
    // Прежде рабочий приказ раздавался цепочкой «дойти, потом работать», и точка подхода
    // считалась для каждого исполнителя своя. Списку, общему на весь отряд, такой приказ
    // принадлежать не может: в нём место одно, а точек подхода столько же, сколько юнитов.
    //
    // Потери в этом нет. Подход механике никогда и не был нужен — исполнитель доходит
    // до места работы сам (Unit.RunWork), — а нужен он был игроку, чтобы путь читался
    // в очереди отдельным шагом. Теперь очередь содержит ровно то, что игрок приказал,
    // а путь по-прежнему виден: линия приказа тянется от юнита к месту работы.

    /// <summary>
    /// Враг в указанной точке. Корпус небольшой, поэтому даём припуск —
    /// попадать точно в кружок мышью неудобно, а промах уводит юнита гулять.
    /// </summary>
    private Node2D EnemyAt(Vector2 point) =>
        GM.Units[Faction.Hostile]
            .Where(enemy => !Targeting.Leaving(enemy)
                            && enemy.GlobalPosition.DistanceTo(point)
                            <= enemy.HitRadius + PickSlack)
            .Nearest(point, enemy => enemy.GlobalPosition);

    private Node2D DamagedUnitAt(Vector2 point) =>
        GM.Units[Faction.Player]
            .Where(unit => !Targeting.Leaving(unit)
                           && unit.GlobalPosition.DistanceTo(point) <= unit.HitRadius + PickSlack)
            .Nearest(point, unit => unit.GlobalPosition) is { } found && Repairable(found) != null
            ? found
            : null;

    /// <summary>Годится ли под ремонт: своё, повреждённое и с курсом ремонта.</summary>
    private static Node2D Repairable(Node2D node) =>
        node is IRepairable { Health: { Ratio: < 0.999f } } repairable
        && repairable.HealthPerMetal > 0f
        && node is IDamageable { Faction: Faction.Player }
            ? node
            : null;

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
            _navigation = GM.Playground.Add(WorldLayer.Effects, new NavGridOverlay());

        if (_ghost == null || !IsInstanceValid(_ghost))
        {
            _ghost = GM.Playground.Add(WorldLayer.Effects, new PlacementGhost());

            // План принадлежит системе, а призрак получает его ссылкой: показанное
            // и поставленное обязаны быть одним и тем же списком
            _ghost.Spots = _plan;
        }

        if (_overlay == null || !IsInstanceValid(_overlay))
            _overlay = GM.Playground.Add(WorldLayer.Effects, new OrderOverlay());

        if (_paths == null || !IsInstanceValid(_paths))
            _paths = GM.Playground.Add(WorldLayer.Effects, new PathOverlay());

        if (_boids == null || !IsInstanceValid(_boids))
            _boids = GM.Playground.Add(WorldLayer.Effects, new BoidsOverlay());

        // Произвольные маркеры — поверх доменных оверлеев; доступ из кода через DebugDraw.Current.
        if (_debugDraw == null || !IsInstanceValid(_debugDraw))
            _debugDraw = GM.Playground.Add(WorldLayer.Effects, new DebugDraw());
    }
}
