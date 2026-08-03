using System.Collections.Generic;
using Godot;

/// <summary>Одно место будущего строения: где, под каким углом и годится ли оно.</summary>
public readonly struct BuildSpot
{
    public readonly Vector2 Center;
    public readonly float Facing;

    /// <summary>Годится ли место. Негодные остаются в плане: игрок должен видеть отказ.</summary>
    public readonly bool Valid;

    public BuildSpot(Vector2 center, float facing, bool valid)
    {
        Center = center;
        Facing = facing;
        Valid = valid;
    }
}

/// <summary>
/// План застройки: во что превращается протаскивание мыши с выбранной постройкой.
///
/// ЗАЧЕМ ОТДЕЛЬНЫМ КЛАССОМ И БЕЗ НОД. План спрашивают дважды и с разными намерениями:
/// призрак под курсором — каждый кадр, чтобы показать, и постановка каркасов — один раз,
/// чтобы выполнить. Оба обязаны получить один и тот же ответ, иначе игрок поставит не то,
/// что видел. Общий расчёт это гарантирует, а отсутствие нод делает его проверяемым
/// без запуска мира.
///
/// ЧТО ЗАДАЁТ ПРОТАСКИВАНИЕ. Вектор от точки нажатия к курсору один, а решает он сразу три
/// вещи: направление ряда, угол строений и протяжённость раскладки. Отсюда и берётся то,
/// что вращение и раскладка работают одновременно, — это не два приёма, а два следствия
/// одного движения.
/// </summary>
public static class BuildPlan
{
    /// <summary>
    /// Короче этого протаскивание считается щелчком: угол по нему не читается, потому что
    /// дрожание руки на паре пикселей давало бы произвольный поворот.
    /// </summary>
    public const float AngleThreshold = 8f;

    /// <summary>
    /// Рассчитать план. Список заполняется заново, первым в нём всегда идёт место
    /// под точкой нажатия: оно и есть то, что игрок выбрал, а остальные к нему пристроены.
    /// </summary>
    public static void Compute(GameManager gm, UnitDefinition def, Vector2 anchor, Vector2 cursor,
        bool alt, List<BuildSpot> into)
    {
        into.Clear();

        if (gm == null || def == null)
            return;

        var drag = cursor - anchor;
        float length = drag.Length();
        float facing = Facing(def, drag, length);

        // Прилипание считается от точки нажатия и только от неё: остальные места ряда
        // отсчитываются от неё же, и подвинься каждое само по себе — ряд перестал бы
        // быть рядом. Негодные места из ряда просто выпадают
        var origin = Placement.Snap(gm, def, anchor, facing);

        var pattern = Pattern(def, alt);
        var taken = new List<Obb>();

        if (pattern == BuildPattern.MetalArea)
        {
            Deposits(gm, def, origin, facing, length, taken, into);
            return;
        }

        Accept(gm, def, origin, facing, taken, into);

        if (pattern == BuildPattern.None || length < AngleThreshold)
            return;

        var direction = drag / length;

        switch (pattern)
        {
            case BuildPattern.Line:
                Line(gm, def, origin, facing, direction, length, taken, into);
                break;

            case BuildPattern.Diamond:
                Diamond(gm, def, origin, facing, direction, length, taken, into);
                break;

            default:
                Field(gm, def, origin, facing, direction, length, taken, into);
                break;
        }
    }

    /// <summary>Какая раскладка сейчас действует: обычная или та, что под Alt.</summary>
    public static BuildPattern PatternOf(UnitDefinition def, bool alt) =>
        alt && def.PatternAlt != BuildPattern.None ? def.PatternAlt : def.Pattern;

    /// <summary>Какая раскладка сейчас действует: обычная или та, что под Alt.</summary>
    private static BuildPattern Pattern(UnitDefinition def, bool alt) => PatternOf(def, alt);

    /// <summary>
    /// Угол постройки. Строение разворачивается поперёк протаскивания, а не вдоль: ряд
    /// выкладывается фронтом, и смотреть строения обязаны в ту сторону, которую фронт
    /// прикрывает, — иначе турели в стене глядели бы вдоль неё.
    ///
    /// Экстрактор не поворачивается вовсе: он привязан к залежи, и раскладка у него
    /// не рядовая, поэтому поперечника у неё нет.
    /// </summary>
    private static float Facing(UnitDefinition def, Vector2 drag, float length)
    {
        float own = Mathf.DegToRad(def.FacingDegrees);

        if (def.RequiresMetalSpot || length < AngleThreshold)
            return own;

        return drag.Angle() + Mathf.Pi * 0.5f;
    }

    /// <summary>
    /// Цепочка вдоль протаскивания. Промежуток постоянен и равен шагу: он выведен из размера
    /// строения, и растягивать его до курсора нельзя.
    ///
    /// РАСТЯЖЕНИЕ БЫЛО ОШИБКОЙ. Промежуток, поделённый на число мест, давал последнее строение
    /// точно под курсором, но ценой того, что весь ряд ехал от любого движения мыши: два
    /// одинаково выложенных ряда получались с разными просветами, и застройка переставала
    /// быть плотной. Курсор задаёт, докуда тянется ряд, а не как в нём стоят строения.
    /// </summary>
    private static void Line(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        Vector2 direction, float length, List<Obb> taken, List<BuildSpot> into)
    {
        float step = Step(def, facing, direction);
        int count = Mathf.FloorToInt(length / step);

        for (int i = 1; i <= count && into.Count < Const.PatternLimit; i++)
            Accept(gm, def, origin + direction * step * i, facing, taken, into);
    }

    /// <summary>
    /// Квадратная застройка: тот же ряд, что и в цепочке, плюс такое же число рядов рядом с ним.
    ///
    /// РЕШЁТКА ИДЁТ ПО ВЕКТОРУ ПРОТАСКИВАНИЯ, промежутки в ней — размеры строения с зазором,
    /// и ничем другим они не задаются. Смысл раскладки в плотности: строения стоят стенка
    /// к стенке, а курсор решает только то, сколько их поместилось.
    ///
    /// ШИРИНА РАВНА ДЛИНЕ, потому что задать её отдельно нечем. Вектор протаскивания
    /// израсходован целиком: направление ушло на угол строений, длина — на счёт мест,
    /// а отклонение курсора от оси измерить невозможно, поскольку ось поворачивается
    /// вслед за курсором и отклонение всегда нулевое. Отсюда квадрат: сколько строений
    /// в ряду, столько и рядов.
    ///
    /// Ряды расходятся от линии протаскивания в обе стороны поровну, поэтому застройка
    /// растёт вокруг того места, куда игрок ведёт мышь, а не сносит её вбок.
    /// </summary>
    private static void Field(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        Vector2 direction, float length, List<Obb> taken, List<BuildSpot> into)
    {
        var across = direction.Orthogonal();

        float stepAlong = Step(def, facing, direction);
        float stepAcross = Step(def, facing, across);

        int count = Mathf.FloorToInt(length / stepAlong) + 1;

        if (count < 2)
            return;

        // Середина полосы приходится на линию протаскивания. При нечётном числе рядов
        // средний ряд ложится точно на неё, при чётном линия проходит между двумя рядами
        float middle = (count - 1) * 0.5f;

        for (int row = 0; row < count; row++)
        {
            for (int column = 0; column < count; column++)
            {
                if (into.Count >= Const.PatternLimit)
                    return;

                var center = origin
                             + direction * stepAlong * column
                             + across * stepAcross * (row - middle);

                // Место под точкой нажатия занесено в план первым
                if (center.DistanceSquaredTo(origin) < 1f)
                    continue;

                Accept(gm, def, center, facing, taken, into);
            }
        }
    }

    /// <summary>
    /// Ромб вокруг первой постройки: строения занимают узлы решётки, чьё расстояние
    /// от неё в шагах не превышает радиуса.
    ///
    /// РАДИУС, А НЕ ОБЛАСТЬ ДО КУРСОРА. Курсор здесь задаёт только величину — сколько шагов
    /// ромб занимает во все стороны, — а растёт раскладка вокруг первого строения. Тем она
    /// и отличается от квадрата: квадрат выкладывается от точки нажатия к курсору, а ромб
    /// разворачивается симметрично, и место, куда игрок нажал, остаётся его серединой.
    ///
    /// На первом шаге получаются четыре соседа по осям решётки, на втором к ним добавляются
    /// диагональные и следующие осевые, и так далее. Промежутки те же, что в сплошной
    /// раскладке: соседи по оси стоят стенка к стенке, а диагональные углы остаются
    /// открытыми — из-за них ромб и прикрывает больше места, чем квадрат той же плотности.
    ///
    /// Обход идёт кольцами от середины наружу: при упоре в предел числа мест в план
    /// попадает ближнее к игроку, а не случайный край раскладки.
    /// </summary>
    private static void Diamond(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        Vector2 direction, float length, List<Obb> taken, List<BuildSpot> into)
    {
        var across = direction.Orthogonal();

        float stepAlong = Step(def, facing, direction);
        float stepAcross = Step(def, facing, across);

        int radius = Mathf.FloorToInt(length / stepAlong);

        for (int ring = 1; ring <= radius; ring++)
        {
            for (int along = -ring; along <= ring; along++)
            {
                int side = ring - Mathf.Abs(along);

                // Узлы кольца: при нулевом отступе поперёк узел на кольце один, иначе их два
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    if (into.Count >= Const.PatternLimit)
                        return;

                    var center = origin
                                 + direction * stepAlong * along
                                 + across * stepAcross * side * sign;

                    Accept(gm, def, center, facing, taken, into);

                    if (side == 0)
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Раскладка по залежам: протаскивание задаёт радиус, а места берутся из карты.
    ///
    /// Порядок обхода — от ближней точки к дальней, чтобы при нехватке предела в план
    /// попало то, что ближе к выбранному игроком месту.
    /// </summary>
    private static void Deposits(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        float radius, List<Obb> taken, List<BuildSpot> into)
    {
        Accept(gm, def, origin, facing, taken, into);

        if (radius < AngleThreshold)
            return;

        var found = new List<MetalSpot>();

        foreach (var spot in gm.Index.All<MetalSpot>())
            if (spot.GlobalPosition.DistanceTo(origin) <= radius)
                found.Add(spot);

        found.Sort((left, right) => origin.DistanceSquaredTo(left.GlobalPosition)
            .CompareTo(origin.DistanceSquaredTo(right.GlobalPosition)));

        foreach (var spot in found)
        {
            if (into.Count >= Const.PatternLimit)
                return;

            // Точка под курсором уже разобрана первой записью плана: экстрактор к ней
            // притянут, и второй раз она в план попасть не должна
            if (spot.GlobalPosition.IsEqualApprox(origin))
                continue;

            Accept(gm, def, spot.GlobalPosition, facing, taken, into);
        }
    }

    /// <summary>
    /// Промежуток между соседями вдоль направления: поперечник строения плюс обязательный
    /// зазор, помноженный на множитель из справочника.
    /// </summary>
    private static float Step(UnitDefinition def, float facing, Vector2 axis)
    {
        var shape = Placement.Footprint(def, Vector2.Zero, facing);
        float span = shape.Reach(axis.Normalized()) * 2f;

        // Добавок в полпикселя разводит соседей, чьи зазоры иначе сходились бы ровно
        // в касание, — см. Const.PatternSlackPx
        return span + Const.BuildMarginPx * Mathf.Max(def.PatternStep, 0f) + Const.PatternSlackPx;
    }

    /// <summary>
    /// Занести место в план вместе с приговором. Годное запоминается: следующие места
    /// проверяются и по нему тоже, иначе два места одной партии встали бы друг на друга
    /// там, где промежуток вышел меньше обязательного.
    /// </summary>
    private static void Accept(GameManager gm, UnitDefinition def, Vector2 center, float facing,
        List<Obb> taken, List<BuildSpot> into)
    {
        bool valid = Placement.CanPlace(gm, def, center, facing, taken);

        if (valid)
            taken.Add(Placement.Footprint(def, center, facing));

        into.Add(new BuildSpot(center, facing, valid));
    }
}
