using Godot;

/// <summary>
/// Огонь всех, у кого есть ствол: врагов и коммандера.
///
/// Порядок один на всех: остыл ли ствол → есть ли цель → в радиусе ли она →
/// довернуть корпус → цель в конусе прицеливания → выстрел. Довернуть, но не выстрелить —
/// нормальный исход кадра: неповоротливый толстяк из-за этого мажет по бегающему коммандеру,
/// а вертлявый стрелок переводит огонь почти мгновенно.
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

    /// <summary>Своя цель в приоритете: враг бьёт того, к кому шёл, а не первого встречного.</summary>
    private IDamageable AcquireTarget(IArmed armed, WeaponDefinition weapon)
    {
        var own = armed.FireTarget;
        if (own != null && Targeting.IsValid(own as GodotObject))
            return own;

        return Targeting.Nearest(armed.GlobalPosition, armed.Faction.Opposite(), weapon.RangePx);
    }

    private void Fire(IArmed armed, WeaponDefinition weapon, Vector2 from, Vector2 to)
    {
        // Целимся в цель, а разброс уводит ствол: конус решает, стрелять ли вообще,
        // а разброс — насколько кучно ложится очередь
        float angle = Heading.AngleTo(from, to)
                      + _rng.RandfRange(-weapon.Spread, weapon.Spread);

        GM.Spawn.SpawnProjectile(weapon, armed, from, angle);
    }
}
