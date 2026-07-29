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
    /// <summary>Ближайший каркас, которому ещё нужна работа.</summary>
    public static WorkNode NearestBlueprint(Vector2 from, float maxDistance = float.MaxValue) =>
        GameManager.I.Index.All<Blueprint>()
            .Where(blueprint => blueprint.NeedsWork)
            .Nearest(from, blueprint => blueprint.GlobalPosition, maxDistance);

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
