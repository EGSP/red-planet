using System.Collections.Generic;
using Godot;

/// <summary>
/// Правило постановки строений. Сетки больше нет: место занимает сам прямоугольник,
/// и единственное требование к нему — не пересекаться с чужими, соблюдая зазор.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ КЛАСС. Правило спрашивают трое, и все с разных сторон: призрак под
/// курсором — каждый кадр, постановка плана — по щелчку, раскладка точек метала —
/// при создании мира. Раньше их обслуживал WorldGrid.CanPlace; лишившись сетки,
/// правило должно было куда-то переехать, и складывать его в одного из потребителей
/// значило бы, что остальные двое ходят за правилом в чужой дом.
///
/// ЗАНЯТОСТЬ ЗДЕСЬ ШИРЕ, ЧЕМ В КАРТЕ ПРЕПЯТСТВИЙ. Место держат не только твёрдые тела,
/// но и планы застройки (<see cref="BuildPlan"/>), которых в карте препятствий нет
/// и быть не может: ходьбе они не мешают. Поэтому правило спрашивает два источника.
///
/// ПОМЕТКА НА БУДУЩЕЕ — ВТОРАЯ КАРТА ПРЕПЯТСТВИЙ.
/// Причина: планы берутся линейным обходом разреза индекса, и обход этот идёт из
/// прилипания, то есть каждый кадр и по нескольку раз (см. <see cref="Cling"/>). Пока
/// планов десятки, это дешевле любой раскладки; на сотнях цена станет заметной.
/// Решение: завести второй экземпляр <see cref="ObstacleMap"/> под планы и спрашивать
/// его отсюда. Класть планы в основную карту нельзя ни при каких обстоятельствах —
/// по ней пересобирается растр навигации и выталкиваются юниты, а план неосязаем.
/// Есть и вторая причина завести карту: состав индекса меняется на границе кадра,
/// поэтому только что поставленный план виден остальным лишь со следующего кадра.
/// Сейчас это безобидно (партия планов сверяется со списком принятых мест внутри себя),
/// но при постановке планов из нескольких источников за кадр станет ошибкой.
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
            // Прилипать нужно и к планам: ряд размечают по краю уже размеченного,
            // и требовать от игрока попадания в полосу шириной в зазор нельзя
            if (!Occupant(gm, area, out var taken))
                break;

            if (!area.Escape(taken, out var push))
                break;

            // Выход считается до касания краями, а его хватает не всегда: то же округление,
            // из-за которого разводятся соседи в раскладке, отказывает и здесь
            moved += push + push.Normalized() * Const.PatternSlackPx;
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
        float facing = 0f, IReadOnlyList<Obb> taken = null, BuildPlan ignorePlan = null)
    {
        if (gm == null || def == null)
            return false;

        var area = Footprint(def, center, facing);

        if (!World.Bounds.Encloses(area.Bounds))
            return false;

        var claim = area.Grow(Const.BuildMarginPx);

        if (gm.Obstacles.Overlaps(claim))
            return false;

        // Размеченное место занято так же, как застроенное. Собственный план из проверки
        // исключается: он спрашивает правило ровно затем, чтобы поставить на своё место
        // каркас, и упереться в самого себя обязан не был
        if (PlanAt(gm, claim, ignorePlan) != null)
            return false;

        if (taken != null)
            foreach (var other in taken)
                if (other.Intersects(claim))
                    return false;

        return CoversMetal(gm, area) == def.RequiresMetalSpot;
    }

    /// <summary>
    /// Чем занято место — телом или планом. Прилипанию безразлично, чем именно:
    /// ему нужна форма, из которой предстоит выйти.
    /// </summary>
    private static bool Occupant(GameManager gm, in Obb area, out Obb shape)
    {
        if (gm.Obstacles.Blocker(area) is { } blocker)
        {
            shape = gm.Obstacles.ShapeOf(blocker);
            return true;
        }

        if (PlanAt(gm, area, null) is { } plan)
        {
            shape = plan.Footprint;
            return true;
        }

        shape = new Obb();
        return false;
    }

    /// <summary>План, чьё место пересекается с областью. Израсходованные не считаются.</summary>
    private static BuildPlan PlanAt(GameManager gm, in Obb area, BuildPlan except)
    {
        foreach (var plan in gm.Index.All<BuildPlan>())
        {
            if (plan == except || !plan.NeedsWork)
                continue;

            if (plan.Footprint.Intersects(area))
                return plan;
        }

        return null;
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
