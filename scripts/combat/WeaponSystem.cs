using Godot;

/// <summary>
/// Огонь всех, у кого есть ствол: врагов, турелей и коммандера.
///
/// Порядок один на всех: остыли ли стволы → есть ли цель → в радиусе ли она →
/// довернуть стволы → у каждого проверить сектор стрельбы → выстрел. Довернуть,
/// но не выстрелить — нормальный исход кадра: неповоротливый носитель из-за этого мажет
/// по бегающей цели, а вертлявый переводит огонь почти мгновенно.
///
/// СТВОЛОВ МОЖЕТ БЫТЬ НЕСКОЛЬКО, И РЕШЕНИЯ У НИХ РАЗНЫЕ. Цель выбирается на носителя одна:
/// разводить огонь по разным жертвам значило бы заводить у сущности несколько намерений
/// сразу, а приказ у неё один. Дальше каждый ствол решает сам — доворот у него свой,
/// перезарядка своя, сектор свой, — и потому бортовое орудие молчит, пока цель на другом
/// борту, а башенное стреляет.
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
            armed.Aim.TickGuns(dt);

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

            // Сближение и выбор цели меряются главным стволом: у сущности одна дистанция
            // боя, и выводить её из самого дальнобойного ствола значило бы, что носитель
            // встаёт там, откуда достаёт лишь половина его снаряжения
            if (!Targeting.InFiringRange(weapon, from, target))
                continue;

            armed.AimAt(to, dt);

            foreach (var mount in armed.Aim.Mounts)
                TryShoot(armed, mount, from, target);
        }
    }

    /// <summary>
    /// Выстрел одного ствола. Три условия, и каждое своё: ствол должен быть стволом,
    /// цель — лежать в его собственной дальности, а сам он — быть довёрнут в пределах
    /// сектора стрельбы от своей нынешней оси.
    ///
    /// Сектор считается от оси ИМЕННО ЭТОГО ствола, а не от корпуса и не от главного ствола:
    /// иначе бортовое орудие, упёршееся в край своего сектора наведения, стреляло бы вслед
    /// за башней в сторону, куда оно не смотрит.
    ///
    /// ДАЛЬНОСТЬ ЗДЕСЬ СЧИТАЕТСЯ ТОЙ ЖЕ ФОРМУЛОЙ, ЧТО И ВЫШЕ. Проверка ведётся через
    /// <see cref="Targeting.InFiringRange"/>, то есть до КРАЯ цели, а не до её середины.
    /// Расстояние между центрами здесь не годится: подход к цели рассчитан от края
    /// (<see cref="Targeting.ApproachDistance"/>), и потому носитель, вставший вплотную
    /// к стене завода, отстоит от центра постройки дальше, чем бьёт его ствол. Со второй
    /// формулой такой носитель молчал бы у самой стены, и тем сильнее, чем крупнее цель.
    /// </summary>
    private void TryShoot(IArmed armed, ToolMount mount, Vector2 from, IDamageable target)
    {
        var weapon = mount.Weapon;

        if (!Targeting.InFiringRange(weapon, from, target))
            return;

        var to = target.GlobalPosition;

        if (!Heading.InCone(mount.World(armed.BodyFacing), from, to, weapon.FireArc))
            return;

        if (!mount.Gun.TryFire(weapon.FireInterval))
            return;

        Fire(armed, mount, to);
    }

    /// <summary>
    /// Своя цель в приоритете: враг бьёт того, к кому шёл, а не первого встречного.
    ///
    /// ДВА ПРЕДЕЛА, И РОЛИ У НИХ РАЗНЫЕ. Докуда достаёт выстрел, решает дальность ствола;
    /// известно ли вообще, где противник, решает разведка стороны (<see cref="Sight"/>).
    /// Прежде оба вопроса решал личный обзор стрелка, и меньшее из двух чисел справочника
    /// молча отменяло большее: зоркий с коротким стволом вёл себя ровно как близорукий
    /// с длинным. Теперь зоркий не стреляет дальше своего ствола, а слепой бьёт по цели,
    /// разведанной союзником.
    ///
    /// Предел автовыбора меряется до края цели, как и сама огневая граница. Расстояние
    /// между центрами отсекало бы крупные постройки: подойдя к стене завода вплотную,
    /// носитель оставался бы от его середины дальше, чем бьёт ствол, и цели «не находил» —
    /// притом что стрелять по ней разрешено.
    /// </summary>
    private IDamageable AcquireTarget(IArmed armed, WeaponDefinition weapon)
    {
        var own = armed.FireTarget;
        if (own != null && Targeting.IsValid(own as GodotObject))
            return Spotted(armed, own) ? own : null;

        float reach = weapon.RangePx;
        if (reach <= 0f)
            return null;

        // Разведка спрашивается у одной цели, уже выбранной, а не у каждой из перебираемых:
        // обход источников обзора на каждого кандидата стоил бы произведения численностей,
        // тогда как правило «бьём ближайшего, если он разведан» даёт тот же исход всюду,
        // кроме случая, когда ближайший скрыт, а дальний виден. Такой случай разрешается
        // сам собой на следующей переигровке выбора
        var target = Targeting.NearestInReach(armed.GlobalPosition, armed.Faction.Opposite(), reach);

        return target != null && Spotted(armed, target) ? target : null;
    }

    /// <summary>Разведана ли цель стороной стрелка. Свой обзор проверяется первым.</summary>
    private static bool Spotted(IArmed armed, IDamageable target) =>
        Sight.Spots(armed.Faction, target.GlobalPosition, armed as IVision);

    private void Fire(IArmed armed, ToolMount mount, Vector2 to)
    {
        var weapon = mount.Weapon;

        // Снаряд рождается на срезе ЭТОГО ствола, если носитель снабжён сценой изображения.
        // Решение стрелять принято выше и от центра носителя, поэтому вынос точки вылета
        // ни дальности, ни сектора не меняет — см. IArmed.MuzzlePosition
        var muzzle = mount.Muzzle(armed.GlobalPosition);

        // Целимся в цель, а разброс уводит ствол: конус решает, стрелять ли вообще,
        // а разброс — насколько кучно ложится очередь. Угол считается от среза ствола,
        // а не от центра: иначе вынесенный ствол давал бы очередь, идущую мимо цели
        // параллельно линии прицеливания
        float angle = Heading.AngleTo(muzzle, to)
                      + _rng.RandfRange(-weapon.Spread, weapon.Spread);

        GM.Spawn.SpawnProjectile(weapon, armed, muzzle, angle);
    }
}
