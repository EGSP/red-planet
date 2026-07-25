using Godot;

/// <summary>Двигает юнитов и подключает их к узлам работы.</summary>
public partial class UnitSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("unit"))
            if (node is Unit unit)
                unit.Step(dt);
    }
}
