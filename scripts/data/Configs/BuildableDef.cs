using System.Collections.Generic;
using Godot;

/// <summary>
/// Справочник постройки. Форма задаётся матрицей строк: '#' — занятая клетка, '.' — пустая.
/// Например Rows = ["##", "##"] — квадрат 2x2, ["###", ".#."] — Т-образная форма.
/// </summary>
[GlobalClass]
public partial class BuildableDef : Resource
{
    [Export] public string Id = "";
    [Export] public string DisplayName = "";

    [Export] public Godot.Collections.Array<string> Rows = new() { "#" };

    /// <summary>
    /// Полная стоимость в метале. Она же — объём работы: время стройки равно
    /// стоимости, делённой на мощность строителей, как в PA.
    /// </summary>
    [Export] public float CostMetal;

    /// <summary>Сколько энергии постройка даёт в секунду. Больше нуля — это генератор.</summary>
    [Export] public float EnergyProduction;

    /// <summary>Насколько постройка поднимает потолок хранилища.</summary>
    [Export] public float MetalStorage;

    [Export] public float EnergyStorage;

    /// <summary>Что появляется в мире после достройки.</summary>
    [Export] public PackedScene Scene;

    /// <summary>Юнит не занимает клетки после достройки — он ходит.</summary>
    [Export] public bool IsUnit;

    /// <summary>Постройка сама работает инструментом в своём радиусе — башня-сборщик.</summary>
    [Export] public bool CanAssemble;

    /// <summary>Радиус обзора в юнитах. Он же рабочая зона для башни-сборщика.</summary>
    [Export] public float VisionRange = 6f;

    [Export] public float MaxHealth = 200f;

    /// <summary>
    /// Куда смотрит постройка, в градусах (0 — вправо). Здание статично и ноду не крутит:
    /// визуал привязан к клеткам сетки. Ось нужна ради единого правила — направление
    /// есть у любой сущности мира, — и пригодится турелям и воротам.
    /// </summary>
    [Export] public float FacingDegrees;

    [Export] public Color Color = new(0.6f, 0.6f, 0.65f);

    public int Width => Rows.Count > 0 ? Rows[0].Length : 1;

    public int Height => Rows.Count;

    public Vector2I Size => new(Width, Height);

    /// <summary>
    /// Полный объём работы. Меряется металом: мощность строителя — это метал в секунду,
    /// поэтому стройка на 100 метала при мощности 20 занимает пять секунд.
    ///
    /// Своей энергоцены у постройки нет намеренно: энергию тратит инструмент строителя,
    /// и сколько именно — записано в его справочнике (BuildEnergyPerPower).
    /// </summary>
    public float TotalWork => CostMetal;

    public bool IsGenerator => EnergyProduction > 0f;

    /// <summary>
    /// Сколько прочности даёт единица метала при ремонте. Курс не выдуманный: он выведен
    /// из самой постройки, поэтому починить её с нуля стоит ровно столько же, сколько
    /// построить, и занимает столько же времени — инструмент-то один и тот же.
    ///
    /// Ноль означает «ремонту не подлежит»: у постройки без цены курса быть не может.
    /// </summary>
    public float HealthPerMetal => CostMetal > 0f ? MaxHealth / CostMetal : 0f;

    public bool CanBeRepaired => HealthPerMetal > 0f;

    public float VisionRadiusPx => VisionRange * Const.Unit;

    /// <summary>Клетки, занимаемые постройкой при начале в origin.</summary>
    public IEnumerable<Vector2I> Cells(Vector2I origin)
    {
        for (int y = 0; y < Rows.Count; y++)
        {
            string row = Rows[y];
            for (int x = 0; x < row.Length; x++)
                if (row[x] == '#')
                    yield return new Vector2I(origin.X + x, origin.Y + y);
        }
    }
}
