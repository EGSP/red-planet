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

    /// <summary>Насколько промахивается мышь: припуск к радиусу цели, в пикселях.</summary>
    private const float PickSlack = Const.Unit * 0.25f;

    /// <summary>Дальше этого протаскивания клик считается рамкой, а не выбором одного.</summary>
    private const float BandThreshold = 8f;

    private PlacementGhost _ghost;
    private OrderOverlay _overlay;

    /// <summary>Курсор в мировых координатах — берём из событий, а не опросом.</summary>
    private Vector2 _cursor;

    private readonly List<IOrderable> _selected = new();

    private Vector2 _bandStart;
    private bool _banding;

    public BuildableDef Pending { get; private set; }

    /// <summary>Кто сейчас выделен — читают оверлей и HUD.</summary>
    public IReadOnlyList<IOrderable> Selected => _selected;

    /// <summary>Рамка выделения, пока её тянут.</summary>
    public bool Banding => _banding;

    public Rect2 Band => new Rect2(_bandStart, _cursor - _bandStart).Abs();

    protected override void OnRegister() => GM.Command = this;

    public void BeginBuild(BuildableDef def)
    {
        Pending = def;
        EnsureNodes();
        _ghost.Def = def;
        _ghost.Visible = true;
    }

    public void CancelBuild()
    {
        Pending = null;
        if (_ghost != null)
            _ghost.Visible = false;
    }

    public override void Step(double dt)
    {
        if (GM.WorldRoot == null)
            return;

        EnsureNodes();

        // Выделенное могло погибнуть или достроиться — держим список живым
        _selected.RemoveAll(actor => !Alive.Is(actor as Node));

        if (Pending == null)
            return;

        var origin = OriginUnderCursor(Pending);
        _ghost.Origin = origin;
        _ghost.Valid = GM.Grid.IsFree(origin, Pending);
        _ghost.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GM.WorldRoot == null)
            return;

        if (@event is InputEventMouse mouseEvent)
            _cursor = GetViewport().GetCanvasTransform().AffineInverse() * mouseEvent.Position;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            CancelBuild();
            _selected.Clear();
            return;
        }

        if (@event is not InputEventMouseButton mouse)
            return;

        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left when mouse.Pressed && Pending != null:
                PlaceBlueprint();
                break;

            // Рамку начинаем сразу: одиночный клик — это её вырожденный случай,
            // и различаются они только тем, сколько мышь успела проехать
            case MouseButton.Left when mouse.Pressed:
                _bandStart = _cursor;
                _banding = true;
                break;

            case MouseButton.Left:
                FinishBand();
                break;

            case MouseButton.Right when mouse.Pressed && Pending != null:
                CancelBuild();
                break;

            case MouseButton.Right when mouse.Pressed:
                IssueOrder();
                break;
        }
    }

    // ── выделение ──────────────────────────────────────────────────────────────

    private void FinishBand()
    {
        if (!_banding)
            return;

        _banding = false;

        bool add = Input.IsKeyPressed(Key.Shift);
        if (!add)
            _selected.Clear();

        if (_bandStart.DistanceTo(_cursor) < BandThreshold)
        {
            var one = ActorUnderCursor();
            if (one != null && !_selected.Contains(one))
                _selected.Add(one);

            return;
        }

        var band = Band;

        foreach (var actor in GM.Index.All<IOrderable>())
        {
            if (!Commandable(actor) || !band.HasPoint(actor.GlobalPosition))
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
    /// Кому уйдёт приказ: выделенным, а если никого не выделено — коммандеру.
    /// Так привычное «щёлкнул и коммандер пошёл» работает без выделения, как раньше.
    /// </summary>
    private List<IOrderable> Recipients()
    {
        if (_selected.Count > 0)
            return _selected;

        var commander = GM.Commander;
        return commander != null ? new List<IOrderable> { commander } : new List<IOrderable>();
    }

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
        if (actor is not Unit { Def: not null } unit)
            return Give(actor, queue, order);

        // Считаем от конца очереди, если дописываем: подход должен вести оттуда,
        // где юнит окажется, а не оттуда, где он стоит сейчас
        var from = queue && unit.Orders.Count > 0
            ? unit.Orders.Items[^1].Point
            : unit.GlobalPosition;

        float range = unit.Def.ToolRange * Const.Unit;
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

    private Vector2I OriginUnderCursor(BuildableDef def)
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
            DefId = def.Id,
            Cell = origin,
        });

        // Строить пойдёт выделенный, а без выделения — коммандер, как и любой другой приказ
        bool queue = Input.IsKeyPressed(Key.Shift);

        foreach (var actor in Recipients())
            GiveWork(actor, queue, Order.Work(OrderKind.Build, blueprint), blueprint);

        if (!Input.IsKeyPressed(Key.Shift))
            CancelBuild();
    }

    private void EnsureNodes()
    {
        if (_ghost == null || !IsInstanceValid(_ghost))
        {
            _ghost = new PlacementGhost { ZIndex = 10 };
            GM.WorldRoot.AddChild(_ghost);
        }

        if (_overlay == null || !IsInstanceValid(_overlay))
        {
            _overlay = new OrderOverlay { ZIndex = 9 };
            GM.WorldRoot.AddChild(_overlay);
        }
    }
}
