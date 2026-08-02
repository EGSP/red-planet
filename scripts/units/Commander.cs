using Godot;

/// <summary>
/// Коммандер игрока: единственный, кто слушает приказы, и единственный, кого нельзя потерять.
///
/// Урон он получает и показывает, но не умирает: сессия не должна обрываться от того,
/// что игрок зазевался. Счётчик полученного — цена ошибки, а не полоска до гибели.
/// Без приказов коммандер сам ведёт огонь по ближайшему врагу — стрельбу делает WeaponSystem,
/// здесь только неуязвимость и то, как это выглядит.
/// </summary>
public partial class Commander : Unit
{
    public override void _Ready()
    {
        base._Ready();

        Health.Invulnerable = true;

        GameManager.I.Commander = this;
    }

    public override void _Draw()
    {
        base._Draw();

        if (Definition == null)
            return;

        // Радиус инструмента: слабая заливка + экранный контур
        ShapeDraw.Circle(this, Vector2.Zero, Definition.WorkRangePx,
            ShapeStyle.Filled(
                new Color(0.5f, 0.8f, 1f, 0.04f),
                new Color(0.5f, 0.8f, 1f, 0.18f),
                1.5f,
                WidthMode.Screen),
            48);

        DrawDamageTaken();
    }

    /// <summary>
    /// Сколько всего прилетело. Поворот корпуса компенсируем — иначе надпись
    /// крутилась бы вместе с коммандером.
    /// </summary>
    private void DrawDamageTaken()
    {
        if (Health.TotalTaken < 1f)
            return;

        var offset = new Vector2(0f, Definition.RadiusPx + 20f).Rotated(-Rotation);
        DrawSetTransform(offset, -Rotation, Vector2.One);

        DrawString(ThemeDB.FallbackFont, new Vector2(-28f, 0f),
            $"−{Mathf.FloorToInt(Health.TotalTaken)}", HorizontalAlignment.Center, 56, 13,
            new Color(1f, 0.5f, 0.45f));

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
