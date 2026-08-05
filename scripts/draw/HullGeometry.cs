using Godot;

/// <summary>
/// Составные силуэты корпуса: те, что не выражаются одной фигурой.
///
/// ПОЧЕМУ ОТДЕЛЬНО ОТ <see cref="ShapeDraw"/>. Там лежат примитивы, не знающие, что рисуют;
/// здесь — раскладка конкретных силуэтов на примитивы. Смешивать нельзя: примитивами
/// пользуется весь проект, включая интерфейс и отладочную отрисовку, а силуэт корпуса
/// нужен ровно одному месту.
///
/// ПОЧЕМУ НАБОР ВЫПУКЛЫХ ЧАСТЕЙ, А НЕ ОДИН МНОГОУГОЛЬНИК. Заливка невыпуклого контура
/// в Godot проходит без триангуляции и на вогнутых углах даёт наложения. Поэтому каждый
/// силуэт разложен на выпуклые части, и это же даёт нужный вид: части читаются как
/// отдельные плиты корпуса, а границы между ними — как стыки.
///
/// Координаты частей заданы в долях радиуса при направлении вперёд по +X. Хитбокс остаётся
/// кругом радиусом <see cref="UnitDefinition.RadiusPx"/> при любом силуэте, поэтому части
/// намеренно выходят за единицу: силуэт крупной машины должен читаться крупным, а расчёт
/// столкновений от этого не зависит.
/// </summary>
public static class HullGeometry
{
    /// <summary>Раскладывается ли силуэт на части. Ложь — рисуется одной фигурой.</summary>
    public static bool Composite(HullShape shape) =>
        shape is HullShape.Arrow or HullShape.Crescent or HullShape.Star or HullShape.Fortress;

    /// <summary>Выпуклые части силуэта, отмасштабированные под радиус.</summary>
    public static Vector2[][] Parts(HullShape shape, float radius) =>
        Scale(Template(shape), radius);

    /// <summary>
    /// Какие части рисуются осветлённым оттенком. Длина совпадает с длиной
    /// <see cref="Parts"/>, порядок тот же.
    ///
    /// ПОЧЕМУ ОТТЕНОК, А НЕ ОБВОДКА. Части нужно отличать друг от друга, иначе крупная
    /// машина выглядит сплошным пятном заливки. Обводка каждой части давала бы чёрную
    /// сетку по всему корпусу: стыки видны там, где части соприкасаются, а разложение
    /// на выпуклые куски продиктовано заливкой, а не рисунком, и показывать его незачем.
    /// Оттенок разделяет ровно то, что задумано разделить, — надстройку от корпуса,
    /// ядро от лучей, — и ничего сверх того.
    /// </summary>
    public static bool[] Accents(HullShape shape) => shape switch
    {
        HullShape.Arrow => ArrowAccents,
        HullShape.Crescent => CrescentAccents(),
        HullShape.Star => StarAccents(),
        HullShape.Fortress => FortressAccents,
        _ => System.Array.Empty<bool>(),
    };

    // ── Шаблоны ───────────────────────────────────────────────────────────────────

    private static Vector2[][] Template(HullShape shape) => shape switch
    {
        HullShape.Arrow => Arrow,
        HullShape.Crescent => Crescent(),
        HullShape.Star => Star(),
        HullShape.Fortress => Fortress,
        _ => System.Array.Empty<Vector2[]>(),
    };

    /// <summary>
    /// Стреловидный: клин носа, два отнесённых назад крыла, кормовой блок.
    /// Силуэт вытянут поперёк — машина читается как широкая и быстрая.
    /// </summary>
    private static readonly Vector2[][] Arrow =
    {
        new[]
        {
            new Vector2(1.35f, 0f),
            new Vector2(0.35f, -0.34f),
            new Vector2(-0.35f, -0.26f),
            new Vector2(-0.35f, 0.26f),
            new Vector2(0.35f, 0.34f),
        },
        new[]
        {
            new Vector2(0.40f, -0.30f),
            new Vector2(-0.25f, -1.02f),
            new Vector2(-0.85f, -0.80f),
            new Vector2(-0.40f, -0.28f),
        },
        new[]
        {
            new Vector2(0.40f, 0.30f),
            new Vector2(-0.25f, 1.02f),
            new Vector2(-0.85f, 0.80f),
            new Vector2(-0.40f, 0.28f),
        },
        new[]
        {
            new Vector2(-0.35f, -0.26f),
            new Vector2(-0.95f, -0.19f),
            new Vector2(-0.95f, 0.19f),
            new Vector2(-0.35f, 0.26f),
        },
    };

    // Светлеет клин носа: он и есть то, чем стреловидный корпус читается как стрела
    private static readonly bool[] ArrowAccents = { true, false, false, false };

    /// <summary>
    /// Блочная крепость: длинный корпус с носовой надстройкой и двумя спонсонами.
    /// Единственный силуэт, у которого длина заметно больше ширины.
    /// </summary>
    private static readonly Vector2[][] Fortress =
    {
        new[]
        {
            new Vector2(1.05f, -0.40f),
            new Vector2(1.30f, -0.15f),
            new Vector2(1.30f, 0.15f),
            new Vector2(1.05f, 0.40f),
            new Vector2(-0.85f, 0.52f),
            new Vector2(-1.15f, 0.26f),
            new Vector2(-1.15f, -0.26f),
            new Vector2(-0.85f, -0.52f),
        },
        new[]
        {
            new Vector2(0.35f, -0.42f),
            new Vector2(0.35f, -0.88f),
            new Vector2(-0.45f, -0.88f),
            new Vector2(-0.45f, -0.42f),
        },
        new[]
        {
            new Vector2(0.35f, 0.42f),
            new Vector2(0.35f, 0.88f),
            new Vector2(-0.45f, 0.88f),
            new Vector2(-0.45f, 0.42f),
        },
        new[]
        {
            new Vector2(0.78f, -0.22f),
            new Vector2(1.02f, 0f),
            new Vector2(0.78f, 0.22f),
            new Vector2(0.28f, 0.22f),
            new Vector2(0.28f, -0.22f),
        },
    };

    // Светлеет носовая надстройка, спонсоны остаются в тон корпусу: иначе машина
    // распадается на три отдельных пятна вместо одного длинного корпуса
    private static readonly bool[] FortressAccents = { false, false, false, true };

    // ── Построенные счётом ────────────────────────────────────────────────────────

    private const int CrescentSegments = 12;
    // Половина раствора подковы в долях полного круга. При 0.17 полоса занимает около
    // двух третей окружности, и раствор читается как хват, а не как разомкнутая дуга
    private const float CrescentGap = 0.17f;
    private const float CrescentInner = 0.54f;
    private const float CrescentClaw = 0.62f;

    /// <summary>
    /// Подкова: кольцевая полоса, разомкнутая вперёд, с двумя вынесенными вперёд клыками
    /// и ядром в середине. Полоса набирается четырёхугольниками — по той же причине,
    /// по которой силуэты вообще разложены на выпуклые части.
    /// </summary>
    private static Vector2[][] Crescent()
    {
        var parts = new Vector2[CrescentSegments + 3][];

        float start = Mathf.Tau * CrescentGap;
        float sweep = Mathf.Tau * (1f - CrescentGap * 2f);

        for (int i = 0; i < CrescentSegments; i++)
        {
            float a0 = start + sweep * i / CrescentSegments;
            float a1 = start + sweep * (i + 1) / CrescentSegments;

            parts[i] = new[]
            {
                Polar(a0, CrescentInner),
                Polar(a0, 1f),
                Polar(a1, 1f),
                Polar(a1, CrescentInner),
            };
        }

        // Клыки: продолжают концы полосы вперёд, отчего раствор читается как хват
        parts[CrescentSegments] = Claw(start);
        parts[CrescentSegments + 1] = Claw(start + sweep);
        parts[CrescentSegments + 2] = Ring(8, 0.34f);

        return parts;
    }

    private static Vector2[] Claw(float angle)
    {
        var inner = Polar(angle, CrescentInner);
        var outer = Polar(angle, 1f);
        var reach = new Vector2(CrescentClaw, 0f);

        return new[] { inner, outer, outer + reach, inner + reach * 0.55f };
    }

    /// <summary>
    /// Полоса идёт в тон, светлеют клыки и ядро. Чередовать оттенок по сегментам полосы
    /// нельзя: полоса разбита на куски ради заливки, и полосатость показала бы разбиение,
    /// которого в замысле нет.
    /// </summary>
    private static bool[] CrescentAccents()
    {
        var accents = new bool[CrescentSegments + 3];

        accents[CrescentSegments] = true;
        accents[CrescentSegments + 1] = true;
        accents[CrescentSegments + 2] = true;

        return accents;
    }

    private const int StarRays = 6;
    private const float StarLong = 1.22f;
    private const float StarShort = 0.78f;
    private const float StarBase = 0.44f;

    /// <summary>
    /// Звезда: шесть лучей попеременной длины вокруг шестигранного ядра. Луч — треугольник,
    /// опирающийся на ядро, поэтому ходовая часть читается как расходящиеся опоры.
    /// </summary>
    private static Vector2[][] Star()
    {
        var parts = new Vector2[StarRays + 1][];

        for (int i = 0; i < StarRays; i++)
        {
            float angle = Mathf.Tau * i / StarRays;
            float reach = i % 2 == 0 ? StarLong : StarShort;
            float spread = Mathf.Tau / StarRays * 0.42f;

            parts[i] = new[]
            {
                Polar(angle, reach),
                Polar(angle - spread, StarBase),
                Polar(angle + spread, StarBase),
            };
        }

        parts[StarRays] = Ring(6, 0.52f);

        return parts;
    }

    /// <summary>Светлеет только ядро: лучи обязаны читаться как одно целое с ним.</summary>
    private static bool[] StarAccents()
    {
        var accents = new bool[StarRays + 1];

        accents[StarRays] = true;

        return accents;
    }

    private static Vector2[] Ring(int sides, float radius)
    {
        var points = new Vector2[sides];

        for (int i = 0; i < sides; i++)
            points[i] = Polar(Mathf.Tau * i / sides, radius);

        return points;
    }

    private static Vector2 Polar(float angle, float radius) =>
        new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

    private static Vector2[][] Scale(Vector2[][] template, float radius)
    {
        var parts = new Vector2[template.Length][];

        for (int i = 0; i < template.Length; i++)
        {
            var source = template[i];
            var scaled = new Vector2[source.Length];

            for (int j = 0; j < source.Length; j++)
                scaled[j] = source[j] * radius;

            parts[i] = scaled;
        }

        return parts;
    }
}
