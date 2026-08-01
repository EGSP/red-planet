using Godot;

/// <summary>
/// Поиск целей. Один вход для всех, кто ищет, в кого стрелять или на кого бежать.
///
/// Всё уязвимое лежит в одном разрезе индекса — по признаку IDamageable, — и разложено
/// по сторонам. Поэтому искать «ближайшую постройку ИЛИ каркас ИЛИ юнита» отдельными
/// обходами не нужно, а чужих не приходится отсеивать поштучно: разрез по стороне сразу
/// отдаёт только их. Новый вид сущности попадает в поиск сам, стоит ему реализовать
/// интерфейс, — списывать его куда-либо руками не нужно.
/// </summary>
public static class Targeting
{
    /// <summary>Ближайшая живая цель указанной стороны, не дальше maxDistance пикселей.</summary>
    public static IDamageable Nearest(Vector2 from, Faction side,
        float maxDistance = float.MaxValue) =>
        GameManager.I.Targets[side]
            .Where(target => !target.Health.IsDead)
            .Nearest(from, target => target.GlobalPosition, maxDistance);

    /// <summary>
    /// Достаёт ли ствол до цели. Радиус меряем до края цели, а не до её центра:
    /// по стене завода стреляют с угла, иначе крупные постройки было бы не достать.
    ///
    /// Одна формула на всех: по ней и система стрельбы решает, пора ли жать на спуск,
    /// и сущность — пора ли ещё подходить.
    /// </summary>
    public static bool InFiringRange(WeaponDefinition weapon, Vector2 from, IDamageable target)
    {
        if (weapon == null || target == null)
            return false;

        return from.DistanceTo(target.GlobalPosition) <= weapon.RangePx + target.HitRadius;
    }

    /// <summary>Годится ли цель: жива, не помечена на удаление, прочность не кончилась.</summary>
    public static bool IsValid(GodotObject obj)
    {
        if (!Alive.Is(obj) || obj is not Node node || node.IsQueuedForDeletion())
            return false;

        return node is not IDamageable damageable || !damageable.Health.IsDead;
    }
}
