using Godot;

/// <summary>
/// Боты сами себе ставят задачи. Порядок предпочтений один и тот же:
/// копать (кто умеет) → строить → чинить в пределах обзора → идти за коммандером.
///
/// Сопровождение — не работа, а отсутствие работы: бот с таким приказом считается свободным
/// и бросит его, как только появится что-то полезное. Поэтому боты не толкутся на месте
/// и не убегают от базы, оставшись без дела.
///
/// Ремонт, наоборот, до конца: начатое лечение не прерывается ради появившегося каркаса.
/// Дёргать бота между целями хуже, чем чуть позже начать стройку.
/// </summary>
public partial class BotAiSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("bot"))
        {
            if (node is not Bot bot || bot.Def == null || !IsFree(bot))
                continue;

            var order = ChooseJob(bot);
            if (order != null)
                bot.SetOrders(order);
        }
    }

    /// <summary>Свободен тот, у кого нет приказа или кто просто идёт следом.</summary>
    private static bool IsFree(Bot bot) => bot.Idle || bot.Current.Kind == OrderKind.Follow;

    private Order ChooseJob(Bot bot)
    {
        var from = bot.GlobalPosition;
        var def = bot.Def;

        if (def.CanMine)
        {
            var ore = Nearest(from, "ore");
            if (ore != null)
                return Order.Work(OrderKind.Mine, ore);
        }

        if (def.CanBuild)
        {
            var blueprint = Jobs.NearestBlueprint(GetTree(), from);
            if (blueprint != null)
                return Order.Work(OrderKind.Build, blueprint);
        }

        if (def.CanRepair)
        {
            var damaged = Jobs.NearestDamaged(GetTree(), from, def.VisionRadiusPx,
                def.CanRepairUnits);

            if (damaged != null)
                return Order.Repair(damaged);
        }

        var commander = GM.Commander;

        // Уже идём следом — приказ переигрывать незачем, точка обновляется сама
        if (commander == null || bot.Current?.Kind == OrderKind.Follow)
            return null;

        return Order.Follow(commander);
    }

    private WorkNode Nearest(Vector2 from, string group)
    {
        WorkNode best = null;
        float bestDistance = float.MaxValue;

        foreach (var node in GetTree().GetNodesInGroup(group))
        {
            if (node is not WorkNode work || !Alive.Is(work)
                || work.IsQueuedForDeletion() || !work.NeedsWork)
                continue;

            float distance = from.DistanceSquaredTo(work.GlobalPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = work;
            }
        }

        return best;
    }
}
