using Godot;

/// <summary>
/// Справочник юнита. Роль задаётся числами, а не классом:
/// коммандер умеет и строить, и копать, фабрикатор — только строить, копатель — только копать.
/// </summary>
[GlobalClass]
public partial class UnitDef : Resource
{
    [Export] public string Id = "";
    [Export] public string DisplayName = "";

    /// <summary>Роль для списка доступных построек: commander или fabricator.</summary>
    [Export] public string Role = "fabricator";

    /// <summary>Скорость в юнитах в секунду.</summary>
    [Export] public float Speed = 3f;

    /// <summary>Скорость вращения корпуса в градусах в секунду.</summary>
    [Export] public float TurnSpeedDegrees = 180f;

    [Export] public float MaxHealth = 80f;

    /// <summary>Радиус корпуса в юнитах: и рисуется по нему, и попадают в него.</summary>
    [Export] public float Radius = 0.35f;

    /// <summary>
    /// Ствол юнита. Пусто — юнит безоружен. У коммандера пушка усреднённая:
    /// бьёт слабее толстяка и реже стрелка, зато достаёт дальше обоих.
    /// </summary>
    [Export] public WeaponDef Weapon;

    /// <summary>Мощность строительного инструмента, единиц в секунду.</summary>
    [Export] public float BuildPower;

    /// <summary>Мощность бура, единиц в секунду.</summary>
    [Export] public float MinePower;

    /// <summary>
    /// Сколько энергии в секунду съедает единица строительной мощности.
    ///
    /// Энергозатраты живут здесь, а не в справочнике постройки: жрёт энергию инструмент,
    /// а не бетон. Один и тот же каркас обойдётся дороже, если его сваривает прожорливый
    /// коммандер, и дешевле, если экономный фабрикатор.
    /// </summary>
    [Export] public float BuildEnergyPerPower = 5f;

    /// <summary>Сколько энергии в секунду съедает единица мощности бура.</summary>
    [Export] public float MineEnergyPerPower = 1.5f;

    /// <summary>Дальность инструмента в юнитах.</summary>
    [Export] public float ToolRange = 3f;

    /// <summary>
    /// Радиус обзора в юнитах. Он же рабочая зона: фабрикатор чинит то, что видит.
    /// </summary>
    [Export] public float VisionRange = 8f;

    /// <summary>
    /// Берётся ли строитель чинить юнитов, а не только постройки. Отдельным переключателем,
    /// потому что это вопрос не умения, а назначения: ремонтник поля боя и строитель базы —
    /// разные роли при одном и том же инструменте.
    /// </summary>
    [Export] public bool CanRepairUnits;

    /// <summary>
    /// Собственное производство, единиц в секунду. У коммандера оно есть с самого начала:
    /// без стартового дохода первую электростанцию было бы не на что строить.
    /// </summary>
    [Export] public float EnergyProduction;

    [Export] public float MetalProduction;

    [Export] public Color Color = new(0.4f, 0.7f, 1f);

    public bool CanBuild => BuildPower > 0f;

    public bool CanMine => MinePower > 0f;

    /// <summary>
    /// Чинит тот же инструмент, что и строит: отдельной ремонтной мощности нет.
    /// Так и в PA — build power одна на стройку, ремонт и достройку чужого.
    /// </summary>
    public bool CanRepair => CanBuild;

    /// <summary>Расход энергии в секунду при работе инструментом на полную.</summary>
    public float EnergyDrainFor(OrderKind kind) => kind == OrderKind.Mine
        ? MinePower * MineEnergyPerPower
        : BuildPower * BuildEnergyPerPower;

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public float RadiusPx => Radius * Const.Unit;

    public float VisionRadiusPx => VisionRange * Const.Unit;


    public float SpeedPx => Speed * Const.Unit;
}
