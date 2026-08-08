using System.Collections.Generic;

/// <summary>
/// Неизменяемый снимок навигационного растра на одну ревизию источника.
/// Массив тайлов копируется при публикации; неизменённые тайлы разделяются по ссылке.
/// </summary>
public sealed class NavSnapshot : IClearanceField
{
    public readonly int SourceRevision;
    public readonly int Width;
    public readonly int TileSize;
    public readonly int TilesPerSide;
    public readonly NavTile[] Tiles;
    public readonly Dictionary<int, int[]> Components;
    public readonly double BuildMs;
    public readonly int RebuiltTiles;

    public NavSnapshot(
        int sourceRevision,
        int width,
        int tileSize,
        int tilesPerSide,
        NavTile[] tiles,
        Dictionary<int, int[]> components,
        double buildMs,
        int rebuiltTiles)
    {
        SourceRevision = sourceRevision;
        Width = width;
        TileSize = tileSize;
        TilesPerSide = tilesPerSide;
        Tiles = tiles;
        Components = components;
        BuildMs = buildMs;
        RebuiltTiles = rebuiltTiles;
    }

    public bool TryIndex(int cellIndex, out NavTile tile, out int local)
    {
        tile = null;
        local = 0;

        if ((uint)cellIndex >= (uint)(Width * Width))
            return false;

        int cellX = cellIndex % Width;
        int cellY = cellIndex / Width;
        int tileX = cellX / TileSize;
        int tileY = cellY / TileSize;

        if ((uint)tileX >= (uint)TilesPerSide || (uint)tileY >= (uint)TilesPerSide)
            return false;

        tile = Tiles[tileY * TilesPerSide + tileX];
        int localX = cellX - tileX * TileSize;
        int localY = cellY - tileY * TileSize;
        local = localY * TileSize + localX;
        return tile != null;
    }

    public bool BlockedAt(int cellIndex) =>
        TryIndex(cellIndex, out var tile, out int local) && tile.Blocked[local];

    public int DistanceAt(int cellIndex) =>
        TryIndex(cellIndex, out var tile, out int local) ? tile.Distance[local] : 0;

    /// <summary>Сторона растра. Поле объявлено раньше интерфейса, отсюда явная реализация.</summary>
    int IClearanceField.Width => Width;

    /// <summary>
    /// Меткой состояния служит ревизия источника: снимок неизменяем, поэтому иной метки
    /// у него быть и не может.
    /// </summary>
    int IClearanceField.Revision => SourceRevision;

    public int ComponentAt(int cellIndex, int required) =>
        Components != null && Components.TryGetValue(required, out var labels) &&
        (uint)cellIndex < (uint)labels.Length
            ? labels[cellIndex]
            : 0;
}
