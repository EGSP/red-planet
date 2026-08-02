using Godot;

/// <summary>
/// Режим интерпретации <see cref="ShapeStyle.StrokeWidth"/>.
/// </summary>
public enum WidthMode
{
    /// <summary>Толщина в мировых единицах; на экране меняется вместе с масштабом.</summary>
    World,

    /// <summary>Толщина в экранных пикселях; мировая величина пересчитывается по canvas transform.</summary>
    Screen,

    /// <summary>
    /// Мировая толщина, но не тоньше заданного числа экранных пикселей.
    /// Числовое значение используется и как мировая величина, и как нижняя граница в экранных пикселях.
    /// </summary>
    MinScreen,
}

/// <summary>
/// Стиль фигуры: заливка и контур задаются независимо.
/// Нулевая альфа-компонента отключает соответствующую часть отрисовки.
/// </summary>
public readonly struct ShapeStyle
{
    public readonly Color Fill;
    public readonly Color Stroke;
    public readonly float StrokeWidth;
    public readonly WidthMode WidthMode;
    public readonly bool Antialiased;

    public ShapeStyle(
        Color fill,
        Color stroke,
        float strokeWidth,
        WidthMode widthMode = WidthMode.World,
        bool antialiased = true)
    {
        Fill = fill;
        Stroke = stroke;
        StrokeWidth = strokeWidth;
        WidthMode = widthMode;
        Antialiased = antialiased;
    }

    public bool HasFill => Fill.A > 0f;

    public bool HasStroke => Stroke.A > 0f && StrokeWidth > 0f;

    /// <summary>Только контур.</summary>
    public static ShapeStyle Outline(
        Color stroke,
        float width,
        WidthMode mode = WidthMode.Screen,
        bool antialiased = true) =>
        new(Colors.Transparent, stroke, width, mode, antialiased);

    /// <summary>Только заливка.</summary>
    public static ShapeStyle Solid(Color fill) =>
        new(fill, Colors.Transparent, 0f, WidthMode.World, true);

    /// <summary>Заливка и контур.</summary>
    public static ShapeStyle Filled(
        Color fill,
        Color stroke,
        float width,
        WidthMode mode = WidthMode.Screen,
        bool antialiased = true) =>
        new(fill, stroke, width, mode, antialiased);
}

/// <summary>
/// Общие 2D-примитивы поверх <see cref="CanvasItem"/>.
/// Координаты и радиусы задаются в мировых единицах в локальном пространстве холста.
/// Толщина контура учитывает <see cref="WidthMode"/> и фактический canvas transform.
/// </summary>
public static class ShapeDraw
{
    /// <summary>
    /// Группа SceneTree для <see cref="CanvasItem"/>, которым нужна перерисовка
    /// при изменении масштаба камеры (режимы <see cref="WidthMode.Screen"/> и
    /// <see cref="WidthMode.MinScreen"/>).
    /// </summary>
    public const string ZoomGroup = "shape_draw_zoom";

    private const float MinScale = 1e-4f;
    private const float MinLength = 1e-4f;

    /// <summary>
    /// Явно включить инвалидацию при изменении масштаба. Вызовы с экранными режимами
    /// толщины регистрируют холст сами; метод нужен, если перерисовка зависит от масштаба
    /// по иной причине.
    /// </summary>
    public static void TrackZoom(CanvasItem canvas)
    {
        if (canvas == null || !GodotObject.IsInstanceValid(canvas))
            return;

        if (!canvas.IsInGroup(ZoomGroup))
            canvas.AddToGroup(ZoomGroup);
    }

    /// <summary>
    /// Перерисовать зарегистрированные холсты после реального изменения масштаба.
    /// Вызывается из <see cref="CameraRig"/>; к <c>GameManager</c> не привязан.
    /// </summary>
    public static void NotifyZoomChanged(SceneTree tree)
    {
        tree?.CallGroup(ZoomGroup, CanvasItem.MethodName.QueueRedraw);
    }

    /// <summary>Масштаб локальных единиц холста к экранным пикселям.</summary>
    public static float CanvasScale(CanvasItem canvas)
    {
        // Итоговый transform: нода, родители и canvas transform камеры.
        float scale = canvas.GetGlobalTransformWithCanvas().X.Length();
        return scale < MinScale ? MinScale : scale;
    }

    /// <summary>Толщина контура в мировых единицах с учётом режима и canvas transform.</summary>
    public static float WorldWidth(CanvasItem canvas, float width, WidthMode mode)
    {
        if (width <= 0f)
            return 0f;

        return mode switch
        {
            WidthMode.World => width,
            WidthMode.Screen => width / CanvasScale(canvas),
            WidthMode.MinScreen => Mathf.Max(width, width / CanvasScale(canvas)),
            _ => width,
        };
    }

    public static void Line(CanvasItem canvas, Vector2 from, Vector2 to, in ShapeStyle style)
    {
        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);
        canvas.DrawLine(from, to, style.Stroke, width, style.Antialiased);
    }

    public static void Polyline(
        CanvasItem canvas,
        Vector2[] points,
        in ShapeStyle style,
        bool closed = false)
    {
        if (points == null || points.Length < 2)
            return;

        if (style.HasFill && points.Length >= 3)
            canvas.DrawColoredPolygon(points, style.Fill);

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);

        if (closed)
        {
            var loop = new Vector2[points.Length + 1];
            points.CopyTo(loop, 0);
            loop[^1] = points[0];
            canvas.DrawPolyline(loop, style.Stroke, width, style.Antialiased);
            return;
        }

        canvas.DrawPolyline(points, style.Stroke, width, style.Antialiased);
    }

    /// <summary>
    /// Стрелка от <paramref name="from"/> к <paramref name="to"/>.
    /// Если <paramref name="headLength"/> не задан, длина наконечника берётся от толщины контура.
    /// </summary>
    public static void Arrow(
        CanvasItem canvas,
        Vector2 from,
        Vector2 to,
        in ShapeStyle style,
        float headLength = 0f)
    {
        if (!style.HasStroke)
            return;

        var delta = to - from;
        float length = delta.Length();

        if (length < MinLength)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);
        var direction = delta / length;

        float head = headLength > 0f
            ? headLength
            : Mathf.Clamp(width * 4f, 4f / CanvasScale(canvas), length * 0.5f);

        canvas.DrawLine(from, to, style.Stroke, width, style.Antialiased);
        canvas.DrawLine(to, to - direction.Rotated(0.4f) * head, style.Stroke, width, style.Antialiased);
        canvas.DrawLine(to, to - direction.Rotated(-0.4f) * head, style.Stroke, width, style.Antialiased);
    }

    /// <summary>
    /// Дуга окружности. Заливка рисуется сектором от центра; контур — линией дуги.
    /// Углы в радианах, как у <see cref="CanvasItem.DrawArc"/>.
    /// </summary>
    public static void Arc(
        CanvasItem canvas,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        in ShapeStyle style,
        int pointCount = 0)
    {
        if (radius <= 0f)
            return;

        float sweep = endAngle - startAngle;
        int points = ResolvePointCount(pointCount, radius, sweep);

        if (style.HasFill && Mathf.Abs(sweep) > MinLength)
            DrawSectorFill(canvas, center, radius, startAngle, endAngle, style.Fill, points, style.Antialiased);

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);
        canvas.DrawArc(center, radius, startAngle, endAngle, points, style.Stroke, width, style.Antialiased);
    }

    /// <summary>Круг с независимыми заливкой и контуром.</summary>
    public static void Circle(
        CanvasItem canvas,
        Vector2 center,
        float radius,
        in ShapeStyle style,
        int pointCount = 0)
    {
        if (radius <= 0f)
            return;

        int points = ResolvePointCount(pointCount, radius, Mathf.Tau);

        if (style.HasFill)
            canvas.DrawCircle(center, radius, style.Fill, true, -1f, style.Antialiased);

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);
        canvas.DrawArc(center, radius, 0f, Mathf.Tau, points, style.Stroke, width, style.Antialiased);
    }

    /// <summary>
    /// Кольцо между внутренним и внешним радиусом.
    /// Заливка — полупрозрачная площадь кольца (толстая дуга по средней линии);
    /// контур — внутренняя и внешняя окружности.
    /// </summary>
    public static void Ring(
        CanvasItem canvas,
        Vector2 center,
        float innerRadius,
        float outerRadius,
        in ShapeStyle style,
        int pointCount = 0)
    {
        float inner = Mathf.Min(innerRadius, outerRadius);
        float outer = Mathf.Max(innerRadius, outerRadius);

        if (outer <= 0f)
            return;

        inner = Mathf.Max(inner, 0f);
        int points = ResolvePointCount(pointCount, outer, Mathf.Tau);

        if (style.HasFill && outer > inner)
        {
            float mid = (inner + outer) * 0.5f;
            float band = outer - inner;
            canvas.DrawArc(center, mid, 0f, Mathf.Tau, points, style.Fill, band, style.Antialiased);
        }

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);

        if (inner > 0f)
            canvas.DrawArc(center, inner, 0f, Mathf.Tau, points, style.Stroke, width, style.Antialiased);

        canvas.DrawArc(center, outer, 0f, Mathf.Tau, points, style.Stroke, width, style.Antialiased);
    }

    /// <summary>Осепараллельный прямоугольник в локальных координатах холста.</summary>
    public static void Rect(CanvasItem canvas, Rect2 rect, in ShapeStyle style)
    {
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
            return;

        if (style.HasFill)
            canvas.DrawRect(rect, style.Fill, true, -1f, style.Antialiased);

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);
        canvas.DrawRect(rect, style.Stroke, false, width, style.Antialiased);
    }

    /// <summary>
    /// Ориентированный прямоугольник. Углы <see cref="Obb"/> считаются мировыми
    /// и переводятся в локальное пространство <see cref="Node2D"/>, если холст им является.
    /// </summary>
    public static void Obb(CanvasItem canvas, in Obb area, in ShapeStyle style)
    {
        if (area.IsEmpty)
            return;

        var corners = area.Corners();
        var local = new Vector2[corners.Length];

        if (canvas is Node2D node)
        {
            for (int i = 0; i < corners.Length; i++)
                local[i] = node.ToLocal(corners[i]);
        }
        else
        {
            for (int i = 0; i < corners.Length; i++)
                local[i] = corners[i];
        }

        Polygon(canvas, local, style, closed: true);
    }

    /// <summary>Многоугольник по вершинам в локальных координатах холста.</summary>
    public static void Polygon(
        CanvasItem canvas,
        Vector2[] points,
        in ShapeStyle style,
        bool closed = true)
    {
        if (points == null || points.Length < 2)
            return;

        if (style.HasFill && points.Length >= 3)
            canvas.DrawColoredPolygon(points, style.Fill);

        if (!style.HasStroke)
            return;

        TrackIfNeeded(canvas, style.WidthMode);
        float width = WorldWidth(canvas, style.StrokeWidth, style.WidthMode);

        if (closed && points.Length >= 2)
        {
            var loop = new Vector2[points.Length + 1];
            points.CopyTo(loop, 0);
            loop[^1] = points[0];
            canvas.DrawPolyline(loop, style.Stroke, width, style.Antialiased);
            return;
        }

        canvas.DrawPolyline(points, style.Stroke, width, style.Antialiased);
    }

    private static void TrackIfNeeded(CanvasItem canvas, WidthMode mode)
    {
        if (mode is WidthMode.Screen or WidthMode.MinScreen)
            TrackZoom(canvas);
    }

    private static int ResolvePointCount(int requested, float radius, float sweep)
    {
        if (requested > 2)
            return requested;

        float fraction = Mathf.Clamp(Mathf.Abs(sweep) / Mathf.Tau, 0.05f, 1f);
        int points = Mathf.CeilToInt(Mathf.Max(12f, radius * 0.5f) * fraction);
        return Mathf.Clamp(points, 8, 128);
    }

    private static void DrawSectorFill(
        CanvasItem canvas,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle,
        Color fill,
        int pointCount,
        bool antialiased)
    {
        // Сектор: центр и точки на дуге. Для полного круга достаточно обычного круга.
        if (Mathf.IsEqualApprox(Mathf.Abs(endAngle - startAngle), Mathf.Tau))
        {
            canvas.DrawCircle(center, radius, fill, true, -1f, antialiased);
            return;
        }

        var points = new Vector2[pointCount + 2];
        points[0] = center;

        for (int i = 0; i <= pointCount; i++)
        {
            float t = i / (float)pointCount;
            float angle = Mathf.Lerp(startAngle, endAngle, t);
            points[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        canvas.DrawColoredPolygon(points, fill);
    }
}
