using Godot;

public enum OrderKind
{
    Move,
    Mine,
    Build,
    Attack,
}

/// <summary>
/// Приказ. Рабочие приказы держат ссылку на узел работы, движение — точку,
/// атака — жертву.
///
/// Жертва хранится нодой, а не через IDamageable: живость проверяется у ноды,
/// а интерфейс достаётся приведением там, где нужен радиус и прочность.
/// </summary>
public sealed class Order
{
    public OrderKind Kind;
    public Vector2 Pos;
    public WorkNode Target;
    public Node2D Victim;

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
        Victim = victim,
        Pos = victim.GlobalPosition,
    };
}
