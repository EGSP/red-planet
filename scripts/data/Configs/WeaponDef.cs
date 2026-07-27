using Godot;

/// <summary>
/// Справочник оружия. Отдельным ресурсом от носителя, потому что ствол — не свойство
/// врага, а самостоятельная вещь: коммандеру достался ствол со средними числами,
/// и это тот же тип данных, что у пушки толстяка.
///
/// Все дальности и скорости заданы в юнитах мира (1 юнит = клетка), в пиксели переводят
/// свойства с суффиксом Px — чтобы в .tres числа читались в игровых, а не экранных величинах.
/// </summary>
[GlobalClass]
public partial class WeaponDef : Resource
{
    [Export] public string Id = "";
    [Export] public string DisplayName = "";

    /// <summary>Дальность стрельбы в юнитах.</summary>
    [Export] public float Range = 6f;

    /// <summary>Урон одного снаряда.</summary>
    [Export] public float Damage = 10f;

    /// <summary>Секунд между выстрелами. Меньше — плотнее очередь.</summary>
    [Export] public float FireInterval = 1f;

    /// <summary>Скорость снаряда в юнитах в секунду.</summary>
    [Export] public float ProjectileSpeed = 14f;

    /// <summary>Случайный увод ствола в градусах — кучность очереди.</summary>
    [Export] public float SpreadDegrees = 1.5f;

    /// <summary>
    /// Раствор конуса прицеливания в градусах: пока цель вне конуса, стрелок только доворачивается.
    /// </summary>
    [Export] public float AimConeDegrees = 8f;

    /// <summary>Радиус снаряда в юнитах — и размер на экране, и радиус попадания.</summary>
    [Export] public float ProjectileRadius = 0.08f;

    [Export] public Color ProjectileColor = new(1f, 0.85f, 0.4f);

    public float RangePx => Range * Const.Unit;

    public float SpeedPx => ProjectileSpeed * Const.Unit;

    public float ProjectileRadiusPx => ProjectileRadius * Const.Unit;

    /// <summary>Половина раствора конуса в радианах — в такой форме его и спрашивают.</summary>
    public float AimCone => Mathf.DegToRad(AimConeDegrees);

    public float Spread => Mathf.DegToRad(SpreadDegrees);

    /// <summary>
    /// Сколько снаряду жить: дальность полёта плюс запас на промах, чтобы пуля
    /// не гасла ровно на границе радиуса и мимо цели улетала чуть дальше.
    /// </summary>
    public float Lifetime => SpeedPx <= 0f ? 0.1f : RangePx / SpeedPx * 1.25f;
}
