using Godot;

public enum OrderKind
{
    Move,
    Mine,
    Build,
    Attack,
    Repair,
    Follow,
}

/// <summary>
/// Приказ. Рабочие приказы держат ссылку на узел работы, движение — точку,
/// а атака, ремонт и сопровождение — сущность.
///
/// Сущность хранится нодой, а не интерфейсом: живость проверяется у ноды,
/// а нужную грань (прочность, радиус) достаём приведением там, где она понадобилась.
/// </summary>
public sealed class Order
{
    public OrderKind Kind;
    public Vector2 Pos;
    public WorkNode Target;

    /// <summary>Кого бьём, кого чиним или за кем идём — смотря какой приказ.</summary>
    public Node2D Entity;

    public static Order MoveTo(Vector2 pos) => new() { Kind = OrderKind.Move, Pos = pos };

    public static Order Work(OrderKind kind, WorkNode target) => new()
    {
        Kind = kind,
        Target = target,
        Pos = target.GlobalPosition,
    };

    public static Order Attack(Node2D victim) => new()
    {
        Kind = OrderKind.Attack,
        Entity = victim,
        Pos = victim.GlobalPosition,
    };

    public static Order Repair(Node2D target) => new()
    {
        Kind = OrderKind.Repair,
        Entity = target,
        Pos = target.GlobalPosition,
    };

    /// <summary>Сопровождение: приказ не завершается сам, пока цель жива.</summary>
    public static Order Follow(Node2D leader) => new()
    {
        Kind = OrderKind.Follow,
        Entity = leader,
        Pos = leader.GlobalPosition,
    };
}
