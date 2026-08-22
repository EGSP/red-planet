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
    /// <summary>
    /// Ближайшая живая цель указанной стороны, не дальше maxDistance пикселей.
    ///
    /// <paramref name="accept"/> отсеивает негодных ПО ПРИЧИНАМ ВЫЗЫВАЮЩЕГО — например,
    /// недостижимых без ухода с рубежа. Отбор идёт при выборе, а не после него: цель,
    /// отвергнутая позже, оставила бы юнита вовсе без цели, хотя рядом есть подходящая.
    /// </summary>
    public static IDamageable Nearest(Vector2 from, Faction side,
        float maxDistance = float.MaxValue, System.Func<IDamageable, bool> accept = null) =>
        GameManager.I.Targets[side]
            .Where(target => !target.Health.IsDead && !Leaving(target))
            .Where(accept)
            .Nearest(from, target => target.GlobalPosition, maxDistance);

    /// <summary>
    /// Ближайшая живая цель указанной стороны, до ПОВЕРХНОСТИ которой инструмент дотягивается.
    ///
    /// Отличается от <see cref="Nearest"/> тем, как понимается предел: там он ограничивает
    /// расстояние между центрами, здесь — расстояние до края цели, то есть ровно та величина,
    /// которой меряется огневая граница. Разница существенна для крупных целей: у постройки
    /// шесть на шесть край отстоит от середины на три клетки, и предел по центрам отсекал бы
    /// её у стрелка, стоящего вплотную к стене.
    ///
    /// Предел по центрам остаётся у полей внимания и приказов: там речь идёт о месте на карте,
    /// а не о том, дотянется ли ствол.
    /// </summary>
    public static IDamageable NearestInReach(Vector2 from, Faction side, float reach) =>
        GameManager.I.Targets[side]
            .Where(target => !target.Health.IsDead && !Leaving(target)
                             && Reach.Within(from, target, reach))
            .Nearest(from, target => target.GlobalPosition);

    /// <summary>Юнит ещё внутри корпуса завода — ни цель, ни стрелок.</summary>
    public static bool Leaving(object obj) =>
        obj is IMobile { Movement.Leaving: true };

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
    /// ни при каких числах справочника. И сама дистанция, и запас берутся у
    /// <see cref="Reach"/> — теми же вызовами, какими пользуется рабочая рука, и с тем же
    /// значением <c>approach_hold</c>.
    ///
    /// ОБЗОР СЮДА БОЛЬШЕ НЕ ВХОДИТ. Прежде рабочая дальность обрезалась сверху радиусом
    /// обзора, поскольку огонь дальше видимого был запрещён. Теперь право на выстрел даёт
    /// разведка стороны (<see cref="Sight"/>), а не личный обзор стрелка, и обрезать
    /// сближение обзором значило бы, что дальнобойный, но близорукий юнит подъезжает
    /// к противнику вплотную, хотя достаёт до него издалека.
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

        return Reach.ApproachDistance(from, target, reach,
            Reach.Slack(reach, approachHoldFraction));
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
