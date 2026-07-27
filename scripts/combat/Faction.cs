/// <summary>
/// Сторона конфликта. Пока их ровно две, и этого хватает: снаряд бьёт по чужим,
/// цель выбирается среди чужих. Кооп появится — сторона игрока останется одна на всех,
/// а личная принадлежность будет жить в Owner (playerId) сущности.
/// </summary>
public enum Faction
{
    Player,
    Hostile,
}

public static class Factions
{
    public static Faction Opposite(this Faction faction) =>
        faction == Faction.Player ? Faction.Hostile : Faction.Player;
}
