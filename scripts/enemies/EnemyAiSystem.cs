using Godot;

/// <summary>
/// Мозг врагов: назначает каждому цель приказом атаки. Ход по этому приказу делает сам враг,
/// а зовёт его OrderSystem — здесь только решение «кого бить».
///
/// Мозг у врага отдельный от союзного (BotAiSystem) не потому, что он устроен иначе,
/// а потому, что политика другая: бот ищет работу, враг ищет цель. Механика движения,
/// очередь приказов и правила отсева у них общие.
///
/// Цель переигрывается по таймеру, а не только по гибели прежней: рядом могло вырасти
/// что-то поближе, и упрямо идти через полкарты к дальнему складу враг не должен.
/// </summary>
public partial class EnemyAiSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var enemy in GM.Index.All<Enemy>())
        {
            enemy.TickRetarget(dt);

            if (!enemy.NeedsTarget)
                continue;

            if (Targeting.Nearest(enemy.GlobalPosition, Faction.Player) is not Node2D victim)
                continue;

            if (enemy.Orders.TrySet(Order.Attack(victim)))
                enemy.NoteTargeted();
        }
    }
}
