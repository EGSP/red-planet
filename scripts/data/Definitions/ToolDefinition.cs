using Godot;

/// <summary>
/// Инструмент — то, чем сущность действует на мир: ствол, строительная рука.
///
/// ПОЧЕМУ ОТДЕЛЬНОЕ ОПРЕДЕЛЕНИЕ, А НЕ ПОЛЯ ВНУТРИ ЮНИТА. Раньше строительная мощность
/// и её прожорливость лежали прямо в определении юнита, а дальность была одна на все
/// занятия, тогда как у ствола она своя. Инструмент как отдельная вещь снимает и то,
/// и другое: дальность принадлежит тому, кто её применяет, а одну и ту же руку можно
/// выдать двум разным юнитам, не повторяя чисел в двух файлах.
///
/// Модель взята из PA, где юнит носит список tools со ссылками на спецификации.
/// </summary>
public abstract class ToolDefinition
{
    public string Id = "";
    public string DisplayName = "";

    /// <summary>Дальность действия в юнитах мира.</summary>
    public float Range = 3f;

    /// <summary>
    /// Может ли инструмент наводиться независимо от корпуса на ходу.
    /// Иначе ось инструмента совпадает с направлением движения, а доворот к цели
    /// возможен только стоя: на ходу корпус принадлежит системе движения.
    /// </summary>
    public bool AimWhileMoving = true;

    public float RangePx => Range * Const.Unit;
}

/// <summary>
/// Ствол. Отдельным определением от носителя, потому что оружие — самостоятельная вещь:
/// коммандеру досталась пушка со средними числами, и это тот же тип данных, что у противника.
///
/// Все дальности и скорости заданы в юнитах мира (1 юнит = клетка), в пиксели переводят
/// свойства с суффиксом Px — чтобы в файле числа читались в игровых, а не экранных величинах.
/// </summary>
public sealed class WeaponDefinition : ToolDefinition
{
    /// <summary>Урон одного снаряда.</summary>
    public float Damage = 10f;

    /// <summary>Секунд между выстрелами. Меньше — плотнее очередь.</summary>
    public float FireInterval = 1f;

    /// <summary>Скорость снаряда в юнитах в секунду.</summary>
    public float ProjectileSpeed = 14f;

    /// <summary>Случайный увод ствола в градусах — кучность очереди.</summary>
    public float SpreadDegrees = 1.5f;

    /// <summary>
    /// Раствор конуса прицеливания в градусах: пока цель вне конуса, стрелок только доворачивается.
    /// </summary>
    public float AimConeDegrees = 8f;

    /// <summary>Радиус снаряда в юнитах — и размер на экране, и радиус попадания.</summary>
    public float ProjectileRadius = 0.08f;

    public Color ProjectileColor = new(1f, 0.85f, 0.4f);

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

/// <summary>
/// Виды работ, которые инструмент выполняет. Ремонт следует из стройки, см. ниже.
/// Вид пока один, но перечисление флагов остаётся: инструмент по своей природе умеет
/// несколько дел разом, и возвращать сюда второй вид не должно означать смену типа.
/// </summary>
[System.Flags]
public enum WorkKinds
{
    None = 0,
    Build = 1,
    Repair = 2,
}

/// <summary>
/// Рабочий инструмент: строительная рука.
///
/// ЕДИНАЯ МОЩНОСТЬ. Отдельной ремонтной мощности нет: чинит та же рука, что и строит,
/// и тратит столько же энергии. Так же устроено в PA, где build power одна на стройку,
/// ремонт и достройку чужого.
///
/// Энергозатраты живут здесь, а не в определении постройки: энергию потребляет инструмент,
/// а не бетон. Один и тот же каркас обойдётся дороже прожорливому коммандеру и дешевле
/// экономному фабрикатору.
/// </summary>
public sealed class WorkToolDefinition : ToolDefinition
{
    /// <summary>Мощность, единиц работы в секунду. Для стройки это метал в секунду.</summary>
    public float Power = 1f;

    /// <summary>Сколько энергии в секунду съедает единица мощности.</summary>
    public float EnergyPerPower = 5f;

    /// <summary>Что инструмент умеет.</summary>
    public WorkKinds Kinds = WorkKinds.None;

    /// <summary>
    /// Берётся ли инструмент чинить юнитов, а не только постройки. Отдельным признаком,
    /// потому что это вопрос не умения, а назначения: ремонтник поля боя и строитель базы
    /// пользуются одной и той же рукой.
    /// </summary>
    public bool RepairsUnits;

    public bool CanBuild => Kinds.HasFlag(WorkKinds.Build);

    /// <summary>
    /// Ремонт может быть отдельным умением: у боевого ремонтника стройки нет,
    /// а чинить он обязан. Если в файле указана только стройка — ремонт следует из неё,
    /// как в PA (одна мощность на оба занятия).
    /// </summary>
    public bool CanRepair =>
        Kinds.HasFlag(WorkKinds.Repair) || Kinds.HasFlag(WorkKinds.Build);

    /// <summary>Годен ли инструмент хоть на какую-то работу. По нему юнит получает руку.</summary>
    public bool CanWork => Kinds != WorkKinds.None;

    /// <summary>Расход энергии в секунду при работе на полную мощность.</summary>
    public float EnergyDrain => Power * EnergyPerPower;
}
