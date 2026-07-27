using Godot;

/// <summary>
/// Двигает снаряды и даёт им проверить попадание. Отдельной системой, а не в _Process
/// самих снарядов: полёт — часть симуляции, и он обязан идти в том же такте и в том же
/// порядке, что стрельба и урон.
/// </summary>
public partial class ProjectileSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var node in GetTree().GetNodesInGroup("projectile"))
            if (node is Projectile projectile && Alive.Is(projectile)
                && !projectile.IsQueuedForDeletion())
                projectile.Step(dt);
    }
}
