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

    /// <summary>Дальность инструмента в юнитах.</summary>
    [Export] public float ToolRange = 3f;

    [Export] public Color Color = new(0.4f, 0.7f, 1f);

    public bool CanBuild => BuildPower > 0f;

    public bool CanMine => MinePower > 0f;

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public float RadiusPx => Radius * Const.Unit;

    public float SpeedPx => Speed * Const.Unit;
}
