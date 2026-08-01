using Godot;

/// <summary>
/// Юнит с очередью приказов. Движется к цели, а войдя в радиус инструмента —
/// подключается к узлу работы. Сам ресурсы не двигает: только сообщает свою мощность.
///
/// Он же цель для врага и он же носитель ствола, если ствол задан справочником.
/// Стрелять юнит начинает только без приказов: работа важнее, а огонь по площадям
/// вместо стройки — не то, чего ждёшь от отданного приказа.
///
/// Приказы юнит только ИСПОЛНЯЕТ. Кто их раздаёт — игрок через CommandSystem или мозговая
/// система — его не касается, а чего ему отдать нельзя, отсекает набор AllowedOrders.
/// </summary>
public partial class Unit : Node2D, IFacing, IDamageable, IArmed, IEconomyActor, IVision,
    IRepairable, IOrderable, IMobile
{
    /// <summary>
    /// Определение. Ставит Spawner при создании: узел юнита собственной сцены не имеет,
    /// и связать его со справочником больше некому.
    /// </summary>
    public UnitDefinition Definition { get; set; }

    public int Id { get; set; }

    public OrderQueue Orders { get; }

    /// <summary>
    /// Намерение двигаться. Сам юнит себя не перемещает: он объявляет, куда хочет попасть,
    /// а путь, обход соседей и выталкивание из зданий делает <see cref="MovementSystem"/>.
    /// </summary>
    public Movement Movement { get; } = new();

    private WorkNode _attached;

    public Unit() => Orders = new OrderQueue(this);

    public bool Idle => Orders.Idle;

    public Order Current => Orders.Current;

    public Health Health { get; protected set; }

    public WeaponState Gun { get; } = new();

    public int EntityId => Id;

    public string DefinitionId => Definition?.Id ?? "";

    public string DisplayName => Definition?.DisplayName ?? "юнит";

    public virtual Faction Faction => Faction.Player;

    /// <summary>
    /// Что юниту можно приказать. Выводится из того, чем он снабжён, а не задаётся списком:
    /// нет руки — нет приказа строить, нет ствола — нет атаки. Поэтому набор не может
    /// разойтись с тем, что юнит на самом деле умеет.
    /// </summary>
    public virtual OrderSet AllowedOrders => Definition == null
        ? OrderSet.None
        : OrderSet.None
            .With(OrderKind.Move, Definition.IsMobile)
            .With(OrderKind.Follow, Definition.IsMobile)
            .With(OrderKind.Attack, Definition.Weapon != null)
            .With(OrderKind.Build, Definition.CanBuild)
            .With(OrderKind.Repair, Definition.CanRepair);

    public SelectionGroup SelectionGroup => Definition?.SelectionGroup ?? SelectionGroup.Bots;

    /// <summary>Ось «вперёд» подвижной сущности — это поворот самой ноды.</summary>
    public float Facing => Rotation;

    public float HitRadius => Definition?.RadiusPx ?? Const.Unit * 0.35f;

    public float VisionRadius => Definition?.VisionRadiusPx ?? 0f;

    /// <summary>
    /// Курс ремонта. Раньше его приходилось складывать из двух определений — прочность
    /// брать из одного, цену из другого, — потому что юнит описывался двумя файлами.
    /// Теперь оба числа лежат рядом, и курс считает само определение.
    ///
    /// У коммандера секции сборки нет — значит нет и цены, а без цены нет курса,
    /// и чинить его нечем. Он и не нуждается: урон он копит, но не гибнет.
    /// </summary>
    public float HealthPerMetal => Definition?.HealthPerMetal ?? 0f;

    public WeaponDefinition Weapon => Definition?.Weapon;

    /// <summary>
    /// Огонь без приказов — по ближайшему, по приказу «атака» — по назначенному.
    /// На работе не стреляем: занятый юнит делом занят.
    /// </summary>
    public virtual bool CanFire => Idle || Current?.Kind == OrderKind.Attack;

    /// <summary>
    /// По приказу бьём именно назначенную жертву, даже если рядом мельтешит другая:
    /// приказ игрока не должен перебиваться выбором «кто ближе».
    /// </summary>
    public IDamageable FireTarget =>
        Current?.Kind == OrderKind.Attack ? Current.Entity as IDamageable : null;

    public override void _Ready()
    {
        Health ??= new Health(Definition?.MaxHealth ?? 80f);

        QueueRedraw();
    }

    public void Declare(EconomyLedger ledger)
    {
        if (Definition == null)
            return;

        ledger.AddIncome(ResourceKind.Energy, Definition.EnergyProduction);
        ledger.AddIncome(ResourceKind.Metal, Definition.MetalProduction);

        // Ремонт — это и есть стройка тем же инструментом, поэтому и заявка та же:
        // метал по мощности руки плюс энергия на неё же
        if (RepairTarget != null && Definition.BuildTool is { } arm)
            Repair.Declare(ledger, arm.Power, arm.EnergyPerPower);
    }

    /// <summary>Своё производство идёт всегда, просадка производительности его не касается.</summary>
    public void Run(double dt, EconomyRates rates)
    {
        if (Definition == null)
            return;

        var events = GameManager.I.Events;

        if (Definition.EnergyProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Energy,
                Amount = Definition.EnergyProduction * (float)dt,
            });

        if (Definition.MetalProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Metal,
                Amount = Definition.MetalProduction * (float)dt,
            });

        var target = RepairTarget;
        if (target != null && Definition.BuildTool is { } arm)
            Repair.Run(target, arm.Power, arm.EnergyPerPower, dt, rates);
    }

    public void AimAt(Vector2 point, double dt)
    {
        if (GlobalPosition.IsEqualApprox(point))
            return;

        float desired = Heading.AngleTo(GlobalPosition, point);
        float step = (Definition?.TurnSpeed ?? Mathf.Pi) * (float)dt;
        Rotation = Heading.TurnToward(Rotation, desired, step);
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>Приказов нет — отпускаем узел работы и стоим.</summary>
    public void OnIdle(double dt) => Detach();

    /// <summary>
    /// Отработать приказ за кадр. Снимать невыполнимое с головы очереди не наше дело —
    /// это уже сделала OrderSystem, сюда приказ приходит заведомо годным.
    /// </summary>
    public void RunOrder(Order order, double dt)
    {
        if (Definition == null)
            return;

        switch (order.Kind)
        {
            case OrderKind.Attack:
                RunAttack(order, dt);
                return;

            case OrderKind.Repair:
                RunRepair(order, dt);
                return;

            case OrderKind.Follow:
                RunFollow(order, dt);
                return;

            default:
                RunWork(order, dt);
                return;
        }
    }

    /// <summary>
    /// Движение и стройка: дойти, а дойдя — подключиться к узлу работы.
    ///
    /// Приказ объявляет намерение и проверяет, дошли ли. Сам ход и доворот корпуса
    /// на ходу делает система движения: обходя препятствие, юнит едет не туда, куда
    /// его послали, и разворот к цели выглядел бы движением боком вперёд.
    /// </summary>
    private void RunWork(Order order, double dt)
    {
        var target = order.Kind == OrderKind.Move ? order.Pos : order.Target.GlobalPosition;

        // Дальность принадлежит инструменту, а не юниту: раньше одно число служило всем
        // занятиям сразу, хотя тянутся они на разное
        var tool = Definition.BuildTool;

        float reach = order.Kind == OrderKind.Move
            ? Const.Unit * 0.2f
            : tool?.RangePx ?? Const.Unit;

        // Приказ «идти» считается исполненным и тогда, когда ближе не пройти из-за своих:
        // всему отряду в одну точку не поместиться, и ждать этого бессмысленно. К работе
        // это не относится — там нужна не точка, а дальность инструмента до цели
        bool settled = order.Kind == OrderKind.Move && Movement.Settled;

        if (!settled && GlobalPosition.DistanceTo(target) > reach)
        {
            Detach();
            Movement.Seek(target, reach);
            return;
        }

        // Дошли: разворачиваемся к тому, с чем работаем
        AimAt(target, dt);

        if (order.Kind == OrderKind.Move)
        {
            Orders.DropCurrent();
            return;
        }

        if (tool == null)
        {
            Orders.DropCurrent();
            return;
        }

        if (_attached != order.Target)
        {
            Detach();

            // Узлу сообщаем и мощность, и прожорливость: энергию тратит инструмент,
            // а сколько именно — записано в нём же
            order.Target.AttachWorker(Id, tool.Power, tool.EnergyDrain);
            _attached = order.Target;
        }
    }

    /// <summary>
    /// Приказ атаковать: подойти на дальность своего ствола и держаться. Сам выстрел —
    /// дело WeaponSystem, сюда попадает только подход.
    ///
    /// В пределах дальности корпус крутит система стрельбы, на подходе — система движения.
    /// Своего доворота здесь нет: два доворота за кадр удвоили бы скорость вращения.
    /// </summary>
    private void RunAttack(Order order, double dt)
    {
        Detach();

        var victim = order.Entity;
        var target = victim as IDamageable;
        var to = victim.GlobalPosition;

        // Подходим до дистанции, заведомо лежащей ВНУТРИ огневой границы, а не до самой
        // границы. Остановка по признаку «уже достаю» оставляла юнита ровно на краю,
        // откуда любое смещение цели или толчок соседа выводили его из радиуса.
        // Безоружный подходит на длину инструмента: приказ хотя бы не зависает
        float stop = Weapon != null
            ? Targeting.ApproachDistance(Weapon, target, Definition.StandoffFraction)
            : Definition.WorkRangePx;

        if (GlobalPosition.DistanceTo(to) > stop)
            Movement.Seek(to, stop);
    }

    /// <summary>
    /// Ремонт: подойти на длину инструмента и стоять. Само восстановление прочности идёт
    /// в Run — ремонт стоит ресурсов и потому проходит через экономику, как и стройка.
    /// </summary>
    private void RunRepair(Order order, double dt)
    {
        Detach();

        var to = order.Entity.GlobalPosition;
        float stop = Definition.BuildTool?.RangePx ?? Definition.WorkRangePx;

        if (GlobalPosition.DistanceTo(to) > stop)
            Movement.Seek(to, stop);
        else
            AimAt(to, dt);
    }

    /// <summary>
    /// Сопровождение: держаться рядом, но не наступать на пятки. Приказ не завершается сам —
    /// его вытесняет работа, как только она появится.
    /// </summary>
    private void RunFollow(Order order, double dt)
    {
        Detach();

        var to = order.Entity.GlobalPosition;

        if (GlobalPosition.DistanceTo(to) > Const.FollowDistancePx)
            Movement.Seek(to, Const.FollowDistancePx);
    }

    /// <summary>Кого чиним прямо сейчас: приказ ремонта, цель в пределах инструмента.</summary>
    private IRepairable RepairTarget
    {
        get
        {
            var order = Current;

            if (order?.Kind != OrderKind.Repair || Definition == null || !Definition.CanRepair)
                return null;

            if (order.Entity is not IRepairable repairable || !Targeting.IsValid(order.Entity))
                return null;

            if (order.Entity is Unit && !Definition.CanRepairUnits)
                return null;

            float reach = Definition.BuildTool.RangePx;
            return GlobalPosition.DistanceTo(order.Entity.GlobalPosition) <= reach + 1f
                ? repairable
                : null;
        }
    }

    private void Detach()
    {
        if (Alive.Is(_attached))
            _attached.DetachWorker(Id);

        _attached = null;
    }

    /// <summary>Узел работы уходит из игры — забываем его, не касаясь мёртвой ноды.</summary>
    public void OnTargetLost(WorkNode node)
    {
        if (_attached == node)
            _attached = null;

        Orders.DropAllFor(node);
    }

    public override void _ExitTree() => Detach();

    /// <summary>Прочность кончилась: отпустить узел работы. EntityStore снимает Spawner.</summary>
    public virtual void OnDestroyed()
    {
        Detach();
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

        VisionGizmo.Draw(this, Definition.VisionRadiusPx, Definition.Color);
        WeaponGizmo.Draw(this, Weapon, Definition.Color);

        DrawCircle(Vector2.Zero, radius, Definition.Color);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 24, new Color(0f, 0f, 0f, 0.4f), 2f);

        // Ось «вперёд»: нода уже повёрнута, поэтому в локальных координатах это просто вправо
        DrawLine(Vector2.Zero, new Vector2(radius * 1.5f, 0f), new Color(1f, 1f, 1f, 0.8f), 2.5f);

        HealthBar.Draw(this, Health, radius * 2.4f, -radius - 10f, Rotation);

        // Луч к узлу работы — это «работа идёт», а не приказ: очередь рисует оверлей
        if (Alive.Is(_attached))
            DrawLine(Vector2.Zero, ToLocal(_attached.GlobalPosition),
                new Color(1f, 1f, 0.5f, 0.6f), 2f);
    }
}
