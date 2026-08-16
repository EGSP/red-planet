using Godot;

/// <summary>
/// Отрисовка постройки без игровой сессии.
///
/// ПОРЯДОК СЛОЁВ совпадает с Building._Draw: границы места, площадка, запасной
/// прямоугольник. Вынесено из Building, чтобы редактор контента и игра не держали две копии
/// одной последовательности. HealthBar и UnitGizmos сюда не входят — в редакторе прочности
/// нет, а круги дальности рисует Preview сам.
///
/// Корпус здесь не рисуется вовсе: изображением распоряжается сцена модели, добавленная
/// отдельным узлом. Прямоугольник остаётся запасным видом для тех определений, у которых
/// модели ещё нет.
/// </summary>
public static class BuildingVisual
{
    public static void Draw(
        CanvasItem canvas,
        UnitDefinition def,
        Vector2 origin,
        float bodyFacing = 0f,
        bool showFootprint = true,
        bool showMargin = false,
        float alpha = 1f,
        float presentationScale = 1f)
    {
        if (def == null || canvas == null)
            return;

        presentationScale = Mathf.Max(presentationScale, 0.01f);
        var size = new Vector2(Mathf.Max(def.Width, 1), Mathf.Max(def.Height, 1))
                   * Const.Unit * presentationScale;
        var rect = new Rect2(-size * 0.5f, size);

        canvas.DrawSetTransform(origin, bodyFacing, Vector2.One);

        if (showMargin)
        {
            var margin = rect.Grow(Const.BuildMarginPx * presentationScale);
            ShapeDraw.Rect(canvas, margin,
                ShapeStyle.Outline(new Color(1f, 0.85f, 0.3f, 0.35f * alpha), 1f, WidthMode.Screen));
        }

        if (showFootprint)
            ShapeDraw.Rect(canvas, rect,
                ShapeStyle.Outline(new Color(0.7f, 0.85f, 1f, 0.55f * alpha), 1f, WidthMode.Screen));

        BuildingSkirt.Draw(canvas, rect);

        // Корпус со сценой модели рисует не этот код, а сам экземпляр сцены: он
        // добавлен отдельным узлом и лежит поверх площадки. Здесь остаются только
        // границы места — они принадлежат постройке, а не её изображению
        if (def.HasModel)
        {
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return;
        }

        var color = def.Color;
        color.A *= alpha;
        ShapeDraw.Rect(canvas, rect,
            ShapeStyle.Filled(color, new Color(0f, 0f, 0f, 0.35f * alpha), 2f,
                WidthMode.Screen));

        float span = Mathf.Min(size.X, size.Y);
        ShapeDraw.Line(canvas, Vector2.Right * span * 0.2f, Vector2.Right * span * 0.45f,
            ShapeStyle.Outline(new Color(1f, 1f, 1f, 0.5f * alpha), 3f, WidthMode.Screen));

        canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
