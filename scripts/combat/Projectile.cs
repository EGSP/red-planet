using Godot;

/// <summary>
/// Снаряд. Летит по прямой, живёт отведённое время и, задев чужую сущность,
/// публикует документ о попадании.
///
/// Урон снаряд НЕ применяет: он публикует DamageDealt, а прочность правит DamageSystem
/// в фазе реакции. Так у попадания есть след в журнале, и никто не убивает цель
/// посреди чужого обхода — от этого при удалении нод и рождаются висячие ссылки.
///
/// Попадание ищется по отрезку за кадр, а не по конечной точке: на скорости
/// пятнадцати пикселей за кадр проскочить цель насквозь несложно.
/// </summary>
public partial class Projectile : Node2D
{
    public Vector2 Velocity;

    public float Damage;

    /// <summary>Радиус самого снаряда в пикселях — складывается с радиусом цели.</summary>
    public float Radius = 4f;

    /// <summary>Сколько ещё лететь, секунд.</summary>
    public float Life = 1f;

    /// <summary>Кто выстрелил — id уходит в документ о попадании.</summary>
    public int SourceId;

    /// <summary>По какой стороне бьёт.</summary>
    public Faction TargetSide;

    public Color Tint = new(1f, 0.9f, 0.4f);

    public override void _Process(double delta) => QueueRedraw();

    public void Step(double dt)
    {
        Life -= (float)dt;
        if (Life <= 0f)
        {
            Retire();
            return;
        }

        var from = GlobalPosition;
        var to = from + Velocity * (float)dt;

        var hit = FindHit(from, to);
        GlobalPosition = to;

        if (hit == null)
            return;

        GameManager.I.Events.Append(new DamageDealt
        {
            TargetId = hit.EntityId,
            SourceId = SourceId,
            Amount = Damage,
            Pos = GlobalPosition,
        });

        Retire();
    }

    private IDamageable FindHit(Vector2 from, Vector2 to)
    {
        IDamageable best = null;
        float bestDistance = float.MaxValue;

        // Разрез уже отсеял и чужую сторону, и всё, чего в мире больше нет
        foreach (var target in GameManager.I.Targets[TargetSide])
        {
            if (target.Health.IsDead)
                continue;

            var center = target.GlobalPosition;
            float reach = target.HitRadius + Radius;

            // Ближайшая точка отрезка за кадр — так снаряд не проскакивает цель насквозь
            var closest = Geometry2D.GetClosestPointToSegment(center, from, to);
            if (closest.DistanceSquaredTo(center) > reach * reach)
                continue;

            float distance = from.DistanceSquaredTo(center);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = target;
        }

        return best;
    }

    private void Retire()
    {
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, Tint);

        // Короткий хвост назад по движению — очередь читается как очередь.
        // Ноду не поворачиваем, поэтому локальные координаты совпадают с мировым смещением
        var tail = -Velocity.Normalized() * Radius * 4f;
        DrawLine(Vector2.Zero, tail, new Color(Tint, 0.45f), Radius);
    }
}
