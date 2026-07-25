using Godot;

/// <summary>
/// Гоняет узлы работы после того, как юниты подключились.
/// Каркасы тянут ресурсы, месторождения отдают — каждый одним запросом за тик.
/// </summary>
public partial class WorkSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("work"))
            if (node is WorkNode work && Alive.Is(work) && !work.IsQueuedForDeletion())
                work.Work(dt);
    }
}
