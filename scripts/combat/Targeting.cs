using Godot;

/// <summary>
/// Поиск целей. Один вход для всех, кто ищет, в кого стрелять или на кого бежать.
///
/// Всё уязвимое состоит в одной группе, а сторона спрашивается у самой сущности —
/// поэтому искать «ближайшую постройку ИЛИ каркас ИЛИ юнита» отдельными обходами не нужно.
/// Добавили новый вид сущности — он попадает в поиск сам, стоит войти в группу.
/// </summary>
public static class Targeting
{
    public const string Group = "damageable";

    /// <summary>Ближайшая живая цель указанной стороны, не дальше maxDistance пикселей.</summary>
    public static IDamageable Nearest(SceneTree tree, Vector2 from, Faction side,
        float maxDistance = float.MaxValue)
    {
        IDamageable best = null;
        float bestDistance = maxDistance >= float.MaxValue
            ? float.MaxValue
            : maxDistance * maxDistance;

        foreach (var node in tree.GetNodesInGroup(Group))
        {
            if (node is not IDamageable candidate || !IsValid(node))
                continue;

            if (candidate.Faction != side)
                continue;

            float distance = from.DistanceSquaredTo(candidate.GlobalPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    /// <summary>Годится ли цель: жива, не помечена на удаление, прочность не кончилась.</summary>
    public static bool IsValid(GodotObject obj)
    {
        if (!Alive.Is(obj) || obj is not Node node || node.IsQueuedForDeletion())
            return false;

        return node is not IDamageable damageable || !damageable.Health.IsDead;
    }
}
