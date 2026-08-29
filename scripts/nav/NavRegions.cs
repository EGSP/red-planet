using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Области непрерывной проходимости внутри одного тайла.
///
/// Метка ноль означает непроходимую ячейку; проходимые размечены числами от единицы.
/// Разметка привязана к тайлу и не зависит от соседей, поэтому пересчёт тайла не трогает
/// разметку остальных — ровно то свойство, ради которого разбиение и заведено.
/// </summary>
public sealed class NavTileRegions
{
    public readonly int[] Labels;
    public readonly int Count;

    /// <summary>Середины областей в мировых координатах, по номеру метки минус единица.</summary>
    public readonly Vector2[] Centers;

    public NavTileRegions(int[] labels, int count, Vector2[] centers)
    {
        Labels = labels;
        Count = count;
        Centers = centers;
    }
}

/// <summary>
/// Граф областей на одном пороге клиренса: узлы — области тайлов, рёбра — переходы через
/// границу между соседними тайлами.
///
/// ЗАЧЕМ. Плоский растр отвечает на вопрос о проходимости ячейки, но не даёт никакого
/// представления о том, куда вести поиск; октильная оценка о зданиях не знает и заводит A*
/// в тупики, обход которых стоил тысяч раскрытых узлов. Граф областей отвечает на вопрос
/// «через какие части поля лежит дорога» за десятки узлов, после чего подробный поиск
/// ограничивается полученной полосой.
///
/// ПОЧЕМУ ГРАНИЦА. Связь тайла с соседями определяется исключительно его периметром:
/// внутренние ячейки на переходы не влияют. Отсюда следует, что рёбра восстанавливаются
/// обходом только граничных ячеек, а это доля порядка одной шестнадцатой от площади.
///
/// ЗДЕСЬ ЖЕ СВЯЗНОСТЬ. Прежде компоненты связности считались заливкой по всем ячейкам поля
/// на каждый пересчёт растра. Компонента графа областей даёт тот же ответ, но считается
/// по нескольким тысячам узлов вместо сотен тысяч ячеек.
/// </summary>
public sealed class NavRegionLayer
{
    /// <summary>Порог клиренса, на котором построен слой.</summary>
    public readonly int Required;

    public readonly int Width;
    public readonly int TileSize;
    public readonly int TilesPerSide;

    /// <summary>Разметка по тайлам. Неизменённые тайлы разделяются с прошлым слоем по ссылке.</summary>
    public readonly NavTileRegions[] Tiles;

    /// <summary>Номер первой области тайла в сквозной нумерации.</summary>
    public readonly int[] FirstId;

    /// <summary>Сколько всего областей в слое.</summary>
    public readonly int Count;

    /// <summary>Середины областей в мировых координатах по сквозному номеру.</summary>
    public readonly Vector2[] Centers;

    /// <summary>Метка компоненты связности по сквозному номеру; нумерация с единицы.</summary>
    public readonly int[] Component;

    /// <summary>Смещения списков смежности: рёбра области i лежат в [EdgeStart[i], EdgeStart[i+1]).</summary>
    public readonly int[] EdgeStart;

    public readonly int[] EdgeTarget;

    /// <summary>Стоимость перехода — расстояние между серединами областей в пикселях.</summary>
    public readonly int[] EdgeCost;

    public NavRegionLayer(
        int required,
        int width,
        int tileSize,
        int tilesPerSide,
        NavTileRegions[] tiles,
        int[] firstId,
        int count,
        Vector2[] centers,
        int[] component,
        int[] edgeStart,
        int[] edgeTarget,
        int[] edgeCost)
    {
        Required = required;
        Width = width;
        TileSize = tileSize;
        TilesPerSide = tilesPerSide;
        Tiles = tiles;
        FirstId = firstId;
        Count = count;
        Centers = centers;
        Component = component;
        EdgeStart = edgeStart;
        EdgeTarget = edgeTarget;
        EdgeCost = edgeCost;
    }

    /// <summary>Сквозной номер области по номеру ячейки; ноль означает непроходимую ячейку.</summary>
    public int RegionAt(int cellIndex)
    {
        if ((uint)cellIndex >= (uint)(Width * Width))
            return 0;

        int cellX = cellIndex % Width;
        int cellY = cellIndex / Width;
        int tileX = cellX / TileSize;
        int tileY = cellY / TileSize;

        if ((uint)tileX >= (uint)TilesPerSide || (uint)tileY >= (uint)TilesPerSide)
            return 0;

        int tile = tileY * TilesPerSide + tileX;
        var regions = Tiles[tile];

        if (regions == null)
            return 0;

        int local = (cellY - tileY * TileSize) * TileSize + (cellX - tileX * TileSize);
        int label = regions.Labels[local];
        return label == 0 ? 0 : FirstId[tile] + label;
    }

    /// <summary>Метка компоненты связности по номеру ячейки; ноль означает непроходимость.</summary>
    public int ComponentAt(int cellIndex)
    {
        int region = RegionAt(cellIndex);
        return region == 0 ? 0 : Component[region];
    }
}

/// <summary>
/// Построение слоёв областей. Чистый вычислитель без обращений к Godot сверх математики
/// векторов: работает в фоновом потоке вместе с остальным пересчётом растра.
/// </summary>
public static class NavRegionBuilder
{
    /// <summary>
    /// Собрать слои на перечисленные пороги клиренса. Разметка тайла берётся из прошлого
    /// слоя, если тайл не пересчитывался; всё остальное — нумерация, рёбра и компоненты —
    /// считается заново, поскольку стоит долей от разметки.
    /// </summary>
    public static Dictionary<int, NavRegionLayer> Build(
        NavTile[] tiles,
        bool[] dirty,
        int tilesPerSide,
        int tileSize,
        int width,
        Vector2 worldMin,
        int cellPx,
        int[] thresholds,
        Dictionary<int, NavRegionLayer> previous)
    {
        var result = new Dictionary<int, NavRegionLayer>();

        foreach (int threshold in thresholds ?? Array.Empty<int>())
        {
            int required = Math.Max(1, threshold);

            if (result.ContainsKey(required))
                continue;

            NavRegionLayer prior = null;

            if (previous != null &&
                previous.TryGetValue(required, out var candidate) &&
                candidate.Width == width &&
                candidate.TileSize == tileSize &&
                candidate.TilesPerSide == tilesPerSide)
                prior = candidate;

            result[required] = BuildLayer(tiles, dirty, tilesPerSide, tileSize, width,
                worldMin, cellPx, required, prior);
        }

        return result;
    }

    private static NavRegionLayer BuildLayer(
        NavTile[] tiles,
        bool[] dirty,
        int tilesPerSide,
        int tileSize,
        int width,
        Vector2 worldMin,
        int cellPx,
        int required,
        NavRegionLayer prior)
    {
        int tileCount = tilesPerSide * tilesPerSide;
        var marks = new NavTileRegions[tileCount];

        for (int tile = 0; tile < tileCount; tile++)
        {
            bool reuse = prior != null && dirty != null && !dirty[tile] && prior.Tiles[tile] != null;

            marks[tile] = reuse
                ? prior.Tiles[tile]
                : MarkTile(tiles[tile], tile, tilesPerSide, tileSize, width, worldMin, cellPx, required);
        }

        var firstId = new int[tileCount];
        int total = 0;

        for (int tile = 0; tile < tileCount; tile++)
        {
            firstId[tile] = total;
            total += marks[tile].Count;
        }

        var centers = new Vector2[total + 1];

        for (int tile = 0; tile < tileCount; tile++)
        {
            var regions = marks[tile];

            for (int local = 0; local < regions.Count; local++)
                centers[firstId[tile] + local + 1] = regions.Centers[local];
        }

        var edges = BuildEdges(marks, firstId, tilesPerSide, tileSize, width, total, centers);
        var component = BuildComponents(total, edges.Start, edges.Target);

        return new NavRegionLayer(required, width, tileSize, tilesPerSide, marks, firstId,
            total, centers, component, edges.Start, edges.Target, edges.Cost);
    }

    /// <summary>
    /// Разметить один тайл заливкой по восьми направлениям. Срезание углов запрещено так же,
    /// как в подробном поиске: иначе граф считал бы связанными области, между которыми
    /// поиск пути пройти не может.
    /// </summary>
    private static NavTileRegions MarkTile(
        NavTile tile,
        int tileIndex,
        int tilesPerSide,
        int tileSize,
        int width,
        Vector2 worldMin,
        int cellPx,
        int required)
    {
        int area = tileSize * tileSize;
        var labels = new int[area];

        int originX = tileIndex % tilesPerSide * tileSize;
        int originY = tileIndex / tilesPerSide * tileSize;

        var sumX = new List<double>();
        var sumY = new List<double>();
        var counts = new List<int>();
        var queue = new Queue<int>();
        int label = 0;

        for (int start = 0; start < area; start++)
        {
            if (labels[start] != 0 || !Open(tile, tileSize, width, originX, originY, start, required))
                continue;

            label++;
            labels[start] = label;
            sumX.Add(0.0);
            sumY.Add(0.0);
            counts.Add(0);
            queue.Clear();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                int lx = at % tileSize;
                int ly = at / tileSize;

                sumX[label - 1] += originX + lx;
                sumY[label - 1] += originY + ly;
                counts[label - 1]++;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int nx = lx + dx;
                        int ny = ly + dy;

                        if (nx < 0 || ny < 0 || nx >= tileSize || ny >= tileSize)
                            continue;

                        int next = ny * tileSize + nx;

                        if (labels[next] != 0)
                            continue;

                        if (!Open(tile, tileSize, width, originX, originY, next, required))
                            continue;

                        if (dx != 0 && dy != 0 &&
                            (!Open(tile, tileSize, width, originX, originY, ly * tileSize + nx, required) ||
                             !Open(tile, tileSize, width, originX, originY, ny * tileSize + lx, required)))
                            continue;

                        labels[next] = label;
                        queue.Enqueue(next);
                    }
                }
            }
        }

        var centers = new Vector2[label];

        for (int i = 0; i < label; i++)
        {
            float cx = (float)(sumX[i] / counts[i]) + 0.5f;
            float cy = (float)(sumY[i] / counts[i]) + 0.5f;
            centers[i] = worldMin + new Vector2(cx, cy) * cellPx;
        }

        return new NavTileRegions(labels, label, centers);
    }

    /// <summary>
    /// Проходима ли ячейка тайла. Ячейки за краем поля непроходимы: тайл на границе покрывает
    /// растр не целиком, когда сторона поля не делится на сторону тайла нацело.
    /// </summary>
    private static bool Open(
        NavTile tile,
        int tileSize,
        int width,
        int originX,
        int originY,
        int local,
        int required)
    {
        int lx = local % tileSize;
        int ly = local / tileSize;

        if (originX + lx >= width || originY + ly >= width)
            return false;

        return tile.Distance[local] >= required;
    }

    private readonly struct Edges
    {
        public readonly int[] Start;
        public readonly int[] Target;
        public readonly int[] Cost;

        public Edges(int[] start, int[] target, int[] cost)
        {
            Start = start;
            Target = target;
            Cost = cost;
        }
    }

    /// <summary>
    /// Рёбра между областями соседних тайлов. Обход идёт по двум границам каждого тайла —
    /// правой и нижней, — поэтому каждая граница поля осматривается один раз.
    ///
    /// Достаточно ортогонального соседства: диагональный шаг подробного поиска разрешён
    /// только при двух проходимых ортогональных соседях, и связность, найденная по диагонали,
    /// всегда находится и по стороне.
    /// </summary>
    private static Edges BuildEdges(
        NavTileRegions[] marks,
        int[] firstId,
        int tilesPerSide,
        int tileSize,
        int width,
        int total,
        Vector2[] centers)
    {
        var pairs = new HashSet<long>();
        var listStart = new List<int>[total + 1];

        void Connect(int a, int b)
        {
            if (a == 0 || b == 0 || a == b)
                return;

            long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;

            if (!pairs.Add(key))
                return;

            (listStart[a] ??= new List<int>()).Add(b);
            (listStart[b] ??= new List<int>()).Add(a);
        }

        for (int ty = 0; ty < tilesPerSide; ty++)
        {
            for (int tx = 0; tx < tilesPerSide; tx++)
            {
                int tile = ty * tilesPerSide + tx;

                if (tx + 1 < tilesPerSide)
                {
                    int right = tile + 1;

                    for (int ly = 0; ly < tileSize; ly++)
                    {
                        int a = RegionOfLocal(marks, firstId, tile, tileSize, tileSize - 1, ly);
                        int b = RegionOfLocal(marks, firstId, right, tileSize, 0, ly);
                        Connect(a, b);
                    }
                }

                if (ty + 1 < tilesPerSide)
                {
                    int below = tile + tilesPerSide;

                    for (int lx = 0; lx < tileSize; lx++)
                    {
                        int a = RegionOfLocal(marks, firstId, tile, tileSize, lx, tileSize - 1);
                        int b = RegionOfLocal(marks, firstId, below, tileSize, lx, 0);
                        Connect(a, b);
                    }
                }
            }
        }

        var start = new int[total + 2];
        int edgeCount = 0;

        for (int id = 1; id <= total; id++)
        {
            start[id] = edgeCount;
            edgeCount += listStart[id]?.Count ?? 0;
        }

        start[total + 1] = edgeCount;

        var target = new int[edgeCount];
        var cost = new int[edgeCount];
        int at = 0;

        for (int id = 1; id <= total; id++)
        {
            var neighbours = listStart[id];

            if (neighbours == null)
                continue;

            foreach (int other in neighbours)
            {
                target[at] = other;
                // Не меньше единицы: нулевое ребро лишило бы спуск по расстоянию строгого
                // убывания, и полоса не собралась бы там, где середины областей совпали
                cost[at] = Math.Max(1, Mathf.RoundToInt(centers[id].DistanceTo(centers[other])));
                at++;
            }
        }

        return new Edges(start, target, cost);
    }

    private static int RegionOfLocal(
        NavTileRegions[] marks,
        int[] firstId,
        int tile,
        int tileSize,
        int lx,
        int ly)
    {
        var regions = marks[tile];

        if (regions == null)
            return 0;

        int label = regions.Labels[ly * tileSize + lx];
        return label == 0 ? 0 : firstId[tile] + label;
    }

    /// <summary>Компоненты связности графа областей обходом в ширину.</summary>
    private static int[] BuildComponents(int total, int[] start, int[] target)
    {
        var component = new int[total + 1];
        var queue = new Queue<int>();
        int label = 0;

        for (int id = 1; id <= total; id++)
        {
            if (component[id] != 0)
                continue;

            label++;
            component[id] = label;
            queue.Clear();
            queue.Enqueue(id);

            while (queue.Count > 0)
            {
                int at = queue.Dequeue();

                for (int edge = start[at]; edge < start[at + 1]; edge++)
                {
                    int next = target[edge];

                    if (component[next] != 0)
                        continue;

                    component[next] = label;
                    queue.Enqueue(next);
                }
            }
        }

        return component;
    }
}
