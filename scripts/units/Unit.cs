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
public partial class Unit : Node2D, IFacing, IDamageable, IArmed
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

    public virtual Faction Faction => Faction.Player;

    /// <summary>Ось «вперёд» подвижной сущности — это поворот самой ноды.</summary>
    public float Facing => Rotation;

    public float HitRadius => Def?.RadiusPx ?? Const.Unit * 0.35f;

    public WeaponDef Weapon => Def?.Weapon;

    /// <summary>Огонь только без приказов: занятый юнит делом занят, а не стрельбой.</summary>
    public virtual bool CanFire => Idle;

    /// <summary>Своей цели у юнита нет — ближайшую находит система стрельбы.</summary>
    public IDamageable FireTarget => null;

    public override void _Ready()
    {
        Health ??= new Health(Def?.MaxHealth ?? 80f);

        AddToGroup("unit");
        AddToGroup(Targeting.Group);
        AddToGroup("armed");
        QueueRedraw();
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
            order.Target.AttachWorker(Id, power);
            _attached = order.Target;
        }
    }

    private bool IsValid(Order order)
    {
        if (order.Kind == OrderKind.Move)
            return true;

        return Alive.Is(order.Target)
               && !order.Target.IsQueuedForDeletion()
               && order.Target.NeedsWork;
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
        foreach (var group in new[] { "unit", "bot", "commander", Targeting.Group, "armed" })
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

        if (Weapon != null)
            DrawArc(Vector2.Zero, Weapon.RangePx, 0f, Mathf.Tau, 64,
                new Color(Def.Color, 0.16f), 1.5f);

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
        else if (Alive.Is(order.Target))
            DrawLine(Vector2.Zero, ToLocal(order.Target.GlobalPosition),
                new Color(1f, 1f, 1f, 0.2f), 1f);
    }
}
