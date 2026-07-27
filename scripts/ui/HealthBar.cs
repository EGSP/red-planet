using Godot;

/// <summary>
/// Полоска прочности над сущностью. Отдельным хелпером, а не нодой: рисуют её пять разных
/// типов, и заводить каждому дочернюю ноду ради двух прямоугольников ни к чему.
///
/// Поворот корпуса компенсируется: у подвижных сущностей ось «вперёд» — это Rotation ноды,
/// и без компенсации полоска крутилась бы вместе с корпусом.
/// </summary>
public static class HealthBar
{
    private static readonly Color Back = new(0f, 0f, 0f, 0.55f);
    private static readonly Color Good = new(0.45f, 0.85f, 0.4f);
    private static readonly Color Hurt = new(0.95f, 0.75f, 0.25f);
    private static readonly Color Bad = new(0.9f, 0.3f, 0.25f);

    /// <summary>
    /// Рисует полоску на высоте y над центром сущности. Целую полоску не показываем —
    /// в мире и так тесно, а нетронутая сущность в подсказке не нуждается.
    /// </summary>
    public static void Draw(CanvasItem canvas, Health health, float width, float y,
        float facing = 0f)
    {
        if (health == null || health.Ratio >= 0.999f)
            return;

        const float height = 5f;

        // Смещение тоже разворачиваем: иначе полоска ездит по кругу вместе с корпусом,
        // ведь позиция трансформа задаётся в уже повёрнутых координатах ноды
        canvas.DrawSetTransform(new Vector2(0f, y).Rotated(-facing), -facing, Vector2.One);

        var rect = new Rect2(-width * 0.5f, -height * 0.5f, width, height);
        canvas.DrawRect(rect, Back);

        var fill = new Rect2(rect.Position, new Vector2(width * health.Ratio, height));
        canvas.DrawRect(fill, Tint(health.Ratio));

        canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static Color Tint(float ratio) => ratio switch
    {
        > 0.6f => Good,
        > 0.3f => Hurt,
        _ => Bad,
    };
}
