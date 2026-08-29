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
    /// <summary>
    /// Слои областей по порогам клиренса: связность и верхний уровень поиска пути.
    /// </summary>
    public readonly Dictionary<int, NavRegionLayer> Regions;
    public readonly double BuildMs;

    /// <summary>
    /// Номера тайлов, пересчитанных этим заданием. Нужны отмене путей: путь обесценивается
    /// только тогда, когда пересчитан хоть один тайл, по которому он проложен.
    /// </summary>
    public readonly int[] RebuiltTileIndices;

    public int RebuiltTiles => RebuiltTileIndices.Length;

    public NavSnapshot(
        int sourceRevision,
        int width,
        int tileSize,
        int tilesPerSide,
        NavTile[] tiles,
        Dictionary<int, NavRegionLayer> regions,
        double buildMs,
        int[] rebuiltTileIndices)
    {
        SourceRevision = sourceRevision;
        Width = width;
        TileSize = tileSize;
        TilesPerSide = tilesPerSide;
        Tiles = tiles;
        Regions = regions;
        BuildMs = buildMs;
        RebuiltTileIndices = rebuiltTileIndices ?? System.Array.Empty<int>();
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
        Regions != null && Regions.TryGetValue(required, out var layer)
            ? layer.ComponentAt(cellIndex)
            : 0;

    /// <summary>Слой областей на пороге клиренса; null, если слой не строился.</summary>
    public NavRegionLayer Layer(int required) =>
        Regions != null && Regions.TryGetValue(required, out var layer) ? layer : null;
}
