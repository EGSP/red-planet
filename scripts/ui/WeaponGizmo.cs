using Godot;

/// <summary>
/// Примитив ствола: круг дальности и рёбра сектора стрельбы.
///
/// Когда круг показывать, решает <see cref="GizmoGate"/> / <see cref="UnitGizmos"/>.
/// Сам примитив одинаков у врагов, коммандера и турелей: три копии в трёх _Draw
/// развели бы поведение.
///
/// Координаты локальные относительно ноды. У турели ось башни совпадает с Rotation,
/// поэтому рёбра рисуются вдоль локальной оси X. У подвижного нода смотрит по курсу
/// движения, а сектор смещают через <c>toolOffset</c> на угол ствола относительно корпуса.
/// </summary>
public static class WeaponGizmo
{
    /// <summary>Доля дальности, на которую тянутся рёбра сектора: на всю длину они превращают экран в паутину.</summary>
    private const float ConeLength = 0.35f;

    public static void Draw(CanvasItem canvas, WeaponDefinition weapon, float toolOffset = 0f)
    {
        if (weapon == null)
            return;

        // Слабая заливка зоны + устойчивый контур дальности
        ShapeDraw.Circle(canvas, Vector2.Zero, weapon.RangePx,
            DrawTheme.Radius(VizKind.Attack), 64);

        float arc = weapon.FireArc;
        float length = weapon.RangePx * ConeLength;
        var edge = DrawTheme.Line(VizKind.Attack, alpha: 0.5f, width: 2f,
            mode: WidthMode.Screen);

        ShapeDraw.Line(canvas, Vector2.Zero, Heading.Forward(toolOffset + arc) * length, edge);
        ShapeDraw.Line(canvas, Vector2.Zero, Heading.Forward(toolOffset - arc) * length, edge);
    }
}

/// <summary>
/// Примитив сектора наведения: докуда инструмент доворачивается относительно корпуса.
///
/// ОТДЕЛЬНО ОТ <see cref="WeaponGizmo"/> ПО ДВУМ ПРИЧИНАМ. Сектор наведения принадлежит
/// не только стволу — на такой же поворотной опоре сидит рабочая рука. И отсчитывается он
/// от оси КОРПУСА, тогда как сектор стрельбы — от оси самого инструмента, поэтому рисовать
/// их одним вызовом означало бы смешать две системы отсчёта.
///
/// Координаты локальные относительно корпуса: вызывающий обязан снять с канвы поворот
/// инструмента, если он был назначен.
/// </summary>
public static class AimArcGizmo
{
    /// <summary>Прозрачность заливки. Выше — и сектор закрывает изображение под собой.</summary>
    private const float FillAlpha = 0.13f;

    private const float EdgeWidth = 2.5f;

    /// <summary>Сдвиг тона на каждый следующий инструмент — см. <see cref="Tint"/>.</summary>
    private const float TintStep = 0.075f;

    /// <summary>
    /// Сектор одного инструмента.
    /// <paramref name="radius"/> — дальность инструмента: дуга доводится до самого её края,
    /// потому что сектор и дальность вместе очерчивают зону, накрываемую без разворота
    /// носителя, и обрезанная дуга эту зону не показывала бы.
    /// <paramref name="index"/> — номер инструмента у носителя: по нему разводятся тона,
    /// иначе секторы двух стволов сливаются в одно пятно.
    /// </summary>
    public static void Draw(CanvasItem canvas, ToolDefinition tool, float radius, int index = 0)
    {
        // Круговому сектору границ нет, и рисовать нечего: заливка по всей окружности
        // сообщила бы ровно то же, что её отсутствие, закрыв при этом изображение
        if (tool == null || tool.FullCircle || radius <= 0f)
            return;

        var hue = Tint(tool, index);

        // Жёсткое крепление: сектора нет вовсе, и вместо заливки рисуется одна ось —
        // единственное направление, куда инструмент смотрит
        if (tool.Fixed)
        {
            canvas.DrawLine(Vector2.Zero, Heading.Forward(0f) * radius,
                hue with { A = 0.9f }, EdgeWidth, true);
            return;
        }

        float arc = tool.AimArc;
        int segments = Mathf.Max(8, Mathf.CeilToInt(arc * 2f / Mathf.Pi * 32f));

        canvas.DrawColoredPolygon(Fan(arc, radius, segments), hue with { A = FillAlpha });

        // Края и дуга рисуются поверх заливки и заметно ярче её: по ним читается граница,
        // тогда как заливка отвечает лишь за то, какому инструменту сектор принадлежит
        var edge = hue with { A = 0.95f };

        canvas.DrawLine(Vector2.Zero, Heading.Forward(arc) * radius, edge, EdgeWidth, true);
        canvas.DrawLine(Vector2.Zero, Heading.Forward(-arc) * radius, edge, EdgeWidth, true);
        canvas.DrawArc(Vector2.Zero, radius, -arc, arc, segments, edge, EdgeWidth, true);
    }

    /// <summary>Точки сектора: вершина в центре вращения, дальше край дуги.</summary>
    private static Vector2[] Fan(float arc, float radius, int segments)
    {
        var points = new Vector2[segments + 2];
        points[0] = Vector2.Zero;

        for (int i = 0; i <= segments; i++)
            points[i + 1] = Heading.Forward(-arc + 2f * arc * i / segments) * radius;

        return points;
    }

    /// <summary>
    /// Цвет сектора: базовый оттенок по роду инструмента, смещённый по тону на его номер.
    /// Род различается сразу (ствол красный, рука зелёная), а смещение разводит одинаковые
    /// по роду инструменты между собой.
    /// </summary>
    private static Color Tint(ToolDefinition tool, int index)
    {
        var basis = DrawTheme.Hue(tool is WeaponDefinition ? VizKind.Attack : VizKind.Work);

        if (index <= 0)
            return basis;

        return Color.FromHsv(Mathf.Wrap(basis.H + index * TintStep, 0f, 1f),
            basis.S, basis.V);
    }
}
