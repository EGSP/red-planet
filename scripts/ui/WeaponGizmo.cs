using Godot;

/// <summary>
/// Как выглядит ствол в мире: круг дальности и рёбра конуса прицеливания.
///
/// Рисуют это все носители оружия — враги, коммандер, турели, — и рисуют одинаково.
/// Правило простое: если сущность стреляет, игрок видит, докуда она достаёт и куда смотрит.
/// Держать три копии этого кода в трёх _Draw было бы верным способом развести их поведение.
///
/// Координаты локальные: у подвижных сущностей и турели ось «вперёд» — это Rotation ноды,
/// поэтому в своей системе координат ствол всегда смотрит вправо. Для сущности, чья ось
/// живёт отдельным числом (статичное здание), угол передаётся через facingOffset.
/// </summary>
public static class WeaponGizmo
{
    /// <summary>Доля дальности, на которую тянутся рёбра конуса: на всю длину они превращают экран в паутину.</summary>
    private const float ConeLength = 0.35f;

    public static void Draw(CanvasItem canvas, WeaponDefinition weapon, Color tint, float facingOffset = 0f)
    {
        if (weapon == null)
            return;

        canvas.DrawArc(Vector2.Zero, weapon.RangePx, 0f, Mathf.Tau, 64, new Color(tint, 0.2f), 1.5f);

        float cone = weapon.AimCone;
        float length = weapon.RangePx * ConeLength;
        var edge = new Color(tint, 0.35f);

        canvas.DrawLine(Vector2.Zero, Heading.Forward(facingOffset + cone) * length, edge, 1f);
        canvas.DrawLine(Vector2.Zero, Heading.Forward(facingOffset - cone) * length, edge, 1f);
    }
}
