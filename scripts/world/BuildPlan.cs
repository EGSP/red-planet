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
    private const float AngleThreshold = 8f;

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

        if (pattern == BuildPattern.Line)
            Line(gm, def, origin, facing, direction, length, taken, into);
        else
            Field(gm, def, origin, facing, drag, pattern == BuildPattern.Diamond, taken, into);
    }

    /// <summary>Какая раскладка сейчас действует: обычная или та, что под Alt.</summary>
    private static BuildPattern Pattern(UnitDefinition def, bool alt) =>
        alt && def.PatternAlt != BuildPattern.None ? def.PatternAlt : def.Pattern;

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
    /// Цепочка вдоль протаскивания. Промежуток растягивается до конца отрезка, поэтому
    /// последнее строение стоит точно под курсором, а сам промежуток плавно растёт от
    /// обязательного до двойного и возвращается к обязательному, когда в ряд входит ещё одно.
    /// </summary>
    private static void Line(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        Vector2 direction, float length, List<Obb> taken, List<BuildSpot> into)
    {
        float step = Step(def, facing, direction);
        int count = Mathf.FloorToInt(length / step);

        if (count < 1)
            return;

        float spacing = length / count;

        for (int i = 1; i <= count && into.Count < Const.PatternLimit; i++)
            Accept(gm, def, origin + direction * spacing * i, facing, taken, into);
    }

    /// <summary>
    /// Заполнение области, у которой точка нажатия и курсор — противоположные углы.
    ///
    /// РЕШЁТКА ИДЁТ ПО МИРОВЫМ ОСЯМ, а не по направлению протаскивания. Иначе она вырождается:
    /// направление задаёт вектор, и в его собственных координатах курсор всегда лежит
    /// на оси, то есть у области нет второй стороны. Строения при этом всё равно повёрнуты
    /// по вектору — ряды остаются ровными, потому что промежуток считается по тени
    /// повёрнутого прямоугольника, а не по стороне формы.
    ///
    /// Ромб отличается от квадрата двумя вещами: решётка мельче в корень из двух и берётся
    /// каждый второй узел. Тогда ближайшие соседи стоят по диагонали ровно на том же
    /// промежутке, что и в сплошной раскладке, а мест выходит вдвое меньше.
    /// </summary>
    private static void Field(GameManager gm, UnitDefinition def, Vector2 origin, float facing,
        Vector2 drag, bool diamond, List<Obb> taken, List<BuildSpot> into)
    {
        float stepX = Step(def, facing, Vector2.Right);
        float stepY = Step(def, facing, Vector2.Down);

        if (diamond)
        {
            stepX *= Mathf.Sqrt2 * 0.5f;
            stepY *= Mathf.Sqrt2 * 0.5f;
        }

        int columns = Mathf.FloorToInt(Mathf.Abs(drag.X) / stepX);
        int rows = Mathf.FloorToInt(Mathf.Abs(drag.Y) / stepY);

        if (columns < 1 && rows < 1)
            return;

        // Промежуток растягивается до края области по каждой оси отдельно — по той же
        // причине, что и в цепочке: угол области должен приходиться на строение
        float spacingX = columns > 0 ? Mathf.Abs(drag.X) / columns : 0f;
        float spacingY = rows > 0 ? Mathf.Abs(drag.Y) / rows : 0f;

        float signX = Mathf.Sign(drag.X);
        float signY = Mathf.Sign(drag.Y);

        for (int row = 0; row <= rows; row++)
        {
            for (int column = 0; column <= columns; column++)
            {
                if (into.Count >= Const.PatternLimit)
                    return;

                if (row == 0 && column == 0)
                    continue;

                if (diamond && (row + column) % 2 != 0)
                    continue;

                var center = origin + new Vector2(
                    signX * spacingX * column,
                    signY * spacingY * row);

                Accept(gm, def, center, facing, taken, into);
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

        return span + Const.BuildMarginPx * Mathf.Max(def.PatternStep, 0f);
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
