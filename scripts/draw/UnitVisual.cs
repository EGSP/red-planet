using Godot;

/// <summary>
/// Запасное изображение подвижной сущности: круг цвета вида с указателем носа.
///
/// ПОЧЕМУ ОДИН КРУГ НА ВСЕХ. Раньше здесь стоял процедурный силуэт из десятка форм корпуса,
/// надстроек тира и контуров брони, задаваемых ключами .toml. Он существовал затем, чтобы
/// сущность без рисунка всё же читалась на карте, и попутно завёл второй способ описывать
/// изображение — независимый от сцены модели и расходившийся с нею. Изображением теперь
/// распоряжается только модель, поэтому запасному виду остаётся одна задача: показать, что
/// сущность здесь есть и куда она смотрит, пока рисунок не нарисован. Круг решает её
/// целиком, а всё, что сложнее, было бы вторым описанием изображения.
///
/// Рисуется в координатах вызывающего узла с началом в центре сущности и носом по +X.
/// <paramref name="origin"/> и <paramref name="angle"/> нужны тем, кто рисует не в своём
/// начале координат: иконке панели и полю редактора контента.
/// </summary>
public static class UnitVisual
{
    /// <summary>Радиус круга относительно радиуса корпуса из справочника.</summary>
    private const float BodyScale = 1f;

    public static void Draw(CanvasItem canvas, UnitDefinition def, float radius,
        Vector2 origin = default, float angle = 0f)
    {
        if (canvas == null || def == null || radius <= 0f)
            return;

        bool placed = origin != Vector2.Zero || angle != 0f;

        if (placed)
            canvas.DrawSetTransform(origin, angle, Vector2.One);

        ShapeDraw.Circle(canvas, Vector2.Zero, radius * BodyScale,
            ShapeStyle.Filled(def.Color, new Color(0f, 0f, 0f, 0.4f), 2f, WidthMode.Screen), 24);

        DrawNose(canvas, def, radius);

        if (placed)
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// Наибольшее удаление нарисованной точки от центра. Нужно тому, кто вписывает запасное
    /// изображение в отведённую площадь; за круг ничто не выходит, поэтому величина равна
    /// его радиусу.
    /// </summary>
    public static float Extent(float radius) => radius * BodyScale;

    /// <summary>
    /// Треугольник на корпусе: нос совпадает с направлением движения, а не с инструментом.
    /// Без него круг не показывает, куда сущность повёрнута.
    /// </summary>
    private static void DrawNose(CanvasItem canvas, UnitDefinition def, float radius)
    {
        float nose = radius * 0.62f;
        float back = radius * 0.12f;
        float half = radius * 0.32f;

        var tip = new[]
        {
            new Vector2(nose, 0f),
            new Vector2(back, -half),
            new Vector2(back, half),
        };

        ShapeDraw.Polygon(canvas, tip,
            ShapeStyle.Filled(def.Color.Lightened(0.25f), new Color(0f, 0f, 0f, 0.55f), 1.5f,
                WidthMode.Screen));
    }
}
