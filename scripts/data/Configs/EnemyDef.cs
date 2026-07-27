using Godot;

/// <summary>
/// Справочник врага. Вид врага — это набор чисел, а не класс: толстяк и стрелок
/// сделаны одним скриптом Enemy и различаются только .tres. Добавил файл в
/// resources/enemies — вид появился в игре, кода не трогая.
///
/// Размеры и скорости заданы в юнитах мира, перевод в пиксели — свойства с суффиксом Px.
/// </summary>
[GlobalClass]
public partial class EnemyDef : Resource
{
    [Export] public string Id = "";
    [Export] public string DisplayName = "";

    [Export] public float MaxHealth = 100f;

    /// <summary>Скорость хода в юнитах в секунду.</summary>
    [Export] public float Speed = 2f;

    /// <summary>Скорость вращения корпуса в градусах в секунду.</summary>
    [Export] public float TurnSpeedDegrees = 120f;

    /// <summary>Радиус корпуса в юнитах: и рисуется по нему, и попадают в него.</summary>
    [Export] public float Radius = 0.4f;

    [Export] public WeaponDef Weapon;

    /// <summary>
    /// Доля вида в спавне относительно остальных. Ноль — вид в игре есть,
    /// но сам не заводится: пригодится для боссов и скриптовых волн.
    /// </summary>
    [Export] public float SpawnWeight = 1f;

    /// <summary>
    /// На какой доле дальности оружия враг останавливается. Меньше единицы,
    /// чтобы цель не выпадала из радиуса от любого шага в сторону.
    /// </summary>
    [Export] public float StandoffFraction = 0.75f;

    [Export] public Color Color = new(0.9f, 0.35f, 0.3f);

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public float RadiusPx => Radius * Const.Unit;

    public float SpeedPx => Speed * Const.Unit;
}
