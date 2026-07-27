/// <summary>
/// Счёт боя: сколько врагов пришло, сколько из них полегло, сколько своего потеряно.
///
/// Считается дельтой по документам о появлении и гибели — обхода живых сущностей нет.
/// Именно на таком обходе обжёгся прошлый прототип: там регистр пересчитывал всех живых
/// врагов заново на каждый спавн и каждую смерть.
/// </summary>
public sealed class CombatProjection : Projection
{
    public int EnemiesSpawned { get; private set; }

    public int EnemiesDestroyed { get; private set; }

    /// <summary>Свои потери: постройки, каркасы и юниты. Коммандер не умирает и сюда не попадает.</summary>
    public int LossesTaken { get; private set; }

    public int EnemiesAlive => EnemiesSpawned - EnemiesDestroyed;

    public override void Subscribe(EventStore events)
    {
        events.Stream<EnemySpawned>().Appended += _ =>
        {
            EnemiesSpawned++;
            NotifyChanged();
        };

        events.Stream<EntityDestroyed>().Appended += record =>
        {
            if (record.Side == Faction.Hostile)
                EnemiesDestroyed++;
            else
                LossesTaken++;

            NotifyChanged();
        };
    }
}
