using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

/// <summary>
/// Чистый построитель навигационного снимка: растеризация, ограниченный chamfer и связность.
/// Не обращается к Node, Resource и прочим объектам Godot — пригоден для фонового потока.
/// </summary>
public static class NavBuilder
{
    public const int Straight = 3;
    public const int Diagonal = 4;

    /// <summary>Вход фонового задания: только значимые типы и массивы.</summary>
    public sealed class Request
    {
        public int SourceRevision;
        public int Width;
        public Vector2 WorldMin;
        public int CellPx;
        public int MaxClearance;
        public Obb[] Shapes;
        public Rect2 DirtyWorld;
        public bool RebuildAll;
        public NavSnapshot Previous;
        public int[] ComponentThresholds;
    }

    public static NavSnapshot Build(Request request)
    {
        var timer = Stopwatch.StartNew();

        int width = request.Width;
        int tileSize = NavTile.Size;
        int tilesPerSide = (width + tileSize - 1) / tileSize;
        int maxClearance = Math.Max(request.MaxClearance, Straight);
        int influence = InfluenceCells(maxClearance);

        var tiles = new NavTile[tilesPerSide * tilesPerSide];
        bool[] dirty = new bool[tiles.Length];
        var rebuilt = new List<int>();

        bool full = request.RebuildAll ||
                    request.Previous == null ||
                    request.Previous.Width != width ||
                    request.Previous.TileSize != tileSize ||
                    request.Previous.TilesPerSide != tilesPerSide;

        if (full)
        {
            for (int i = 0; i < dirty.Length; i++)
                dirty[i] = true;
        }
        else
        {
            Array.Copy(request.Previous.Tiles, tiles, tiles.Length);
            MarkDirtyTiles(request.DirtyWorld, request.WorldMin, request.CellPx, width, tileSize,
                tilesPerSide, influence, dirty);
        }

        for (int ty = 0; ty < tilesPerSide; ty++)
        {
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                int index = ty * tilesPerSide + tx;

                if (!dirty[index])
                    continue;

                tiles[index] = BuildTile(tx, ty, tileSize, width, request.WorldMin, request.CellPx,
                    maxClearance, request.Shapes);
                rebuilt.Add(index);
            }
        }

        // После замены тайлов пересчитать клиренс на всех грязных тайлах ещё раз нельзя
        // по одному: граничные значения зависят от соседей. Поэтому чемфер идёт по окну.
        ApplyClearance(tiles, dirty, tilesPerSide, tileSize, width, maxClearance);

        var regions = NavRegionBuilder.Build(tiles, dirty, tilesPerSide, tileSize, width,
            request.WorldMin, request.CellPx, request.ComponentThresholds,
            request.Previous?.Regions);

        timer.Stop();

        return new NavSnapshot(
            request.SourceRevision,
            width,
            tileSize,
            tilesPerSide,
            tiles,
            regions,
            timer.Elapsed.TotalMilliseconds,
            rebuilt.ToArray());
    }

    /// <summary>Сколько ячеек покрывает насыщенное chamfer-расстояние.</summary>
    public static int InfluenceCells(int maxClearance) =>
        Math.Max(1, (maxClearance + Straight - 1) / Straight);

    /// <summary>
    /// Диапазон тайлов, которых касается изменение области. Возвращает false, когда область
    /// не задевает поле вовсе: она пуста либо лежит целиком за его границами. Случай пустой
    /// области — «сведений нет, затронуты все тайлы» — разбирается до вызова, поскольку
    /// у вызывающего для него есть готовый признак, а «здание поставлено за полем» и
    /// «неизвестно, что изменилось» требуют противоположных действий.
    ///
    /// Метод открыт потому, что тем же диапазоном пользуется отмена путей: путь устаревает
    /// ровно тогда, когда пересчитан хоть один из тайлов, по которым он проложен.
    /// </summary>
    public static bool TileSpan(
        Rect2 dirtyWorld,
        Vector2 worldMin,
        int cellPx,
        int width,
        int tileSize,
        int influence,
        out int tx0,
        out int ty0,
        out int tx1,
        out int ty1)
    {
        tx0 = ty0 = tx1 = ty1 = 0;

        if (dirtyWorld.Size.X <= 0f || dirtyWorld.Size.Y <= 0f)
            return false;

        var min = ToCell(dirtyWorld.Position, worldMin, cellPx);
        var max = ToCell(dirtyWorld.End - new Vector2(0.001f, 0.001f), worldMin, cellPx);

        // Для корректного клиренса нужны и тайлы в зоне влияния, и ореол для границ
        int pad = influence + 1;
        int x0 = Math.Max(0, min.X - pad);
        int y0 = Math.Max(0, min.Y - pad);
        int x1 = Math.Min(width - 1, max.X + pad);
        int y1 = Math.Min(width - 1, max.Y + pad);

        if (x0 > x1 || y0 > y1)
            return false;

        tx0 = x0 / tileSize;
        ty0 = y0 / tileSize;
        tx1 = x1 / tileSize;
        ty1 = y1 / tileSize;
        return true;
    }

    private static void MarkDirtyTiles(
        Rect2 dirtyWorld,
        Vector2 worldMin,
        int cell,
        int width,
        int tileSize,
        int tilesPerSide,
        int influence,
        bool[] dirty)
    {
        if (dirtyWorld.Size.X <= 0f || dirtyWorld.Size.Y <= 0f)
        {
            for (int i = 0; i < dirty.Length; i++)
                dirty[i] = true;
            return;
        }

        if (!TileSpan(dirtyWorld, worldMin, cell, width, tileSize, influence,
                out int tx0, out int ty0, out int tx1, out int ty1))
            return;

        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                dirty[ty * tilesPerSide + tx] = true;
    }

    private static NavTile BuildTile(
        int tileX,
        int tileY,
        int tileSize,
        int width,
        Vector2 worldMin,
        int cell,
        int maxClearance,
        Obb[] shapes)
    {
        int area = tileSize * tileSize;
        var blocked = new bool[area];
        var distance = new int[area];

        int originX = tileX * tileSize;
        int originY = tileY * tileSize;

        for (int i = 0; i < shapes.Length; i++)
            Rasterize(shapes[i], worldMin, cell, width, originX, originY, tileSize, blocked);

        for (int i = 0; i < area; i++)
            distance[i] = blocked[i] ? 0 : maxClearance;

        return new NavTile(tileX, tileY, blocked, distance);
    }

    private static void Rasterize(
        in Obb shape,
        Vector2 worldMin,
        int cell,
        int width,
        int originX,
        int originY,
        int tileSize,
        bool[] blocked)
    {
        if (shape.IsEmpty)
            return;

        var bounds = shape.Bounds;
        var min = ToCell(bounds.Position, worldMin, cell);
        var max = ToCell(bounds.End - new Vector2(0.001f, 0.001f), worldMin, cell);

        int x0 = Math.Max(min.X, originX);
        int y0 = Math.Max(min.Y, originY);
        int x1 = Math.Min(max.X, Math.Min(width - 1, originX + tileSize - 1));
        int y1 = Math.Min(max.Y, Math.Min(width - 1, originY + tileSize - 1));

        if (x0 > x1 || y0 > y1)
            return;

        bool square = IsAxisAligned(shape.Angle);

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!square && !shape.Intersects(CellShape(x, y, worldMin, cell)))
                    continue;

                blocked[(y - originY) * tileSize + (x - originX)] = true;
            }
        }
    }

    private static void ApplyClearance(
        NavTile[] tiles,
        bool[] dirty,
        int tilesPerSide,
        int tileSize,
        int width,
        int maxClearance)
    {
        // Рабочее окно — объединение грязных тайлов; чтение снаружи окна идёт из уже
        // готовых тайлов снимка и фиксирует границу распространения.
        int tx0 = tilesPerSide, ty0 = tilesPerSide, tx1 = -1, ty1 = -1;

        for (int ty = 0; ty < tilesPerSide; ty++)
        {
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                if (!dirty[ty * tilesPerSide + tx])
                    continue;

                tx0 = Math.Min(tx0, tx);
                ty0 = Math.Min(ty0, ty);
                tx1 = Math.Max(tx1, tx);
                ty1 = Math.Max(ty1, ty);
            }
        }

        if (tx1 < 0)
            return;

        int cellX0 = tx0 * tileSize;
        int cellY0 = ty0 * tileSize;
        int cellX1 = Math.Min(width, (tx1 + 1) * tileSize);
        int cellY1 = Math.Min(width, (ty1 + 1) * tileSize);
        int windowW = cellX1 - cellX0;
        int windowH = cellY1 - cellY0;
        var distance = new int[windowW * windowH];

        for (int y = 0; y < windowH; y++)
        {
            for (int x = 0; x < windowW; x++)
            {
                int cellX = cellX0 + x;
                int cellY = cellY0 + y;
                distance[y * windowW + x] = ReadBlocked(tiles, tilesPerSide, tileSize, width, cellX, cellY)
                    ? 0
                    : maxClearance;
            }
        }

        for (int y = 0; y < windowH; y++)
        {
            for (int x = 0; x < windowW; x++)
            {
                int i = y * windowW + x;
                if (distance[i] == 0)
                    continue;

                int best = distance[i];
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x - 1, y, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Straight);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x, y - 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Straight);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x - 1, y - 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Diagonal);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x + 1, y - 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Diagonal);
                distance[i] = Math.Min(best, maxClearance);
            }
        }

        for (int y = windowH - 1; y >= 0; y--)
        {
            for (int x = windowW - 1; x >= 0; x--)
            {
                int i = y * windowW + x;
                if (distance[i] == 0)
                    continue;

                int best = distance[i];
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x + 1, y, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Straight);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x, y + 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Straight);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x + 1, y + 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Diagonal);
                best = Math.Min(best, ReadWindow(distance, windowW, windowH, x - 1, y + 1, tiles, tilesPerSide,
                    tileSize, width, cellX0, cellY0, maxClearance) + Diagonal);
                distance[i] = Math.Min(best, maxClearance);
            }
        }

        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                if (!dirty[ty * tilesPerSide + tx])
                    continue;

                var tile = tiles[ty * tilesPerSide + tx];
                int originX = tx * tileSize;
                int originY = ty * tileSize;

                for (int ly = 0; ly < tileSize; ly++)
                {
                    int cellY = originY + ly;
                    if (cellY >= width)
                        break;

                    for (int lx = 0; lx < tileSize; lx++)
                    {
                        int cellX = originX + lx;
                        if (cellX >= width)
                            break;

                        tile.Distance[ly * tileSize + lx] =
                            distance[(cellY - cellY0) * windowW + (cellX - cellX0)];
                    }
                }
            }
        }
    }

    private static int ReadWindow(
        int[] distance,
        int windowW,
        int windowH,
        int localX,
        int localY,
        NavTile[] tiles,
        int tilesPerSide,
        int tileSize,
        int width,
        int cellX0,
        int cellY0,
        int maxClearance)
    {
        int cellX = cellX0 + localX;
        int cellY = cellY0 + localY;

        if (cellX < 0 || cellY < 0 || cellX >= width || cellY >= width)
            return 0;

        if ((uint)localX < (uint)windowW && (uint)localY < (uint)windowH)
            return distance[localY * windowW + localX];

        return ReadDistance(tiles, tilesPerSide, tileSize, width, cellX, cellY, maxClearance);
    }

    private static bool ReadBlocked(
        NavTile[] tiles,
        int tilesPerSide,
        int tileSize,
        int width,
        int cellX,
        int cellY)
    {
        if (cellX < 0 || cellY < 0 || cellX >= width || cellY >= width)
            return true;

        int tx = cellX / tileSize;
        int ty = cellY / tileSize;
        var tile = tiles[ty * tilesPerSide + tx];
        int lx = cellX - tx * tileSize;
        int ly = cellY - ty * tileSize;
        return tile.Blocked[ly * tileSize + lx];
    }

    private static int ReadDistance(
        NavTile[] tiles,
        int tilesPerSide,
        int tileSize,
        int width,
        int cellX,
        int cellY,
        int maxClearance)
    {
        if (cellX < 0 || cellY < 0 || cellX >= width || cellY >= width)
            return 0;

        int tx = cellX / tileSize;
        int ty = cellY / tileSize;
        var tile = tiles[ty * tilesPerSide + tx];
        int lx = cellX - tx * tileSize;
        int ly = cellY - ty * tileSize;
        return tile.Distance[ly * tileSize + lx];
    }

    /// <summary>
    /// Сверить инкрементальный снимок с полной пересборкой тех же входов.
    /// Нужна проверке границы тайлов и зоны влияния клиренса.
    /// </summary>
    public static bool MatchesFullRebuild(Request request, NavSnapshot incremental, out int mismatches)
    {
        var fullRequest = new Request
        {
            SourceRevision = request.SourceRevision,
            Width = request.Width,
            WorldMin = request.WorldMin,
            CellPx = request.CellPx,
            MaxClearance = request.MaxClearance,
            Shapes = request.Shapes,
            DirtyWorld = new Rect2(request.WorldMin, new Vector2(request.Width * request.CellPx, request.Width * request.CellPx)),
            RebuildAll = true,
            Previous = null,
            ComponentThresholds = request.ComponentThresholds,
        };

        var full = Build(fullRequest);
        mismatches = 0;
        int area = request.Width * request.Width;

        for (int i = 0; i < area; i++)
        {
            if (incremental.BlockedAt(i) != full.BlockedAt(i) ||
                incremental.DistanceAt(i) != full.DistanceAt(i))
                mismatches++;
        }

        return mismatches == 0;
    }

    private static Vector2I ToCell(Vector2 world, Vector2 worldMin, int cell) => new(
        Mathf.FloorToInt((world.X - worldMin.X) / cell),
        Mathf.FloorToInt((world.Y - worldMin.Y) / cell));

    private static Obb CellShape(int x, int y, Vector2 worldMin, int cell) =>
        Obb.FromRect(new Rect2(worldMin + new Vector2(x, y) * cell, new Vector2(cell, cell)));

    private static bool IsAxisAligned(float angle) =>
        Mathf.IsZeroApprox(Mathf.Sin(angle * 2f));
}
