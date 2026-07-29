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
///
/// Приказ — НЕ документ. Документ это свершившийся факт, его нельзя отменить; приказ —
/// намерение, и он выбрасывается из очереди, как только стал невыполнимым.
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

    /// <summary>
    /// Куда приказ ведёт прямо сейчас. У движения это заданная точка, у остальных —
    /// текущее положение цели: она могла с тех пор уйти, и путь тянется за ней.
    /// </summary>
    public Vector2 Point
    {
        get
        {
            if (Alive.Is(Target))
                return Target.GlobalPosition;

            if (Alive.Is(Entity))
                return Entity.GlobalPosition;

            return Pos;
        }
    }

    /// <summary>
    /// Выполним ли приказ ещё. Проверка одна на всех исполнителей и живёт здесь, а не
    /// в сущности: невыполнимость — свойство самого приказа, а не того, кто его получил.
    /// Снимает приказы с головы очереди OrderSystem.
    /// </summary>
    public bool IsValid()
    {
        switch (Kind)
        {
            case OrderKind.Move:
                return true;

            // Цель пала — приказ исчерпан, исполнитель возвращается к обычному поведению
            case OrderKind.Attack:
                return Targeting.IsValid(Entity);

            // Починили — работа закончена сама собой
            case OrderKind.Repair:
                return Targeting.IsValid(Entity)
                       && Entity is IRepairable { Health.Ratio: < 0.999f };

            case OrderKind.Follow:
                return Alive.Is(Entity) && !Entity.IsQueuedForDeletion();
        }

        return Alive.Is(Target) && !Target.IsQueuedForDeletion() && Target.NeedsWork;
    }

    /// <summary>Название для интерфейса.</summary>
    public static string Name(OrderKind kind) => kind switch
    {
        OrderKind.Move => "идти",
        OrderKind.Mine => "копать",
        OrderKind.Build => "строить",
        OrderKind.Attack => "атаковать",
        OrderKind.Repair => "чинить",
        OrderKind.Follow => "следовать",
        _ => kind.ToString(),
    };

    /// <summary>Цвет линии приказа на карте — один и тот же в очереди и в подсказках.</summary>
    public static Color Tint(OrderKind kind) => kind switch
    {
        OrderKind.Move => new Color(0.85f, 0.9f, 1f),
        OrderKind.Mine => new Color(1f, 0.7f, 0.35f),
        OrderKind.Build => new Color(0.55f, 0.85f, 1f),
        OrderKind.Attack => new Color(1f, 0.4f, 0.35f),
        OrderKind.Repair => new Color(0.6f, 1f, 0.7f),
        OrderKind.Follow => new Color(0.7f, 0.75f, 0.85f),
        _ => Colors.White,
    };
}
