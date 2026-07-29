using Godot;

/// <summary>Двигает юнитов и подключает их к узлам работы.</summary>
public partial class UnitSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var unit in GM.Index.All<Unit>())
            unit.Step(dt);
    }
}
