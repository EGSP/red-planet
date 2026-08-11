using Godot;

/// <summary>
/// Огонь всех, у кого есть ствол: врагов и коммандера.
///
/// Порядок один на всех: остыл ли ствол → есть ли цель → в радиусе ли она →
/// довернуть ось прицеливания → цель в конусе → выстрел. Довернуть, но не выстрелить —
/// нормальный исход кадра: неповоротливый носитель из-за этого мажет по бегающей цели,
/// а вертлявый переводит огонь почти мгновенно. У подвижного ось — инструмент, а не корпус.
/// </summary>
public partial class WeaponSystem : GameSystem
{
    private readonly RandomNumberGenerator _rng = new();

    protected override void OnRegister() => _rng.Randomize();

    public override void Step(double dt)
    {
        // Снарядам нужен слой в мире: без площадки стрелять попросту некуда
        if (GM.Playground == null)
            return;

        foreach (var armed in GM.Index.All<IArmed>())
        {
            armed.Gun.Tick(dt);

            // Содержимое корпуса завода не стреляет и не должно получать огонь в ответ
            if (armed is IMobile { Movement.Leaving: true })
                continue;

            var weapon = armed.Weapon;
            if (weapon == null || !armed.CanFire)
                continue;

            var target = AcquireTarget(armed, weapon);
            if (target == null)
                continue;

            var from = armed.GlobalPosition;
            var to = target.GlobalPosition;

            if (!Targeting.InFiringRange(weapon, from, target))
                continue;

            armed.AimAt(to, dt);

            if (!Heading.InCone(armed.Facing, from, to, weapon.AimCone))
                continue;

            if (!armed.Gun.TryFire(weapon.FireInterval))
                continue;

            Fire(armed, weapon, from, to);
        }
    }

    /// <summary>
    /// Своя цель в приоритете: враг бьёт того, к кому шёл, а не первого встречного.
    /// Автовыбор и ответный огонь не выходят за обзор носителя: ствол длиннее зрения
    /// не даёт права стрелять в то, чего носитель не видит.
    /// </summary>
    private IDamageable AcquireTarget(IArmed armed, WeaponDefinition weapon)
    {
        float sight = SightRange(armed);

        var own = armed.FireTarget;
        if (own != null && Targeting.IsValid(own as GodotObject))
        {
            if (armed.GlobalPosition.DistanceTo(own.GlobalPosition) > sight)
                return null;

            return own;
        }

        float reach = Mathf.Min(weapon.RangePx, sight);
        if (reach <= 0f)
            return null;

        return Targeting.Nearest(armed.GlobalPosition, armed.Faction.Opposite(), reach);
    }

    /// <summary>
    /// Предел, дальше которого ствол сам цель не ищет. Нет обзора — нет автоогня;
    /// нет признака зрения — предел не режет дальность (на случай носителя без IVision).
    /// </summary>
    private static float SightRange(IArmed armed) =>
        armed is IVision vision ? vision.VisionRadius : float.MaxValue;

    private void Fire(IArmed armed, WeaponDefinition weapon, Vector2 from, Vector2 to)
    {
        // Целимся в цель, а разброс уводит ствол: конус решает, стрелять ли вообще,
        // а разброс — насколько кучно ложится очередь
        float angle = Heading.AngleTo(from, to)
                      + _rng.RandfRange(-weapon.Spread, weapon.Spread);

        GM.Spawn.SpawnProjectile(weapon, armed, from, angle);
    }
}
