using Godot;

/// <summary>
/// Экономика за кадр: собрать заявки, свести баланс, дать всем отработать.
///
/// Заменила собой WorkSystem и FactorySystem: раньше каждый потребитель сам решал,
/// хватает ли ему ресурсов, и делал это по-своему. В потоковой экономике так нельзя —
/// производительность общая, и считать её должен кто-то один.
///
/// Три прохода по одной и той же группе:
///   1. Declare — кто сколько производит и просит;
///   2. Resolve — производительность базы, одна на всех;
///   3. Run     — работа с этой производительностью и публикация документов о ресурсах.
/// </summary>
public partial class EconomySystem : GameSystem
{
    public const string Group = "economy";

    public override void Step(double dt)
    {
        var ledger = GM.Economy;
        var actors = GetTree().GetNodesInGroup(Group);

        ledger.BeginFrame();

        foreach (var node in actors)
            if (node is IEconomyActor actor && Alive.Is(node) && !node.IsQueuedForDeletion())
                actor.Declare(ledger);

        ledger.Resolve(dt, GM.Stockpile);

        var rates = ledger.Rates;

        foreach (var node in actors)
            if (node is IEconomyActor actor && Alive.Is(node) && !node.IsQueuedForDeletion())
                actor.Run(dt, rates);
    }
}
