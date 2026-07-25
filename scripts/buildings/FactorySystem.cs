using Godot;

/// <summary>Заводы тянут руду из общего хранилища и кладут туда метал.</summary>
public partial class FactorySystem : GameSystem
{
    public override void Step(double dt)
    {
        var stockpile = GM.Stockpile;
        var events = GM.Events;

        foreach (var node in GetTree().GetNodesInGroup("factory"))
        {
            if (node is not Factory factory)
                continue;

            float want = factory.OrePerSecond * (float)dt;
            float use = Mathf.Min(want, stockpile.Get(ResourceKind.Ore));

            factory.Working = use > 0.0001f;
            if (!factory.Working)
                continue;

            events.Append(new ResourceSpent { Kind = ResourceKind.Ore, Amount = use });
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Metal,
                Amount = use * factory.MetalPerOre,
            });
        }
    }
}
