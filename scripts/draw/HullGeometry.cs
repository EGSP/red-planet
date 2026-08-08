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
        shape is HullShape.Arrow or HullShape.Crescent or HullShape.Star or HullShape.Fortress
            or HullShape.Sickle or HullShape.Crown;

    /// <summary>Выпуклые части силуэта, отмасштабированные под радиус.</summary>
    public static Vector2[][] Parts(HullShape shape, float radius) =>
        Scale(Template(shape), radius);

    /// <summary>
    /// Части надстройки тира, отмасштабированные под радиус. Пустой массив означает, что
    /// надстройки нет. Накладываются поверх любой базовой формы — см. <see cref="HullTrim"/>.
    /// </summary>
    public static Vector2[][] Trim(HullTrim trim, float radius) => trim switch
    {
        HullTrim.Shoulders => Scale(Shoulders, radius),
        _ => System.Array.Empty<Vector2[]>(),
    };

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
        HullShape.Sickle => SickleAccents(),
        HullShape.Crown => CrownAccents,
        _ => System.Array.Empty<bool>(),
    };

    // ── Шаблоны ───────────────────────────────────────────────────────────────────

    private static Vector2[][] Template(HullShape shape) => shape switch
    {
        HullShape.Arrow => Arrow,
        HullShape.Crescent => Crescent(),
        HullShape.Star => Star(),
        HullShape.Fortress => Fortress,
        HullShape.Sickle => Sickle(),
        HullShape.Crown => Crown,
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

    /// <summary>
    /// Венец: восьмигранное тело, вынесенный вперёд клин, два бортовых блока, кормовой
    /// блок и светлое ядро в середине. Силуэт коммандера, и он единственный на карте,
    /// поэтому позволяет себе больше частей, чем у титанов: спутать его не с чем, а найти
    /// взглядом среди своих машин нужно мгновенно.
    ///
    /// ПОЧЕМУ ЯДРО ПОСЛЕДНЕЙ ЧАСТЬЮ. Порядок частей есть порядок отрисовки, а ядро лежит
    /// поверх тела; будь оно раньше, тело закрыло бы его целиком.
    /// </summary>
    private static readonly Vector2[][] Crown =
    {
        // Тело: восьмигранник, слегка вытянутый вперёд
        new[]
        {
            new Vector2(0.92f, -0.38f),
            new Vector2(0.92f, 0.38f),
            new Vector2(0.38f, 0.86f),
            new Vector2(-0.38f, 0.86f),
            new Vector2(-0.86f, 0.38f),
            new Vector2(-0.86f, -0.38f),
            new Vector2(-0.38f, -0.86f),
            new Vector2(0.38f, -0.86f),
        },
        // Бортовые блоки: вынесены наружу за тело, отчего силуэт читается широким
        new[]
        {
            new Vector2(0.30f, -0.74f),
            new Vector2(0.34f, -1.20f),
            new Vector2(-0.42f, -1.20f),
            new Vector2(-0.50f, -0.72f),
        },
        new[]
        {
            new Vector2(-0.50f, 0.72f),
            new Vector2(-0.42f, 1.20f),
            new Vector2(0.34f, 1.20f),
            new Vector2(0.30f, 0.74f),
        },
        // Кормовой блок: уравновешивает клин, иначе силуэт валится вперёд
        new[]
        {
            new Vector2(-0.80f, -0.34f),
            new Vector2(-1.32f, -0.20f),
            new Vector2(-1.32f, 0.20f),
            new Vector2(-0.80f, 0.34f),
        },
        // Носовой клин
        new[]
        {
            new Vector2(1.46f, 0f),
            new Vector2(0.62f, -0.44f),
            new Vector2(0.62f, 0.44f),
        },
        // Ядро. Шире метки курса намеренно: та идёт до 0.62 радиуса, и ядро меньшего
        // размера ушло бы под неё целиком, а нужно, чтобы метка лежала на светлой площадке
        new[]
        {
            new Vector2(0.72f, 0f),
            new Vector2(0.36f, 0.52f),
            new Vector2(-0.36f, 0.52f),
            new Vector2(-0.72f, 0f),
            new Vector2(-0.36f, -0.52f),
            new Vector2(0.36f, -0.52f),
        },
    };

    // Светлеют клин и ядро: они задают ось машины. Бортовые и кормовой блоки остаются
    // в тон телу, иначе венец распадается на набор отдельных пятен вместо одного корпуса
    private static readonly bool[] CrownAccents = { false, false, false, false, true, true };

    // ── Надстройки тира ───────────────────────────────────────────────────────────

    /// <summary>
    /// Плечи: две трапеции по бортам, вынесенные наружу от края корпуса.
    ///
    /// ПОЧЕМУ ПО БОРТАМ, А НЕ НА НОСУ. Надстройка обязана выступать за корпус при любой
    /// базовой форме, иначе тир не читается. Носовая часть у вытянутого прямоугольника
    /// уходит на 1.26 радиуса вперёд, и клин на носу оказался бы внутри корпуса; поперёк
    /// же прямоугольник, наоборот, самый узкий, поэтому борта выступают у всех форм.
    /// Кроме того, нос занят меткой курса из <c>Unit.DrawMoveMark</c>.
    /// </summary>
    private static readonly Vector2[][] Shoulders =
    {
        new[]
        {
            new Vector2(-0.30f, -0.70f),
            new Vector2(0.32f, -0.70f),
            new Vector2(0.24f, -1.12f),
            new Vector2(-0.26f, -1.06f),
        },
        new[]
        {
            new Vector2(-0.26f, 1.06f),
            new Vector2(0.24f, 1.12f),
            new Vector2(0.32f, 0.70f),
            new Vector2(-0.30f, 0.70f),
        },
    };

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

    private const int SickleSegments = 12;
    // Половина раствора серпа в долях полного круга. При 0.26 незанятой остаётся чуть
    // больше половины окружности, и полоса читается как полумесяц, а не как подкова
    private const float SickleGap = 0.26f;
    // Толщина спины взята почти до центра: метка курса из Unit.DrawMoveMark идёт до 0.62
    // радиуса, и при тонкой полосе она висела бы в пустом растворе, ни на что не опираясь
    private const float SickleThick = 0.22f;
    // Не единица: при полном сведении концы вырождаются в линию и рога пропадают вовсе
    private const float SickleTip = 0.86f;
    // Степень сужения к остриям. Единица дала бы клин с прямыми краями, значения выше
    // держат середину полосы толстой и убирают толщину лишь у самых концов
    private const float SickleTaper = 1.5f;

    /// <summary>
    /// Серп: полоса переменной толщины, раствором вперёд. Внешний край идёт по окружности
    /// радиуса, внутренний расходится от <see cref="SickleThick"/> в середине спины до
    /// <see cref="SickleTip"/> у остриев, отчего концы сходятся в рога.
    ///
    /// ПОЧЕМУ ОТДЕЛЬНО ОТ <see cref="Crescent"/>. Подкова набрана полосой постоянной
    /// толщины с клыками и ядром и заведена под титана: на радиусе в треть клетки её ядро
    /// и клыки сливаются в пятно. Серпу же нужна ровно одна черта — сужение к остриям, —
    /// и она читается при любом размере.
    /// </summary>
    private static Vector2[][] Sickle()
    {
        var parts = new Vector2[SickleSegments][];

        float start = Mathf.Tau * SickleGap;
        float sweep = Mathf.Tau * (1f - SickleGap * 2f);

        for (int i = 0; i < SickleSegments; i++)
        {
            float u0 = (float)i / SickleSegments;
            float u1 = (float)(i + 1) / SickleSegments;
            float a0 = start + sweep * u0;
            float a1 = start + sweep * u1;

            parts[i] = new[]
            {
                Polar(a0, SickleInner(u0)),
                Polar(a0, 1f),
                Polar(a1, 1f),
                Polar(a1, SickleInner(u1)),
            };
        }

        return parts;
    }

    /// <summary>Внутренний радиус полосы в доле от внешнего: середина толстая, концы тонкие.</summary>
    private static float SickleInner(float u)
    {
        float fromMiddle = Mathf.Abs(u * 2f - 1f);

        return Mathf.Lerp(SickleThick, SickleTip, Mathf.Pow(fromMiddle, SickleTaper));
    }

    /// <summary>
    /// Светлеют по одному сегменту у каждого острия. Без этого серп остаётся однотонным
    /// пятном: составные силуэты рисуются без обводки, и на светлой поверхности рога
    /// теряются как раз там, где по ним и опознаётся форма.
    /// </summary>
    private static bool[] SickleAccents()
    {
        var accents = new bool[SickleSegments];

        accents[0] = true;
        accents[SickleSegments - 1] = true;

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
