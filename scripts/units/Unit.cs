using System.Collections.Generic;
using Godot;

/// <summary>
/// Юнит с очередью приказов. Движется к цели, а войдя в радиус инструмента —
/// подключается к узлу работы. Сам ресурсы не двигает: только сообщает свою мощность.
/// </summary>
public partial class Unit : Node2D
{
    [Export] public UnitDef Def;

    public int Id { get; set; }

    protected readonly List<Order> Orders = new();

    private WorkNode _attached;

    public bool Idle => Orders.Count == 0;

    public Order Current => Orders.Count > 0 ? Orders[0] : null;

    public override void _Ready()
    {
        AddToGroup("unit");
        QueueRedraw();
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
            GlobalPosition = GlobalPosition.MoveToward(target, Def.Speed * Const.Unit * (float)dt);
            return;
        }

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

    public override void _Draw()
    {
        if (Def == null)
            return;

        float radius = Const.Unit * 0.35f;
        DrawCircle(Vector2.Zero, radius, Def.Color);
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 24, new Color(0f, 0f, 0f, 0.4f), 2f);

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
