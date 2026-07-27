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

    [Export] public float CostOre;
    [Export] public float CostMetal;

    /// <summary>Что появляется в мире после достройки.</summary>
    [Export] public PackedScene Scene;

    /// <summary>Юнит не занимает клетки после достройки — он ходит.</summary>
    [Export] public bool IsUnit;

    /// <summary>Роли строителей, которым доступна эта постройка: commander, fabricator.</summary>
    [Export] public Godot.Collections.Array<string> BuildableBy = new() { "commander" };

    /// <summary>Хук на будущее: постройка сама умеет строить в своём радиусе.</summary>
    [Export] public bool CanAssemble;

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

    /// <summary>Полный объём работы — суммарная стоимость в единицах.</summary>
    public float TotalWork => CostOre + CostMetal;

    public bool AvailableFor(string role) => BuildableBy.Contains(role);

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
