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
    public static WorkNode NearestBlueprint(SceneTree tree, Vector2 from,
        float maxDistance = float.MaxValue)
    {
        WorkNode best = null;
        float bestDistance = float.MaxValue;

        foreach (var node in tree.GetNodesInGroup("blueprint"))
        {
            if (node is not WorkNode work || !Alive.Is(work)
                || work.IsQueuedForDeletion() || !work.NeedsWork)
                continue;

            float distance = from.DistanceTo(work.GlobalPosition);
            if (distance > maxDistance || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = work;
        }

        return best;
    }

    /// <summary>
    /// Ближайшее повреждённое, что можно починить. Ремонтом занимаются только в пределах
    /// обзора: бегать через полкарты чинить забор — не то поведение, которого ждёшь от бота.
    ///
    /// Юниты в поиск попадают, только если строителю это разрешено переключателем:
    /// ремонтник поля боя и строитель базы — разные роли при одном инструменте.
    /// </summary>
    public static Node2D NearestDamaged(SceneTree tree, Vector2 from, float maxDistance,
        bool includeUnits = false)
    {
        Node2D best = null;
        float bestDistance = float.MaxValue;

        Scan(tree.GetNodesInGroup("building"));

        if (includeUnits)
            Scan(tree.GetNodesInGroup("unit"));

        return best;

        void Scan(Godot.Collections.Array<Node> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is not Node2D { } candidate || candidate is not IRepairable repairable
                    || !Targeting.IsValid(node))
                    continue;

                // Без цены курса ремонта взять неоткуда — чинить такое нечем
                if (repairable.Health == null || repairable.Health.Ratio >= 0.999f
                    || repairable.HealthPerMetal <= 0f)
                    continue;

                float distance = from.DistanceTo(candidate.GlobalPosition);
                if (distance > maxDistance || distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = candidate;
            }
        }
    }
}
