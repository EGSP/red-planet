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
            .Where(target => !target.Health.IsDead && !Leaving(target))
            .Nearest(from, target => target.GlobalPosition, maxDistance);

    /// <summary>Юнит ещё внутри корпуса завода — ни цель, ни стрелок.</summary>
    public static bool Leaving(object obj) =>
        obj is IMobile { Movement.Leaving: true };

    /// <summary>
    /// Наименьший запас между дистанцией подхода и границей огня, пикселей.
    ///
    /// Существует затем, чтобы доля подхода из справочника не могла сделать запас нулевым:
    /// у ствола малой дальности четверть от дальности — это несколько пикселей, и юнит
    /// вставал бы фактически на самой границе.
    /// </summary>
    private static readonly float ApproachMargin = Const.Unit * 0.5f;

    /// <summary>
    /// Дальность, с которой ствол достаёт до цели, отсчитанная ОТ ЦЕНТРА цели. Меряется
    /// до края цели, а не до середины: по стене завода стреляют с угла, иначе крупные
    /// постройки было бы не достать.
    ///
    /// Поправка на габарит берётся у <see cref="Reach"/> и потому зависит от того, с какой
    /// стороны стреляют: у постройки два на четыре край вдоль корпуса и край поперёк отстоят
    /// от центра на разное. Прежде здесь стоял радиус описанной окружности, одинаковый во все
    /// стороны, и поперёк длинного корпуса он разрешал огонь из точки, откуда до стены ещё
    /// далеко.
    ///
    /// Единственная формула огневой границы во всём проекте. Всё, что связано с подходом
    /// к бою, обязано выводиться отсюда вычитанием, а не считаться отдельно: две формулы
    /// неизбежно разойдутся, и юнит остановится там, где стрелять ещё нельзя.
    /// </summary>
    public static float FiringDistance(WeaponDefinition weapon, Vector2 from, IDamageable target) =>
        weapon == null ? 0f : Reach.StopDistance(from, target, weapon.RangePx);

    /// <summary>
    /// На какую дистанцию подходить, чтобы вести огонь наверняка.
    ///
    /// Считается вычитанием запаса из огневой границы, поэтому по построению лежит внутри
    /// неё: остановиться там, где ствол не достаёт, при таком определении невозможно
    /// ни при каких числах справочника. Запас берётся как доля дальности, но не меньше
    /// <see cref="ApproachMargin"/>.
    ///
    /// Зачем запас вообще нужен: цель движется, юнита толкают соседи, а выталкивание
    /// из построек сдвигает его на радиус корпуса. Остановка ровно на границе означала бы,
    /// что огонь прекращается от любого из этих смещений.
    /// </summary>
    public static float ApproachDistance(WeaponDefinition weapon, Vector2 from,
        IDamageable target, float approachHoldFraction)
    {
        if (weapon == null)
            return 0f;

        float reach = weapon.RangePx;
        float slack = Mathf.Max(
            reach * (1f - Mathf.Clamp(approachHoldFraction, 0f, 1f)), ApproachMargin);

        return Reach.StopDistance(from, target, Mathf.Max(reach - slack, 0f));
    }

    /// <summary>
    /// Достаёт ли ствол до цели. По этой проверке система стрельбы решает, пора ли жать
    /// на спуск.
    /// </summary>
    public static bool InFiringRange(WeaponDefinition weapon, Vector2 from, IDamageable target)
    {
        if (weapon == null || target == null)
            return false;

        return from.DistanceTo(target.GlobalPosition) <= FiringDistance(weapon, from, target);
    }

    /// <summary>Годится ли цель: жива, не помечена на удаление, прочность не кончилась.</summary>
    public static bool IsValid(GodotObject obj)
    {
        if (!Alive.Is(obj) || obj is not Node node || node.IsQueuedForDeletion())
            return false;

        if (Leaving(obj))
            return false;

        return node is not IDamageable damageable || !damageable.Health.IsDead;
    }
}
