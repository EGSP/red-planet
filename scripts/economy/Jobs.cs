using Godot;

/// <summary>
/// Поиск работы для тех, кто умеет работать инструментом: ботов и башен-сборщиков.
///
/// Вынесено сюда, потому что искать одинаково должны и подвижный фабрикатор, и неподвижная
/// башня. Отличаются они только тем, что первый к работе идёт, а вторая ждёт, пока работа
/// сама окажется в радиусе.
///
/// Порядок задач один на всех и задан здесь же: сначала стройка, потом ремонт.
/// Недостроенное важнее повреждённого — оно ещё не приносит пользы вовсе.
/// </summary>
public static class Jobs
{
    /// <summary>
    /// Ближайшее место работы, которому ещё нужны руки: план, который некому поставить,
    /// или каркас, который некому достроить.
    ///
    /// ПЛАН И КАРКАС ИЩУТСЯ ВМЕСТЕ, потому что для того, кто ищет себе занятие, разницы
    /// между ними нет: и то и другое — недостроенная постройка, к которой нужно подойти.
    /// Разница вскрывается только на месте — план ставят, каркас варят.
    /// </summary>
    public static IWorkSite NearestSite(Vector2 from, float maxDistance = float.MaxValue)
    {
        var index = GameManager.I.Index;

        var plan = index.All<BuildPlan>()
            .Where(site => site.NeedsWork)
            .Nearest(from, site => site.GlobalPosition, maxDistance);

        var frame = index.All<Blueprint>()
            .Where(site => site.NeedsWork)
            .Nearest(from, site => site.GlobalPosition, maxDistance);

        if (plan == null)
            return frame;

        if (frame == null)
            return plan;

        // Начатое ближе к завершению, но правило выбора здесь одно — расстояние:
        // предпочтение каркасу означало бы, что размеченное далеко от базы не ставится
        // никогда, пока в тылу есть хоть одна недоваренная стена
        return from.DistanceSquaredTo(plan.GlobalPosition)
               <= from.DistanceSquaredTo(frame.GlobalPosition)
            ? plan
            : frame;
    }

    /// <summary>
    /// Ближайшее повреждённое, что можно починить. Ремонтом занимаются только в пределах
    /// обзора: бегать через полкарты чинить забор — не то поведение, которого ждёшь от бота.
    ///
    /// Ремонтник поля боя и строитель базы — разные роли при одном инструменте, поэтому
    /// юниты в поиск попадают не всегда. Но решает это ВЫБОР РАЗРЕЗА, а не фильтр:
    /// «юнит ли ты» — вопрос о типе, он не меняется ни разу за всю жизнь сущности,
    /// и проверять его на каждом элементе каждого обхода незачем. Признак «чинится»
    /// реализуют ровно постройки и юниты, поэтому широкий случай — весь IRepairable.
    /// </summary>
    public static Node2D NearestDamaged(Vector2 from, float maxDistance,
        bool includeUnits = false)
    {
        var index = GameManager.I.Index;

        return includeUnits
            ? Damaged(index.All<IRepairable>(), from, maxDistance)
            : Damaged(index.All<Building>(), from, maxDistance);
    }

    private static Node2D Damaged<T>(Slice<T> slice, Vector2 from, float maxDistance)
        where T : class, IRepairable =>
        slice.Where(NeedsRepair).Nearest(from, target => target.GlobalPosition, maxDistance)
            as Node2D;

    /// <summary>Есть ли что чинить и есть ли чем: без цены постройки курса ремонта нет.</summary>
    private static bool NeedsRepair(IRepairable target) =>
        target.Health != null && target.Health.Ratio < 0.999f && target.HealthPerMetal > 0f;
}
