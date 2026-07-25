using Godot;

public enum OrderKind
{
    Move,
    Mine,
    Build,
}

/// <summary>Приказ. Рабочие приказы держат ссылку на узел работы, движение — точку.</summary>
public sealed class Order
{
    public OrderKind Kind;
    public Vector2 Pos;
    public WorkNode Target;

    public static Order MoveTo(Vector2 pos) => new() { Kind = OrderKind.Move, Pos = pos };

    public static Order Work(OrderKind kind, WorkNode target) => new()
    {
        Kind = kind,
        Target = target,
        Pos = target.GlobalPosition,
    };
}
