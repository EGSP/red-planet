using Godot;

/// <summary>
/// Единицы измерения и мировые константы.
/// 1 unit = 64 px. Все размеры сущностей кратны юниту.
/// </summary>
public static class Const
{
    public const int Unit = 64;

    /// <summary>Мир — квадрат 41x41 клетка с центром в (0,0).</summary>
    public const int WorldRadiusCells = 20;

    // Руда появляется в кольце вокруг базы
    public const int OreRingMin = 6;
    public const int OreRingMax = 14;
    public const int OreSlots = 12;
    public const float OreRespawnDelay = 8f;
    public const float OreDepositAmount = 200f;

    public static Vector2 CellCorner(Vector2I cell) => new(cell.X * Unit, cell.Y * Unit);

    public static Vector2 CellCenter(Vector2I cell) =>
        new(cell.X * Unit + Unit * 0.5f, cell.Y * Unit + Unit * 0.5f);

    public static Vector2I WorldToCell(Vector2 pos) =>
        new(Mathf.FloorToInt(pos.X / Unit), Mathf.FloorToInt(pos.Y / Unit));

    /// <summary>Центр области, занимаемой постройкой размера size с началом в origin.</summary>
    public static Vector2 AreaCenter(Vector2I origin, Vector2I size) =>
        CellCorner(origin) + new Vector2(size.X, size.Y) * Unit * 0.5f;
}
