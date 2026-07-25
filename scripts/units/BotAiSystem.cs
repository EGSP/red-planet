using Godot;

/// <summary>
/// Боты сами себе ставят задачи: копатель ищет ближайшее месторождение,
/// фабрикатор — ближайший недостроенный каркас.
/// Работы нет — бот остаётся на месте, а не бегает впустую.
/// </summary>
public partial class BotAiSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("bot"))
        {
            if (node is not Bot bot || bot.Def == null || !bot.Idle)
                continue;

            WorkNode target = null;
            var kind = OrderKind.Mine;

            if (bot.Def.CanMine)
                target = Nearest(bot.GlobalPosition, "ore");

            if (target == null && bot.Def.CanBuild)
            {
                target = Nearest(bot.GlobalPosition, "blueprint");
                kind = OrderKind.Build;
            }

            if (target != null)
                bot.SetOrders(Order.Work(kind, target));
        }
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
