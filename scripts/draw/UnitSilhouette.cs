using Godot;

/// <summary>
/// Силуэт юнита: корпус, надстройка тира, контуры брони, передняя плита, указатель носа
/// и передняя часть инструмента.
///
/// ПОЧЕМУ ОТДЕЛЬНО ОТ <see cref="Unit"/>. Тот же силуэт рисует иконка строительной панели
/// (<see cref="UnitIcon"/>), а изображение юнита в панели и на карте обязано совпадать:
/// игрок опознаёт машину по очертанию, и расхождение между панелью и полем означало бы,
/// что заказывается одно, а выезжает другое. Общий код здесь и есть гарантия совпадения.
///
/// Рисуется в локальных координатах вызывающего узла с началом в центре машины и носом
/// по +X. Поворот и положение задаёт вызывающий: юнит своим <see cref="Node2D.Rotation"/>,
/// иконка — трансформацией отрисовки.
/// </summary>
public static class UnitSilhouette
{
    /// <summary>
    /// Нарисовать силуэт. <paramref name="toolLocal"/> — угол инструмента относительно
    /// корпуса; у иконки он нулевой, поскольку неподвижного изображения это не касается.
    ///
    /// <paramref name="origin"/> и <paramref name="angle"/> задают положение силуэта
    /// в системе координат вызывающего узла. Юниту они не нужны: он сам стоит там, где
    /// нарисован. Иконке нужны оба, поскольку центр её квадрата не совпадает с началом
    /// координат, а нос повёрнут влево.
    /// </summary>
    public static void Draw(CanvasItem canvas, UnitDefinition def, float radius,
        float toolLocal = 0f, Vector2 origin = default, float angle = 0f)
    {
        if (def == null)
            return;

        bool placed = origin != Vector2.Zero || angle != 0f;

        if (placed)
            canvas.DrawSetTransform(origin, angle, Vector2.One);

        DrawHull(canvas, def, radius);
        DrawMoveMark(canvas, def, radius);
        DrawToolMark(canvas, def, radius, origin, angle, toolLocal);

        if (placed)
            canvas.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// Насколько далеко от центра уходит самая дальняя нарисованная точка. Нужна тому,
    /// кто вписывает силуэт в отведённую площадь: радиус корпуса меньше видимого размера,
    /// поскольку части силуэта, броня и ствол намеренно выходят за него.
    /// </summary>
    public static float Extent(UnitDefinition def, float radius)
    {
        if (def == null || radius <= 0f)
            return 0f;

        float rings = 1f + 0.12f * Mathf.Max(def.ArmorRings, 0);
        float body = Mathf.Max(HullReach(def), 1f) * radius * rings;

        body = Mathf.Max(body, TrimReach(def) * radius);

        return Mathf.Max(body, ToolReach(def, radius));
    }

    /// <summary>Наибольшее удаление точки корпуса от центра в долях радиуса.</summary>
    private static float HullReach(UnitDefinition def)
    {
        if (HullGeometry.Composite(def.Hull))
            return Reach(HullGeometry.Parts(def.Hull, 1f));

        if (def.Hull != HullShape.Rect)
            return 1f;

        float aspect = Mathf.Max(def.HullAspect, 0.5f);
        return new Vector2(Mathf.Sqrt(aspect), 1f / Mathf.Sqrt(aspect)).Length();
    }

    private static float TrimReach(UnitDefinition def) =>
        Reach(HullGeometry.Trim(def.HullTrim, 1f));

    private static float Reach(Vector2[][] parts)
    {
        float reach = 0f;

        foreach (var part in parts)
            foreach (var point in part)
                reach = Mathf.Max(reach, point.Length());

        return reach;
    }

    /// <summary>Длина ствола либо дуги манипулятора. Без инструмента — ноль.</summary>
    private static float ToolReach(UnitDefinition def, float radius)
    {
        bool hasWeapon = def.Weapon != null;
        bool hasArm = def.BuildTool != null;

        if (!hasWeapon && !hasArm)
            return 0f;

        if (!hasWeapon)
            return radius * 1.15f;

        return radius + Mathf.Clamp(def.Weapon.RangePx * 0.12f, radius * 0.4f, radius * 2.2f);
    }

    /// <summary>
    /// Корпус по силуэту из определения. Простые силуэты рисуются одной фигурой, составные
    /// — набором выпуклых частей из <see cref="HullGeometry"/>.
    /// </summary>
    private static void DrawHull(CanvasItem canvas, UnitDefinition def, float radius)
    {
        var fill = ShapeStyle.Filled(def.Color, new Color(0f, 0f, 0f, 0.4f), 2f,
            WidthMode.Screen);

        DrawHullShape(canvas, def, radius, fill);

        // Надстройка тира ложится сразу за корпусом, до контуров брони: борта плеч выходят
        // на те же 1.12 радиуса, где идёт первый контур, и охватывающая линия поверх плеч
        // читается как броня по всему силуэту, а не как черта, рассёкшая надстройку
        DrawHullTrim(canvas, def, radius);

        // Дополнительные контуры брони. У составных силуэтов повтор самого силуэта дал бы
        // обводку по каждой части, то есть ту же сетку по стыкам, ради снятия которой корпус
        // и рисуется без обводки. Поэтому там броня выражена окружностью, а цвет ей берётся
        // от корпуса затемнением: чёрная линия поверх крупной машины читается как грязь
        bool composite = HullGeometry.Composite(def.Hull);
        var armour = composite
            ? def.Color.Darkened(0.45f) with { A = 0.7f }
            : new Color(0f, 0f, 0f, 0.55f);

        for (int ring = 1; ring <= def.ArmorRings; ring++)
        {
            float gap = radius * 0.12f * ring;
            var outline = ShapeStyle.Outline(armour, 1.5f, WidthMode.Screen);

            if (composite)
                ShapeDraw.Circle(canvas, Vector2.Zero, radius + gap, outline, 28);
            else
                DrawHullShape(canvas, def, radius + gap, outline);
        }

        // Ближний бой: заливка передней трети поверх корпуса. Признак берётся из
        // определения, а не выводится из дальности оружия, — см. UnitDefinition.FrontPlate
        if (def.FrontPlate)
        {
            float tip = radius * 0.55f;
            ShapeDraw.Rect(canvas, new Rect2(radius * 0.15f, -tip * 0.7f, tip, tip * 1.4f),
                ShapeStyle.Solid(def.Color.Lightened(0.15f)));
        }
    }

    /// <summary>
    /// Надстройка тира поверх корпуса: части из <see cref="HullGeometry.Trim"/> заливкой
    /// светлее корпуса и с тёмной обводкой.
    ///
    /// ПОЧЕМУ ОБВОДКА ЕСТЬ, А У СОСТАВНЫХ ЧАСТЕЙ КОРПУСА НЕТ. Там обводка проходила бы по
    /// стыкам частей одного силуэта; здесь надстройка выступает за край корпуса, и её
    /// внешняя граница проходит по фону, а не по стыку.
    /// </summary>
    private static void DrawHullTrim(CanvasItem canvas, UnitDefinition def, float radius)
    {
        var parts = HullGeometry.Trim(def.HullTrim, radius);

        if (parts.Length == 0)
            return;

        var style = ShapeStyle.Filled(def.Color.Lightened(0.2f),
            new Color(0f, 0f, 0f, 0.45f), 1.5f, WidthMode.Screen);

        foreach (var part in parts)
            ShapeDraw.Polygon(canvas, part, style);
    }

    /// <summary>Один силуэт заданным стилем. Общее место для корпуса и контуров брони.</summary>
    private static void DrawHullShape(CanvasItem canvas, UnitDefinition def, float radius,
        in ShapeStyle style)
    {
        if (HullGeometry.Composite(def.Hull))
        {
            var parts = HullGeometry.Parts(def.Hull, radius);
            var accents = HullGeometry.Accents(def.Hull);

            // Обводка снимается: она проходила бы по стыкам частей, а разложение
            // на выпуклые куски продиктовано заливкой и показывать его незачем.
            // Части различаются оттенком — см. HullGeometry.Accents
            var accent = ShapeStyle.Solid(style.Fill.Lightened(0.22f));
            var plain = ShapeStyle.Solid(style.Fill);

            for (int i = 0; i < parts.Length; i++)
            {
                bool lighten = i < accents.Length && accents[i];

                ShapeDraw.Polygon(canvas, parts[i], lighten ? accent : plain);
            }

            return;
        }

        switch (def.Hull)
        {
            case HullShape.Rect:
                DrawRectHull(canvas, def, radius, style);
                break;

            case HullShape.Hex:
                DrawPolygonHull(canvas, radius, 6, style);
                break;

            default:
                ShapeDraw.Circle(canvas, Vector2.Zero, radius, style, 24);
                break;
        }
    }

    private static void DrawRectHull(CanvasItem canvas, UnitDefinition def, float radius,
        in ShapeStyle style)
    {
        float aspect = Mathf.Max(def.HullAspect, 0.5f);
        float length = radius * 2f * Mathf.Sqrt(aspect);
        float width = radius * 2f / Mathf.Sqrt(aspect);
        ShapeDraw.Rect(canvas, new Rect2(-length * 0.5f, -width * 0.5f, length, width), style);
    }

    private static void DrawPolygonHull(CanvasItem canvas, float radius, int sides,
        in ShapeStyle style)
    {
        var points = new Vector2[sides];

        for (int i = 0; i < sides; i++)
        {
            float angle = Mathf.Tau * i / sides;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        ShapeDraw.Polygon(canvas, points, style);
    }

    /// <summary>
    /// Треугольник на корпусе: нос совпадает с направлением движения
    /// (<see cref="Node2D.Rotation"/>), а не с инструментом.
    /// </summary>
    private static void DrawMoveMark(CanvasItem canvas, UnitDefinition def, float radius)
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

    /// <summary>
    /// Передняя часть инструмента: ствол или дуга манипулятора. Рисуется со сдвигом на угол
    /// инструмента относительно корпуса, поэтому трансформация здесь своя. Задаётся она
    /// целиком, а не поверх текущей, отчего положение силуэта приходится складывать
    /// с углом инструмента вручную и возвращать по окончании.
    /// </summary>
    private static void DrawToolMark(CanvasItem canvas, UnitDefinition def, float radius,
        Vector2 origin, float angle, float toolLocal)
    {
        bool hasWeapon = def.Weapon != null;
        bool hasArm = def.BuildTool != null;

        if (!hasWeapon && !hasArm)
            return;

        canvas.DrawSetTransform(origin, angle + toolLocal, Vector2.One);

        if (!hasWeapon && hasArm)
        {
            ShapeDraw.Arc(canvas, Vector2.Zero, radius * 1.15f, -0.7f, 0.7f,
                ShapeStyle.Outline(new Color(0.45f, 0.85f, 1f, 0.85f), 2.5f, WidthMode.Screen));
        }
        else
        {
            float barrel = radius * 1.2f;
            if (hasWeapon)
                barrel = radius + Mathf.Clamp(def.Weapon.RangePx * 0.12f, radius * 0.4f,
                    radius * 2.2f);

            ShapeDraw.Line(canvas, Vector2.Zero, new Vector2(barrel, 0f),
                ShapeStyle.Outline(new Color(1f, 1f, 1f, 0.8f), 2.5f, WidthMode.Screen));
        }

        canvas.DrawSetTransform(origin, angle, Vector2.One);
    }
}
