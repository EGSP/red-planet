using Godot;

/// <summary>
/// Мозг союзных ботов: сами себе ставят задачи приказами. Порядок предпочтений один и тот же:
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
        foreach (var bot in GM.Index.All<Bot>())
        {
            if (bot.Definition == null || !IsFree(bot))
                continue;

            var order = ChooseJob(bot);

            // Недопустимый боту вид приказа очередь не примет — второй сети не нужно
            if (order != null)
                bot.Orders.TrySet(order);
        }
    }

    /// <summary>Свободен тот, у кого нет приказа или кто просто идёт следом.</summary>
    private static bool IsFree(Bot bot) => bot.Idle || bot.Current.Kind == OrderKind.Follow;

    private Order ChooseJob(Bot bot)
    {
        var from = bot.GlobalPosition;
        var def = bot.Definition;

        if (def.CanMine)
        {
            var ore = GM.Index.All<OreDeposit>()
                .Where(deposit => deposit.NeedsWork)
                .Nearest(from, deposit => deposit.GlobalPosition);

            if (ore != null)
                return Order.Work(OrderKind.Mine, ore);
        }

        if (def.CanBuild)
        {
            var blueprint = Jobs.NearestBlueprint(from);
            if (blueprint != null)
                return Order.Work(OrderKind.Build, blueprint);
        }

        if (def.CanRepair)
        {
            var damaged = Jobs.NearestDamaged(from, def.VisionRadiusPx, def.CanRepairUnits);

            if (damaged != null)
                return Order.Repair(damaged);
        }

        var commander = GM.Commander;

        // Уже идём следом — приказ переигрывать незачем, точка обновляется сама
        if (commander == null || bot.Current?.Kind == OrderKind.Follow)
            return null;

        return Order.Follow(commander);
    }
}
