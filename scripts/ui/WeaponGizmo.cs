using Godot;

/// <summary>
/// Примитив ствола: круг дальности и рёбра конуса прицеливания.
///
/// Когда круг показывать, решает <see cref="GizmoGate"/> / <see cref="UnitGizmos"/>.
/// Сам примитив одинаков у врагов, коммандера и турелей: три копии в трёх _Draw
/// развели бы поведение.
///
/// Координаты локальные: у подвижных сущностей и турели ось «вперёд» — это Rotation ноды,
/// поэтому в своей системе координат ствол всегда смотрит вправо. Для сущности, чья ось
/// живёт отдельным числом (статичное здание), угол передаётся через facingOffset.
/// </summary>
public static class WeaponGizmo
{
    /// <summary>Доля дальности, на которую тянутся рёбра конуса: на всю длину они превращают экран в паутину.</summary>
    private const float ConeLength = 0.35f;

    public static void Draw(CanvasItem canvas, WeaponDefinition weapon, float facingOffset = 0f)
    {
        if (weapon == null)
            return;

        // Слабая заливка зоны + устойчивый контур дальности
        ShapeDraw.Circle(canvas, Vector2.Zero, weapon.RangePx,
            DrawTheme.Radius(VizKind.Attack), 64);

        float cone = weapon.AimCone;
        float length = weapon.RangePx * ConeLength;
        var edge = DrawTheme.Line(VizKind.Attack, alpha: 0.35f, width: 1f);

        ShapeDraw.Line(canvas, Vector2.Zero, Heading.Forward(facingOffset + cone) * length, edge);
        ShapeDraw.Line(canvas, Vector2.Zero, Heading.Forward(facingOffset - cone) * length, edge);
    }
}
