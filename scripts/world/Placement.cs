using System.Collections.Generic;
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
    /// <summary>
    /// Прямоугольник строения с центром в точке. Размер берётся из формы справочника,
    /// поворот задаёт игрок при постановке.
    /// </summary>
    public static Obb Footprint(UnitDefinition def, Vector2 center, float facing = 0f)
    {
        var size = new Vector2(def.Size.X, def.Size.Y) * Const.Unit;
        return new Obb(center, size, facing);
    }

    /// <summary>
    /// Куда на самом деле встанет строение, если игрок целится сюда.
    ///
    /// Поправок две. Экстрактор притягивается к ближайшей точке метала: он ОБЯЗАН её накрыть,
    /// а попадать в неё мышью с точностью до пикселя — работа, которую игроку поручать незачем.
    /// Всё остальное отодвигается от соседа, в которого упёрлось, — см. <see cref="Cling"/>.
    /// </summary>
    public static Vector2 Snap(GameManager gm, UnitDefinition def, Vector2 cursor, float facing = 0f)
    {
        if (gm == null || def == null)
            return cursor;

        if (def.RequiresMetalSpot)
        {
            var spot = gm.Index.All<MetalSpot>()
                .Nearest(cursor, node => node.GlobalPosition, Const.Unit * 1.5f);

            return spot?.GlobalPosition ?? cursor;
        }

        return Cling(gm, def, cursor, facing);
    }

    /// <summary>
    /// Прилипание к соседям: место, наехавшее на чужое, отодвигается наружу по кратчайшему
    /// пути, пока не встанет вплотную с обязательным зазором.
    ///
    /// ЗАЧЕМ. Ряд строений выкладывается по краю уже построенного, и требовать от игрока
    /// попадания в полосу шириной в зазор — требовать точности, которой мышь не даёт.
    /// Прилипание избавляет от подведения курсора и заодно делает ряды ровными.
    ///
    /// ПОРОГ ОБЯЗАТЕЛЕН. Сдвиг дальше <see cref="Const.ClingLimitPx"/> означает, что курсор
    /// стоит глубоко внутри чужого места, и любое подходящее место оттуда далеко. Считать,
    /// что игрок метил к его краю, там уже нельзя: он либо промахнулся, либо целится
    /// в третье место, и подменять его выбор на далёкий край — хуже, чем показать отказ.
    /// </summary>
    private static Vector2 Cling(GameManager gm, UnitDefinition def, Vector2 cursor, float facing)
    {
        var area = Footprint(def, cursor, facing).Grow(Const.BuildMarginPx);
        var moved = cursor;

        // Соседей у одного места бывает несколько, и выход из первого заводит во второго.
        // Обходим их по очереди; предел на число шагов нужен от хождения по кругу
        // между двумя соседями, которое возможно в тесном промежутке
        for (int step = 0; step < ClingSteps; step++)
        {
            var blocker = gm.Obstacles.Blocker(area);

            if (blocker == null)
                break;

            if (!area.Escape(gm.Obstacles.ShapeOf(blocker), out var push))
                break;

            moved += push;
            area = area.MovedTo(moved);

            if (cursor.DistanceTo(moved) > Const.ClingLimitPx)
                return cursor;
        }

        return moved;
    }

    /// <summary>Сколько соседей подряд разбирает прилипание, прежде чем сдаться.</summary>
    private const int ClingSteps = 4;

    /// <summary>
    /// Можно ли поставить. Проверок три, и последняя работает в обе стороны: экстрактор
    /// обязан накрыть точку метала, а всё остальное обязано её не накрывать.
    ///
    /// Второе требование существеннее, чем кажется. Точек на карте конечное число, они
    /// не восстанавливаются, и застроенная генератором точка потеряна до конца партии.
    /// Такую потерю игрок должен получить отказом при постановке, а не через полчаса,
    /// обнаружив, что ставить экстрактор некуда.
    ///
    /// РАНЕЕ ПРИНЯТЫЕ МЕСТА проверяются наравне с построенным. Паттерн застройки готовит
    /// целую партию каркасов, которых в мире ещё нет: сверяться с одной картой препятствий
    /// означало бы разрешить двум местам одной партии встать друг на друга.
    /// </summary>
    public static bool CanPlace(GameManager gm, UnitDefinition def, Vector2 center,
        float facing = 0f, IReadOnlyList<Obb> taken = null)
    {
        if (gm == null || def == null)
            return false;

        var area = Footprint(def, center, facing);

        if (!Const.WorldBounds.Encloses(area.Bounds))
            return false;

        var claim = area.Grow(Const.BuildMarginPx);

        if (gm.Obstacles.Overlaps(claim))
            return false;

        if (taken != null)
            foreach (var other in taken)
                if (other.Intersects(claim))
                    return false;

        return CoversMetal(gm, area) == def.RequiresMetalSpot;
    }

    /// <summary>Накрывает ли прямоугольник хоть одну точку метала.</summary>
    private static bool CoversMetal(GameManager gm, in Obb area)
    {
        foreach (var spot in gm.Index.All<MetalSpot>())
            if (area.HasPoint(spot.GlobalPosition))
                return true;

        return false;
    }
}
