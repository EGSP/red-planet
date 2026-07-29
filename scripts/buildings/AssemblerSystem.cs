using Godot;

/// <summary>
/// Мозг башен-сборщиков: раздаёт им приказы. Отдельная система, а не _Process самой башни —
/// выбор работы обязан произойти до исполнения и до прохода экономики, иначе башня заявит
/// спрос на цель, которую только что потеряла.
///
/// Ставит приказ только свободной башне: приказ игрока важнее самодеятельности и не должен
/// перебиваться каждым кадром. Отработанный приказ снимет с очереди OrderSystem, и башня
/// снова окажется свободной.
/// </summary>
public partial class AssemblerSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var assembler in GM.Index.All<Assembler>())
        {
            if (!assembler.Orders.Idle)
                continue;

            var order = assembler.ChooseJob();
            if (order != null)
                assembler.Orders.TrySet(order);
        }
    }
}
