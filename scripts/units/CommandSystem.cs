using System.Collections.Generic;
using Godot;

/// <summary>
/// Приказы игрока, выделение и режим строительства.
/// Клиент отдаёт намерения, применяет их симуляция — привычка на будущее.
///
/// ЧТО ПРИКАЗАТЬ, РЕШАЕТ ЦЕЛЬ ПОД КУРСОРОМ, а не заранее выбранный режим: щёлкнули по врагу —
/// атака, по каркасу — стройка, по повреждённому — ремонт, по месторождению — копка, по земле —
/// движение. Одна кнопка на всё, как в PA.
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

    private PlacementGhost _ghost;
    private OrderOverlay _overlay;

    /// <summary>Курсор в мировых координатах — берём из событий, а не опросом.</summary>
    private Vector2 _cursor;

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

    /// <summary>Рамка выделения, пока её тянут.</summary>
    public bool Banding => _banding;

    public Rect2 Band => new Rect2(_bandStart, _cursor - _bandStart).Abs();

    /// <summary>
    /// Вторая фаза: площадка мира к этому мигу собрана, поэтому служебную графику
    /// заводим здесь, один раз, а не проверяем её наличие в каждом кадре.
    /// Проверка в EnsureNodes при этом остаётся: ноды могут быть освобождены позже.
    /// </summary>
    protected override void OnLink()
    {
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

        if (_ghost != null)
            _ghost.Visible = false;
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
        if (GM.Playground == null)
            return;

        EnsureNodes();

        // Выделенное могло погибнуть или достроиться — держим списки живыми
        _selected.RemoveAll(actor => !Alive.Is(actor as Node));
        Groups.Sweep();

        if (Pending == null)
            return;

        // Призрак показывает не выбор, а готовность поставить: пока режим спит,
        // место под курсором не подсвечивается, и щелчок обещает обычное выделение
        _ghost.Visible = Building;

        if (!_ghost.Visible)
            return;

        var origin = OriginUnderCursor(Pending);
        _ghost.Origin = origin;
        _ghost.Valid = GM.Grid.IsFree(origin, Pending);
        _ghost.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GM?.Playground == null)
            return;

        if (@event is InputEventMouse mouseEvent)
            _cursor = GetViewport().GetCanvasTransform().AffineInverse() * mouseEvent.Position;

        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && ControlGroups.SlotOf(key.Keycode) is var slot and >= 0)
        {
            if (key.CtrlPressed)
                AssignGroup(slot);
            else
                SelectGroup(slot);

            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton mouse)
            return;

        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left when mouse.Pressed && Building:
                PlaceBlueprint();
                break;

            // Рамку начинаем сразу: одиночный клик — это её вырожденный случай,
            // и различаются они только тем, сколько мышь успела проехать.
            //
            // Уснувший выбор панели снимается этим же щелчком: игрок вернулся
            // к управлению отрядом, и держать за ним постройку больше незачем
            case MouseButton.Left when mouse.Pressed:
                CancelBuild();
                _bandStart = _cursor;
                _banding = true;
                break;

            case MouseButton.Left:
                FinishBand();
                break;

            case MouseButton.Right when mouse.Pressed && Building:
                CancelBuild();
                break;

            case MouseButton.Right when mouse.Pressed:
                CancelBuild();
                IssueOrder();
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

    private void FinishBand()
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

        if (_bandStart.DistanceTo(_cursor) < BandThreshold)
        {
            var one = ActorUnderCursor();

            // Повтор по той же цели означает «и всех таких же». Прямое указание игрока,
            // поэтому ни родство, ни преобладание здесь не применяются
            if (_doubleTap.Register(one))
                SelectSameKind(one);
            else if (one != null && !_selected.Contains(one))
                _selected.Add(one);

            return;
        }

        var band = Band;
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
    /// </summary>
    private static bool Commandable(IOrderable actor) =>
        actor.Faction == Faction.Player && actor.AllowedOrders.Any;

    private IOrderable ActorUnderCursor()
    {
        var cursor = _cursor;

        return GM.Index.All<IOrderable>()
            .Where(actor => Commandable(actor) && Hit(actor, cursor))
            .Nearest(cursor, actor => actor.GlobalPosition);
    }

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

    private void IssueOrder()
    {
        var recipients = Recipients();
        if (recipients.Count == 0)
            return;

        bool queue = Input.IsKeyPressed(Key.Shift);

        // Цель разбираем один раз на всех: она общая, а вид приказа у каждого свой
        var victim = EnemyUnderCursor();
        var occupant = GM.Entities.Get(GM.Grid.OwnerOf(Const.WorldToCell(_cursor)));
        var damagedUnit = DamagedUnitUnderCursor();

        foreach (var actor in recipients)
            Send(actor, victim, occupant, damagedUnit, queue);
    }

    /// <summary>
    /// Один приказ по цели под курсором. Порядок разбора — от самого определённого
    /// к самому общему: враг, каркас, руда, повреждённое, и только потом голая земля.
    /// </summary>
    private void Send(IOrderable actor, Node2D victim, Node occupant, Node2D damagedUnit,
        bool queue)
    {
        if (victim != null && Give(actor, queue, Order.Attack(victim)))
            return;

        if (occupant is Blueprint { NeedsWork: true } blueprint
            && GiveWork(actor, queue, Order.Work(OrderKind.Build, blueprint), blueprint))
            return;

        if (occupant is OreDeposit { NeedsWork: true } ore
            && GiveWork(actor, queue, Order.Work(OrderKind.Mine, ore), ore))
            return;

        // Ремонт: сначала постройка на клетке, потом юнит под курсором
        var damaged = Repairable(occupant as Node2D) ?? damagedUnit;

        if (damaged != null && GiveWork(actor, queue, Order.Repair(damaged), damaged))
            return;

        Give(actor, queue, Order.MoveTo(_cursor));
    }

    /// <summary>
    /// Рабочий приказ с подходом: далеко — сначала дойти, потом работать. Цепочка нужна
    /// не механике (юнит дошёл бы и сам), а игроку — чтобы путь и работа были видны
    /// в очереди двумя отдельными шагами.
    /// </summary>
    private static bool GiveWork(IOrderable actor, bool queue, Order order, Node2D target)
    {
        if (actor is not Unit { Definition: not null } unit)
            return Give(actor, queue, order);

        // Считаем от конца очереди, если дописываем: подход должен вести оттуда,
        // где юнит окажется, а не оттуда, где он стоит сейчас
        var from = queue && unit.Orders.Count > 0
            ? unit.Orders.Items[^1].Point
            : unit.GlobalPosition;

        float range = unit.Definition.WorkRangePx;
        var to = target.GlobalPosition;

        if (from.DistanceTo(to) <= range)
            return Give(actor, queue, order);

        var direction = (from - to).Normalized();
        if (direction == Vector2.Zero)
            direction = Vector2.Right;

        return Give(actor, queue, Order.MoveTo(to + direction * range * 0.8f), order);
    }

    private static bool Give(IOrderable actor, bool queue, params Order[] orders) =>
        queue ? actor.Orders.TryEnqueue(orders) : actor.Orders.TrySet(orders);

    /// <summary>
    /// Враг, по которому щёлкнули. Корпус небольшой, поэтому даём припуск —
    /// попадать точно в кружок мышью неудобно, а промах уводит юнита гулять.
    /// </summary>
    private Node2D EnemyUnderCursor()
    {
        var cursor = _cursor;

        return GM.Index.All<Enemy>()
            .Where(enemy => enemy.GlobalPosition.DistanceTo(cursor)
                            <= enemy.HitRadius + PickSlack)
            .Nearest(cursor, enemy => enemy.GlobalPosition);
    }

    private Node2D DamagedUnitUnderCursor()
    {
        var cursor = _cursor;

        return GM.Index.All<Unit>()
            .Where(unit => unit.GlobalPosition.DistanceTo(cursor) <= unit.HitRadius + PickSlack)
            .Nearest(cursor, unit => unit.GlobalPosition) is { } found && Repairable(found) != null
            ? found
            : null;
    }

    /// <summary>Годится ли под ремонт: своё, повреждённое и с курсом ремонта.</summary>
    private static Node2D Repairable(Node2D node) =>
        node is IRepairable { Health: { Ratio: < 0.999f } } repairable
        && repairable.HealthPerMetal > 0f
        && node is IDamageable { Faction: Faction.Player }
            ? node
            : null;

    private Vector2I OriginUnderCursor(UnitDefinition def)
    {
        var cell = Const.WorldToCell(_cursor);
        return cell - new Vector2I(def.Width / 2, def.Height / 2);
    }

    private void PlaceBlueprint()
    {
        var def = Pending;
        var origin = OriginUnderCursor(def);

        if (!GM.Grid.IsFree(origin, def) || BlueprintScene == null)
            return;

        var blueprint = GM.Spawn.SpawnBlueprint(BlueprintScene, def, origin);

        GM.Events.Append(new BlueprintPlaced
        {
            EntityId = blueprint.Id,
            DefinitionId = def.Id,
            Cell = origin,
        });

        // Строить пойдут выделенные — как и с любым другим приказом. Без выделения
        // каркас просто встанет на место и будет ждать, пока за него возьмётся
        // свободный бот: те ищут работу сами
        bool queue = Input.IsKeyPressed(Key.Shift);

        foreach (var actor in Recipients())
            GiveWork(actor, queue, Order.Work(OrderKind.Build, blueprint), blueprint);

        // Выбор в панели остаётся: дальше он оживает под Shift и гаснет без него
        _placed = true;
    }

    private void EnsureNodes()
    {
        // Порядок призрака и оверлея внутри слоя эффектов задан тем, кто заведён первым:
        // призрак постройки должен лежать под отрисовкой приказов
        if (_ghost == null || !IsInstanceValid(_ghost))
            _ghost = GM.Playground.Add(WorldLayer.Effects, new PlacementGhost());

        if (_overlay == null || !IsInstanceValid(_overlay))
            _overlay = GM.Playground.Add(WorldLayer.Effects, new OrderOverlay());
    }
}
