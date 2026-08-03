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
    private bool _banding;

    /// <summary>Что выбрано в строительной панели. Держится до отмены выбора.</summary>
    public UnitDefinition Pending { get; private set; }

    /// <summary>Поставлен ли хоть один каркас с нынешнего выбора.</summary>
    private bool _placed;

    /// <summary>
    /// Точка, где нажали кнопку, пока её держат. Она задаёт и место первой постройки,
    /// и начало вектора, по которому считаются угол и раскладка.
    /// </summary>
    private Vector2 _buildAnchor;

    private bool _dragging;

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
    /// </summary>
    public bool Building => Pending != null && (!_placed || Input.IsKeyPressed(Key.Shift));

    /// <summary>Кто сейчас выделен — читают оверлей и HUD.</summary>
    public IReadOnlyList<IOrderable> Selected => _selected;

    /// <summary>
    /// Показать очереди всех своих, а не только выделенных — как CapsLock в PA.
    /// Переключается клавишей C.
    /// </summary>
    public bool ShowAllOrders { get; private set; }

    /// <summary>Рамка выделения, пока её тянут.</summary>
    public bool Banding => _banding;

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

    public void BeginBuild(UnitDefinition def)
    {
        Pending = def;
        _placed = false;
        EnsureNodes();
        _ghost.Definition = def;
        _ghost.Visible = true;
    }

    public void CancelBuild()
    {
        Pending = null;
        _placed = false;
        _dragging = false;
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
        if (Pending != null)
        {
            CancelBuild();
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

        // Условия показа областей: Ctrl при выделении и покрытие турелей при стройке
        GizmoGate.Refresh(_selected, Pending);

        if (Pending == null)
            return;

        // Призрак показывает не выбор, а готовность поставить: пока режим спит,
        // место под курсором не подсвечивается, и щелчок обещает обычное выделение
        _ghost.Visible = Building || _dragging;

        if (!_ghost.Visible)
        {
            _ghost.StretchRadius = 0f;
            return;
        }

        // Представление читает визуальную позицию: при прогнозе призрак упреждает задержку,
        // при движении камеры без мыши точку уже пересчитал CursorSystem
        var visual = _cursor.VisualWorldPosition;
        var anchor = _dragging ? _buildAnchor : visual;
        bool alt = Input.IsKeyPressed(Key.Alt);

        BuildLayout.Compute(GM, Pending, anchor, visual, alt, _plan);
        UpdateStretchGhost(anchor, visual, alt);

        _ghost.QueueRedraw();
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GM?.Playground == null || _cursor == null)
            return;

        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.C)
            {
                ShowAllOrders = !ShowAllOrders;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (ControlGroups.SlotOf(key.Keycode) is var slot and >= 0)
            {
                if (key.CtrlPressed)
                    AssignGroup(slot);
                else
                    SelectGroup(slot);

                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is not InputEventMouseButton mouse)
            return;

        // Кнопки берут точную позицию события и никогда не прогнозируемую:
        // приказ обязан уйти туда, куда ткнули
        var point = _cursor.WorldFromEvent(mouse);

        switch (mouse.ButtonIndex)
        {
            // Постройка ставится не по нажатию, а по отпусканию: между ними игрок задаёт
            // угол и раскладку, и до отпускания решение не принято
            case MouseButton.Left when mouse.Pressed && Building:
                _buildAnchor = point;
                _dragging = true;
                break;

            case MouseButton.Left when _dragging:
                PlaceBatch(point);
                break;

            // Рамку начинаем сразу: одиночный клик — это её вырожденный случай,
            // и различаются они только тем, сколько мышь успела проехать.
            //
            // Уснувший выбор панели снимается этим же щелчком: игрок вернулся
            // к управлению отрядом, и держать за ним постройку больше незачем
            case MouseButton.Left when mouse.Pressed:
                CancelBuild();
                _bandStart = point;
                _banding = true;
                break;

            case MouseButton.Left:
                FinishBand(point);
                break;

            // Правая кнопка посреди протаскивания отменяет только его: выбор в панели
            // остаётся, и следующее нажатие начинает раскладку заново
            case MouseButton.Right when mouse.Pressed && _dragging:
                _dragging = false;
                break;

            case MouseButton.Right when mouse.Pressed && Building:
                CancelBuild();
                break;

            case MouseButton.Right when mouse.Pressed:
                CancelBuild();
                IssueOrder(point);
                break;
        }
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

    private void FinishBand(Vector2 point)
    {
        if (!_banding)
            return;

        _banding = false;

        bool add = Input.IsKeyPressed(Key.Shift);

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
    /// Все такие же на карте. Область не ограничена видимой частью намеренно: «выделить
    /// всех копателей» — приказ о составе отряда, а не о том, что сейчас попало в кадр,
    /// и результат не должен меняться от положения камеры.
    ///
    /// Тип опознаём по DisplayName — по той же строке, которой подписана панель выделения.
    /// </summary>
    private void SelectSameKind(IOrderable sample)
    {
        string kind = sample.DisplayName;

        if (!_selected.Contains(sample))
            _selected.Add(sample);

        foreach (var actor in GM.Index.All<IOrderable>())
        {
            if (!Commandable(actor) || actor.DisplayName != kind)
                continue;

            if (!_selected.Contains(actor))
                _selected.Add(actor);
        }
    }

    /// <summary>
    /// Выделяем только то, чем можно управлять: свой и с непустым набором приказов.
    /// Месторождение и склад в рамку не попадут — приказать им всё равно нечего.
    /// Юнит, ещё выезжающий из корпуса завода, некликабелен.
    /// </summary>
    private static bool Commandable(IOrderable actor) =>
        actor.Faction == Faction.Player
        && actor.AllowedOrders.Any
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
    /// </summary>
    private void IssueOrder(Vector2 point)
    {
        var recipients = Recipients();
        if (recipients.Count == 0)
            return;

        bool queue = Input.IsKeyPressed(Key.Shift);

        // Цель разбираем один раз на всех: она общая, а вид приказа у каждого свой
        var victim = EnemyAt(point);
        var occupant = GM.Obstacles.At(point) as Node;
        IWorkSite site = occupant is Blueprint { NeedsWork: true } frame
            ? frame
            : PlanAt(point);
        var damaged = Repairable(occupant as Node2D) ?? DamagedUnitAt(point);
        var leader = Leader(point, recipients);

        var attack = new Assignment(recipients, queue);
        var build = new Assignment(recipients, queue);
        var repair = new Assignment(recipients, queue);
        var follow = new Assignment(recipients, queue);
        var move = new Assignment(recipients, queue);

        foreach (var actor in recipients)
        {
            if (victim != null && attack.Give(actor, () => Order.Attack(victim)))
                continue;

            if (site != null && build.Give(actor, () => Order.Work(OrderKind.Build, site)))
                continue;

            if (damaged != null && repair.Give(actor, () => Order.Repair(damaged)))
                continue;

            if (leader != null && follow.Give(actor, () => Order.Follow(leader)))
                continue;

            move.Give(actor, () => Order.MoveTo(point));
        }
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
    private void PlaceBatch(Vector2 point)
    {
        _dragging = false;

        var def = Pending;

        if (def == null || BlueprintScene == null)
            return;

        BuildLayout.Compute(GM, def, _buildAnchor, point, Input.IsKeyPressed(Key.Alt), _plan);

        // Строить пойдут выделенные — как и с любым другим приказом. Без выделения
        // планы просто останутся размеченными и будут ждать, пока за них возьмутся:
        // подвижный сам за стройку не берётся, а башня-сборщик берётся в своём радиусе
        //
        // Раздача на всю партию одна: размеченное одним протаскиванием — это одна задача,
        // и ветка приказов у неё одна. Поэтому Shift здесь решает только то, заменяет ли
        // партия прежние дела или пристёгивается к ним
        var assignment = new Assignment(Recipients(), Input.IsKeyPressed(Key.Shift));

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
