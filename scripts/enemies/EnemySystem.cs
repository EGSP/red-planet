using Godot;

/// <summary>
/// Ведёт врагов: назначает каждому ближайшую цель игрока и даёт сделать ход.
///
/// Устройство то же, что у ботов игрока (BotAiSystem + UnitSystem), только здесь обе
/// половины в одной системе: у врагов один вид поведения, и разносить его по двум нодам
/// значило бы плодить сущности ради симметрии.
/// </summary>
public partial class EnemySystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("enemy"))
        {
            if (node is not Enemy enemy || !Targeting.IsValid(enemy))
                continue;

            if (enemy.NeedsTarget)
                enemy.SetTarget(Targeting.Nearest(GetTree(), enemy.GlobalPosition, Faction.Player));

            enemy.Step(dt);
        }
    }
}
