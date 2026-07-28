using System.Collections.Generic;
using Godot;

/// <summary>
/// Юнит с очередью приказов. Движется к цели, а войдя в радиус инструмента —
/// подключается к узлу работы. Сам ресурсы не двигает: только сообщает свою мощность.
///
/// Он же цель для врага и он же носитель ствола, если ствол задан справочником.
/// Стрелять юнит начинает только без приказов: работа важнее, а огонь по площадям
/// вместо стройки — не то, чего ждёшь от отданного приказа.
/// </summary>
public partial class Unit : Node2D, IFacing, IDamageable, IArmed, IEconomyActor, IVision, IRepairable
{
    [Export] public UnitDef Def;

    public int Id { get; set; }

    protected readonly List<Order> Orders = new();

    private WorkNode _attached;

    public bool Idle => Orders.Count == 0;

    public Order Current => Orders.Count > 0 ? Orders[0] : null;

    public Health Health { get; protected set; }

    public WeaponState Gun { get; } = new();

    public int EntityId => Id;

    /// <summary>Юнита нет в справочнике построек, ёмкости хранилища он не даёт.</summary>
    public string DefId => "";

    public virtual Faction Faction => Faction.Player;

    /// <summary>Ось «вперёд» подвижной сущности — это поворот самой ноды.</summary>
    public float Facing => Rotation;

    public float HitRadius => Def?.RadiusPx ?? Const.Unit * 0.35f;

    public float VisionRadius => Def?.VisionRadiusPx ?? 0f;

    /// <summary>
    /// Курс ремонта юнита берётся из справочника постройки с тем же ключом — того самого,
    /// по которому его и собирают. Дублировать цену в UnitDef незачем: она уже есть,
    /// и два числа рано или поздно разъехались бы.
    ///
    /// У коммандера такого справочника нет — значит и курса нет, и чинить его нечем.
    /// Он и не нуждается: урон он копит, но не гибнет.
    /// </summary>
    public float HealthPerMetal
    {
        get
        {
            if (Def == null)
                return 0f;

            var buildable = GameManager.I.Catalog.Buildable(Def.Id);

            // Прочность берём свою, а цену — из справочника сборки: чинить юнита с нуля
            // должно стоить ровно столько же, сколько собрать его заново
            return buildable is { CostMetal: > 0f } ? Def.MaxHealth / buildable.CostMetal : 0f;
        }
    }

    public WeaponDef Weapon => Def?.Weapon;

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
        Health ??= new Health(Def?.MaxHealth ?? 80f);

        AddToGroup("unit");
        AddToGroup(Targeting.Group);
        AddToGroup("armed");

        // В экономике участвуют все юниты: коммандер — своим доходом (без него первую
        // электростанцию было бы не на что строить), остальные — ремонтом
        AddToGroup(EconomySystem.Group);

        QueueRedraw();
    }

    public void Declare(EconomyLedger ledger)
    {
        if (Def == null)
            return;

        ledger.AddIncome(ResourceKind.Energy, Def.EnergyProduction);
        ledger.AddIncome(ResourceKind.Metal, Def.MetalProduction);

        // Ремонт — это и есть стройка тем же инструментом, поэтому и заявка та же:
        // метал по строительной мощности плюс энергия на инструмент
        if (RepairTarget != null)
        {
            ledger.Request(ResourceKind.Metal, Def.BuildPower);
            ledger.Request(ResourceKind.Energy, Def.BuildPower * Def.BuildEnergyPerPower);
        }
    }

    /// <summary>Своё производство идёт всегда, просадка производительности его не касается.</summary>
    public void Run(double dt, EconomyRates rates)
    {
        if (Def == null)
            return;

        var events = GameManager.I.Events;

        if (Def.EnergyProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Energy,
                Amount = Def.EnergyProduction * (float)dt,
            });

        if (Def.MetalProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Metal,
                Amount = Def.MetalProduction * (float)dt,
            });

        RunRepair(dt, rates);
    }

    private void RunRepair(double dt, EconomyRates rates)
    {
        var target = RepairTarget;
        if (target != null)
            Repair.Run(target, Def.BuildPower, Def.BuildEnergyPerPower, dt, rates);
    }

    public void AimAt(Vector2 point, double dt)
    {
        if (GlobalPosition.IsEqualApprox(point))
            return;

        float desired = Heading.AngleTo(GlobalPosition, point);
        float step = (Def?.TurnSpeed ?? Mathf.Pi) * (float)dt;
        Rotation = Heading.TurnToward(Rotation, desired, step);
    }

    public override void _Process(double delta) => QueueRedraw();

    public void SetOrders(params Order[] orders)
    {
        ClearOrders();
        Orders.AddRange(orders);
    }

    public void Enqueue(Order order) => Orders.Add(order);

    public void ClearOrders()
    {
        Detach();
        Orders.Clear();
    }

    public void Step(double dt)
    {
        while (Orders.Count > 0 && !IsValid(Orders[0]))
        {
            Detach();
            Orders.RemoveAt(0);
        }

        if (Orders.Count == 0)
        {
            Detach();
            return;
        }

        var order = Orders[0];

        switch (order.Kind)
        {
            case OrderKind.Attack:
                StepAttack(order, dt);
                return;

            case OrderKind.Repair:
                StepRepair(order, dt);
                return;

            case OrderKind.Follow:
                StepFollow(order, dt);
                return;
        }

        Vector2 target = order.Kind == OrderKind.Move ? order.Pos : order.Target.GlobalPosition;

        float reach = order.Kind == OrderKind.Move
            ? Const.Unit * 0.2f
            : Def.ToolRange * Const.Unit;

        if (GlobalPosition.DistanceTo(target) > reach)
        {
            Detach();
            AimAt(target, dt);
            GlobalPosition = GlobalPosition.MoveToward(target, Def.SpeedPx * (float)dt);
            return;
        }

        // Дошли: разворачиваемся к тому, с чем работаем
        AimAt(target, dt);

        if (order.Kind == OrderKind.Move)
        {
            Orders.RemoveAt(0);
            return;
        }

        float power = order.Kind == OrderKind.Mine ? Def.MinePower : Def.BuildPower;
        if (power <= 0f)
        {
            Orders.RemoveAt(0);
            return;
        }

        if (_attached != order.Target)
        {
            Detach();

            // Узлу сообщаем и мощность, и собственную прожорливость инструмента:
            // энергию тратит инструмент, а сколько именно — дело справочника юнита
            order.Target.AttachWorker(Id, power, Def.EnergyDrainFor(order.Kind));
            _attached = order.Target;
        }
    }

    /// <summary>
    /// Приказ атаковать: подойти на дальность своего ствола и держаться. Сам выстрел —
    /// дело WeaponSystem, сюда попадает только подход.
    ///
    /// Доворачиваемся, только пока идём: в пределах дальности корпус крутит система стрельбы,
    /// и второй доворот за тот же кадр удвоил бы скорость вращения.
    /// </summary>
    private void StepAttack(Order order, double dt)
    {
        Detach();

        var victim = order.Entity;
        var target = victim as IDamageable;
        var to = victim.GlobalPosition;

        if (Weapon != null && Targeting.InFiringRange(Weapon, GlobalPosition, target))
            return;

        AimAt(to, dt);

        // Безоружный юнит подходит на длину инструмента: приказ хотя бы не зависает
        float stop = Weapon != null
            ? Weapon.RangePx * 0.85f + (target?.HitRadius ?? 0f)
            : Def.ToolRange * Const.Unit;

        if (GlobalPosition.DistanceTo(to) > stop)
            GlobalPosition = GlobalPosition.MoveToward(to, Def.SpeedPx * (float)dt);
    }

    /// <summary>
    /// Ремонт: подойти на длину инструмента и стоять. Само восстановление прочности идёт
    /// в Run — ремонт стоит ресурсов и потому проходит через экономику, как и стройка.
    /// </summary>
    private void StepRepair(Order order, double dt)
    {
        Detach();

        var to = order.Entity.GlobalPosition;
        AimAt(to, dt);

        float stop = Def.ToolRange * Const.Unit;
        if (GlobalPosition.DistanceTo(to) > stop)
            GlobalPosition = GlobalPosition.MoveToward(to, Def.SpeedPx * (float)dt);
    }

    /// <summary>
    /// Сопровождение: держаться рядом, но не наступать на пятки. Приказ не завершается сам —
    /// его вытесняет работа, как только она появится.
    /// </summary>
    private void StepFollow(Order order, double dt)
    {
        Detach();

        var to = order.Entity.GlobalPosition;
        float distance = GlobalPosition.DistanceTo(to);

        if (distance <= Const.FollowDistancePx)
            return;

        AimAt(to, dt);
        GlobalPosition = GlobalPosition.MoveToward(to, Def.SpeedPx * (float)dt);
    }

    private bool IsValid(Order order)
    {
        switch (order.Kind)
        {
            case OrderKind.Move:
                return true;

            // Цель пала — приказ исчерпан, и юнит возвращается к обычному поведению
            case OrderKind.Attack:
                return Targeting.IsValid(order.Entity);

            // Починили — работа закончена сама собой
            case OrderKind.Repair:
                return Targeting.IsValid(order.Entity)
                       && order.Entity is IRepairable { Health.Ratio: < 0.999f };

            case OrderKind.Follow:
                return Alive.Is(order.Entity) && !order.Entity.IsQueuedForDeletion();
        }

        return Alive.Is(order.Target)
               && !order.Target.IsQueuedForDeletion()
               && order.Target.NeedsWork;
    }

    /// <summary>Кого чиним прямо сейчас: приказ ремонта, цель в пределах инструмента.</summary>
    private IRepairable RepairTarget
    {
        get
        {
            var order = Current;

            if (order?.Kind != OrderKind.Repair || Def == null || !Def.CanRepair)
                return null;

            if (order.Entity is not IRepairable repairable || !Targeting.IsValid(order.Entity))
                return null;

            if (order.Entity is Unit && !Def.CanRepairUnits)
                return null;

            float reach = Def.ToolRange * Const.Unit;
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

        Orders.RemoveAll(order => order.Target == node);
    }

    public override void _ExitTree() => Detach();

    /// <summary>Прочность кончилась: отпустить узел работы, выйти из реестра и групп.</summary>
    public virtual void OnDestroyed()
    {
        ClearOrders();
        GameManager.I.Entities.Remove(Id);

        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем
        foreach (var group in new[]
                     { "unit", "bot", "commander", Targeting.Group, "armed", EconomySystem.Group })
            if (IsInGroup(group))
                RemoveFromGroup(group);

        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Def == null)
            return;

        float radius = Def.RadiusPx;

        VisionGizmo.Draw(this, Def.VisionRadiusPx, Def.Color);
        WeaponGizmo.Draw(this, Weapon, Def.Color);

        DrawCircle(Vector2.Zero, radius, Def.Color);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 24, new Color(0f, 0f, 0f, 0.4f), 2f);

        // Ось «вперёд»: нода уже повёрнута, поэтому в локальных координатах это просто вправо
        DrawLine(Vector2.Zero, new Vector2(radius * 1.5f, 0f), new Color(1f, 1f, 1f, 0.8f), 2.5f);

        HealthBar.Draw(this, Health, radius * 2.4f, -radius - 10f, Rotation);

        if (Alive.Is(_attached))
        {
            DrawLine(Vector2.Zero, ToLocal(_attached.GlobalPosition),
                new Color(1f, 1f, 0.5f, 0.6f), 2f);
            return;
        }

        var order = Current;
        if (order == null)
            return;

        if (order.Kind == OrderKind.Move)
            DrawLine(Vector2.Zero, ToLocal(order.Pos), new Color(1f, 1f, 1f, 0.2f), 1f);
        else if (order.Kind == OrderKind.Attack && Alive.Is(order.Entity))
            DrawLine(Vector2.Zero, ToLocal(order.Entity.GlobalPosition),
                new Color(1f, 0.4f, 0.35f, 0.5f), 1.5f);
        else if (Alive.Is(order.Target))
            DrawLine(Vector2.Zero, ToLocal(order.Target.GlobalPosition),
                new Color(1f, 1f, 1f, 0.2f), 1f);
    }
}
