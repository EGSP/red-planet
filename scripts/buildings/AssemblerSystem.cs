using Godot;

/// <summary>
/// Раздаёт работу башням-сборщикам. Отдельная система, а не _Process самой башни:
/// выбор работы обязан произойти до прохода экономики, иначе башня заявит спрос
/// на цель, которую только что потеряла.
/// </summary>
public partial class AssemblerSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("assembler"))
            if (node is Assembler assembler && Alive.Is(assembler)
                && !assembler.IsQueuedForDeletion())
                assembler.Think();
    }
}
