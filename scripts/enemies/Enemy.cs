using Godot;

/// <summary>
/// Враг. Один скрипт на все виды: толстяк и стрелок различаются только справочником —
/// прочностью, скоростью хода и вращения, размером и стволом.
///
/// Что делает сам: идёт к назначенной цели, доворачивает корпус и держит дистанцию,
/// с которой достаёт оружие. Чего не делает: не выбирает цель (это EnemySystem) и не
/// стреляет (это WeaponSystem). Разделение то же, что у ботов игрока: поиск работы —
/// дело системы, ход — дело сущности.
/// </summary>
public partial class Enemy : Node2D, IFacing, IDamageable, IArmed, IVision, IOrderable, IMobile
{
    public UnitDefinition Definition { get; private set; }

    public int Id { get; set; }

    public Health Health { get; private set; }

    public WeaponState Gun { get; } = new();

    public OrderQueue Orders { get; }

    /// <summary>Намерение двигаться. Ход, обход и выталкивание делает MovementSystem.</summary>
    public Movement Movement { get; } = new();

    public Enemy() => Orders = new OrderQueue(this);

    private float _retarget;

    public int EntityId => Id;

    public string DisplayName => Definition?.DisplayName ?? "враг";

    /// <summary>
    /// Враг ходит и стреляет — этим его набор и исчерпывается. Ни копать, ни строить он
    /// не умеет, и приказ такого рода до него не дойдёт, кто бы его ни отдал.
    /// </summary>
    public OrderSet AllowedOrders =>
        OrderSet.None.With(OrderKind.Move).With(OrderKind.Attack);

    /// <summary>Род при выделении берётся из определения, как у всех: он выведен из тегов.</summary>
    public SelectionGroup SelectionGroup => Definition?.SelectionGroup ?? SelectionGroup.Bots;

    public string DefinitionId => Definition?.Id ?? "";

    public Faction Faction => Faction.Hostile;

    /// <summary>Ось «вперёд» подвижной сущности — это поворот самой ноды.</summary>
    public float Facing => Rotation;

    public float HitRadius => Definition?.RadiusPx ?? Const.Unit * 0.4f;

    public float VisionRadius => Definition?.VisionRadiusPx ?? 0f;

    public WeaponDefinition Weapon => Definition?.Weapon;

    public bool CanFire => true;

    /// <summary>Кого враг бьёт — это и есть его текущий приказ атаки, отдельного поля нет.</summary>
    public IDamageable Target =>
        Orders.Current?.Kind == OrderKind.Attack ? Orders.Current.Entity as IDamageable : null;

    /// <summary>Враг бьёт того, к кому шёл, а не первого попавшегося в радиус.</summary>
    public IDamageable FireTarget => Target;

    /// <summary>
    /// Пора ли искать цель заново. Цель может и не погибнуть — просто рядом выросло что-то
    /// поближе, поэтому выбор переигрывается по таймеру, а не только по смерти прежней цели.
    /// </summary>
    public bool NeedsTarget => Orders.Idle || _retarget <= 0f;

    /// <summary>Отсчёт до следующей переигровки ведёт мозг — он же и решает.</summary>
    public void TickRetarget(double dt) => _retarget -= (float)dt;

    public void NoteTargeted() => _retarget = Const.EnemyRetargetDelay;

    public void Init(int id, UnitDefinition def, Vector2 position)
    {
        Id = id;
        Definition = def;
        Position = position;
        Health = new Health(def.MaxHealth);

        // Появился — уже смотрит на базу: иначе первый ход выглядит как разворот на месте
        Rotation = Heading.AngleTo(position, Vector2.Zero);
    }

    public override void _Ready() => Health ??= new Health(Definition?.MaxHealth ?? 100f);

    public override void _Process(double delta) => QueueRedraw();

    public void AimAt(Vector2 point, double dt)
    {
        float desired = Heading.AngleTo(GlobalPosition, point);
        float step = (Definition?.TurnSpeed ?? Mathf.Pi) * (float)dt;
        Rotation = Heading.TurnToward(Rotation, desired, step);
    }

    /// <summary>Приказов нет — враг стоит: чем ему заняться, решает мозг, а не он сам.</summary>
    public void OnIdle(double dt) { }

    public void RunOrder(Order order, double dt)
    {
        if (Definition == null)
            return;

        if (order.Kind == OrderKind.Move)
        {
            float reach = Const.Unit * 0.2f;

            // Ближе не пройти из-за своих — приказ исполнен: всей толпе в одну точку
            // не поместиться, и ждать этого бессмысленно
            if (Movement.Settled || GlobalPosition.DistanceTo(order.Pos) <= reach)
            {
                Orders.DropCurrent();
                return;
            }

            Movement.Seek(order.Pos, reach);
            return;
        }

        if (order.Entity is not IDamageable target)
            return;

        var to = target.GlobalPosition;
        float stop = StopDistance(target);

        // Доворот на подходе делает система движения, в пределах дальности — система
        // стрельбы. Два доворота за кадр удвоили бы скорость вращения и обесценили
        // разницу видов
        if (GlobalPosition.DistanceTo(to) > stop)
            Movement.Seek(to, stop);
    }

    /// <summary>
    /// С какого расстояния враг перестаёт подходить: доля дальности ствола плюс радиус цели.
    /// Долю берём меньше единицы — иначе цель выпадает из радиуса от любого шага в сторону.
    /// </summary>
    private float StopDistance(IDamageable target)
    {
        float reach = Weapon != null
            ? Weapon.RangePx * Definition.StandoffFraction
            : Const.Unit;

        return reach + target.HitRadius;
    }

    /// <summary>Прочность кончилась: вывести из игры. EntityStore снимает Spawner.</summary>
    public void OnDestroyed()
    {
        Orders.Clear();

        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем.
        // Реестр по id чистит подписка Spawner на выбытие из индекса.
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Definition == null)
            return;

        float radius = Definition.RadiusPx;

        VisionGizmo.Draw(this, VisionRadius, Definition.Color);
        WeaponGizmo.Draw(this, Weapon, Definition.Color);

        DrawCircle(Vector2.Zero, radius, Definition.Color);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 24, new Color(0f, 0f, 0f, 0.45f), 2f);

        // Ось «вперёд»: нода уже повёрнута, поэтому в локальных координатах это просто вправо
        DrawLine(Vector2.Zero, new Vector2(radius * 1.5f, 0f), new Color(1f, 1f, 1f, 0.85f), 2.5f);

        HealthBar.Draw(this, Health, radius * 2.4f, -radius - 10f, Rotation);
    }
}
