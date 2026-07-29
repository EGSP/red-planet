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
        foreach (var assembler in GM.Index.All<Assembler>())
            assembler.Think();
    }
}
