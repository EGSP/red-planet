using Godot;

/// <summary>
/// Правило постановки строений. Сетки больше нет: место занимает сам прямоугольник,
/// и единственное требование к нему — не пересекаться с чужими, соблюдая зазор.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ КЛАСС. Правило спрашивают трое, и все с разных сторон: призрак под
/// курсором — каждый кадр, постановка каркаса — по щелчку, раскладка точек метала —
/// при создании мира. Раньше их обслуживал WorldGrid.CanPlace; лишившись сетки,
/// правило должно было куда-то переехать, и складывать его в одного из потребителей
/// значило бы, что остальные двое ходят за правилом в чужой дом.
/// </summary>
public static class Placement
{
    /// <summary>Прямоугольник строения с центром в точке. Размер берётся из формы справочника.</summary>
    public static Rect2 Footprint(UnitDefinition def, Vector2 center)
    {
        var size = new Vector2(def.Size.X, def.Size.Y) * Const.Unit;
        return new Rect2(center - size * 0.5f, size);
    }

    /// <summary>
    /// Куда на самом деле встанет строение, если игрок целится сюда.
    ///
    /// Экстрактор притягивается к ближайшей точке метала: он ОБЯЗАН её накрыть, а попадать
    /// в неё мышью с точностью до пикселя — работа, которую игроку поручать незачем.
    /// Всё остальное ставится ровно туда, куда указано.
    /// </summary>
    public static Vector2 Snap(GameManager gm, UnitDefinition def, Vector2 cursor)
    {
        if (gm == null || def == null || !def.RequiresMetalSpot)
            return cursor;

        var spot = gm.Index.All<MetalSpot>()
            .Nearest(cursor, node => node.GlobalPosition, Const.Unit * 1.5f);

        return spot?.GlobalPosition ?? cursor;
    }

    /// <summary>
    /// Можно ли поставить. Проверок три, и последняя работает в обе стороны: экстрактор
    /// обязан накрыть точку метала, а всё остальное обязано её не накрывать.
    ///
    /// Второе требование существеннее, чем кажется. Точек на карте конечное число, они
    /// не восстанавливаются, и застроенная генератором точка потеряна до конца партии.
    /// Такую потерю игрок должен получить отказом при постановке, а не через полчаса,
    /// обнаружив, что ставить экстрактор некуда.
    /// </summary>
    public static bool CanPlace(GameManager gm, UnitDefinition def, Vector2 center)
    {
        if (gm == null || def == null)
            return false;

        var area = Footprint(def, center);

        if (!Const.WorldBounds.Encloses(area))
            return false;

        if (gm.Obstacles.Overlaps(area.Grow(Const.BuildMarginPx)))
            return false;

        return CoversMetal(gm, area) == def.RequiresMetalSpot;
    }

    /// <summary>Накрывает ли прямоугольник хоть одну точку метала.</summary>
    private static bool CoversMetal(GameManager gm, Rect2 area)
    {
        foreach (var spot in gm.Index.All<MetalSpot>())
            if (area.HasPoint(spot.GlobalPosition))
                return true;

        return false;
    }
}
