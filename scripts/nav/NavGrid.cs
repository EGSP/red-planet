using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Растровая карта навигации: непроходимость, клиренс и связность.
///
/// НЕ СИСТЕМА И НЕ ПРОЕКЦИЯ. Проекции впитывают документы из журнала, а навигация выводится
/// из положений живых сущностей — то есть из <see cref="ObstacleMap"/>. Поэтому карта просто
/// объект на композиционном корне.
///
/// ТАЙЛЫ И ФОН. Поле делится на тайлы <see cref="NavTile.Size"/> ячеек. Пересчёт
/// изменённой области и глобальная связность выполняются одним фоновым заданием; главный
/// поток публикует готовый <see cref="NavSnapshot"/> по ревизии. Пока снимок не готов,
/// добавленные препятствия учитываются временной маской непроходимости, а снятые остаются
/// непроходимыми по прежнему снимку.
///
/// КЛИРЕНС ВМЕСТО ШИРИНЫ КОРИДОРА. Задача юнитов разного размера решается одним сравнением:
/// ячейка годится, если расстояние до ближайшего препятствия не меньше радиуса.
/// Расстояние считается чемфером 3-4 и насыщается <see cref="NavSettings.MaxClearance"/>,
/// поэтому влияние правки конечно.
/// </summary>
public sealed class NavGrid : IClearanceField
{
    public const int CellPx = Const.NavCell;

    /// <summary>
    /// Ячеек по стороне. Величина перестала быть константой вместе с тем, как размер мира
    /// переехал в настройки: растр покрывает поле целиком, поэтому его сторона есть
    /// следствие размера поля, а не самостоятельное число.
    /// </summary>
    public static int Width => World.NavWidth;

    public static int Area => Width * Width;

    /// <summary>Тайлов по стороне поля.</summary>
    public static int TilesPerSide => (Width + NavTile.Size - 1) / NavTile.Size;

    /// <summary>Сторона тайла в пикселях.</summary>
    public static int TilePx => NavTile.Size * CellPx;

    public static int TileCount => TilesPerSide * TilesPerSide;

    /// <summary>Расстояние в третях ячейки: шаг по стороне.</summary>
    public const int Straight = NavBuilder.Straight;

    private readonly ObstacleMap _obstacles;
    private readonly List<Obb> _pendingAdds = new();
    private readonly HashSet<int> _pendingBlocked = new();
    private readonly List<int> _componentThresholds = new();
    private readonly object _exceptionLock = new();

    private NavSnapshot _active;
    private Task<NavSnapshot> _task;
    private CancellationTokenSource _cancel;

    private int _activeSourceRevision = -1;
    private int _requestedRevision = -1;
    private int _buildingRevision = -1;
    private int _pendingMaskObstacleRevision = int.MinValue;
    private int _pendingMaskActiveRevision = int.MinValue;
    private int _fittedWidth = -1;

    /// <summary>
    /// Когда тайл менялся последний раз: значение <see cref="Revision"/> на момент правки.
    /// По этим меткам путь обесценивается выборочно — только если тронут тайл, через который
    /// он проложен, — вместо прежней отмены всех путей при любой смене ревизии.
    /// </summary>
    private int[] _tileStamp = System.Array.Empty<int>();

    /// <summary>Ревизия источника, до которой метки тайлов уже проставлены.</summary>
    private int _stampedRevision = -1;

    /// <summary>
    /// Сколько порогов клиренса вошло в последнее запущенное задание. Порог добавляется
    /// первым обращением юнита нового размера, и без пересборки слой областей на него
    /// не появился бы до следующей постройки.
    /// </summary>
    private int _builtThresholds = -1;

    private Exception _backgroundError;

    /// <summary>
    /// Ревизия эффективной карты для кеша путей: растёт при изменении препятствий
    /// и при публикации нового снимка.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Ревизия источника у опубликованного снимка; −1, если снимка ещё нет.</summary>
    public int ActiveRevision => _activeSourceRevision;

    /// <summary>Ревизия источника, на которую сейчас идёт или запрошен пересчёт.</summary>
    public int RequestedRevision => _requestedRevision;

    /// <summary>Сколько занял последний фоновый пересчёт, миллисекунд.</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Сколько тайлов пересчитано в последнем задании.</summary>
    public int LastRebuiltTiles { get; private set; }

    /// <summary>Есть ли незавершённое фоновое задание.</summary>
    public bool BuildPending => _task != null && !_task.IsCompleted;

    /// <summary>Опубликованный снимок; может быть null до первого завершения задания.</summary>
    public NavSnapshot Active => _active;

    /// <summary>Прямоугольник последнего изменения источника.</summary>
    public Rect2 LastChange => _obstacles.LastChange;

    /// <summary>
    /// Текущие настройки навигации. Назначает <see cref="GameManager"/> из
    /// <c>resources/tuning/nav.tres</c>; без назначения действует экземпляр по умолчанию.
    /// </summary>
    public static NavSettings Settings { get; set; } = new();

    public NavGrid(ObstacleMap obstacles)
    {
        _obstacles = obstacles;
        _cancel = new CancellationTokenSource();
    }

    // ── координаты ────────────────────────────────────────────────────────────────

    public static Vector2I ToCell(Vector2 world) => new(
        Mathf.FloorToInt((world.X - World.Min.X) / CellPx),
        Mathf.FloorToInt((world.Y - World.Min.Y) / CellPx));

    public static Vector2 ToWorld(Vector2I cell) =>
        World.Min + new Vector2(cell.X + 0.5f, cell.Y + 0.5f) * CellPx;

    public static Vector2 ToWorld(int index) =>
        ToWorld(new Vector2I(index % Width, index / Width));

    public static bool InBounds(Vector2I cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Width;

    public static int IndexOf(Vector2I cell) => cell.Y * Width + cell.X;

    // ── запросы ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Какое расстояние в третях ячейки требуется, чтобы поместился радиус.
    ///
    /// Половина ячейки учитывается потому, что расстояние меряется до ЦЕНТРА непроходимой
    /// ячейки, а препятствие занимает её целиком: ближняя граница на полклетки ближе центра.
    /// <see cref="NavSettings.ClearanceFactor"/> масштабирует радиус до сравнения: меньше
    /// единицы — мягче проходимость, больше — строже. На поле расстояний не влияет.
    /// </summary>
    public static int Required(float radiusPx)
    {
        float factor = Mathf.Max(Settings?.ClearanceFactor ?? 1f, 0.01f);
        int required = Mathf.Max(1, Mathf.CeilToInt((radiusPx * factor / CellPx + 0.5f) * Straight));
        int cap = Mathf.Max(Settings?.MaxClearance ?? 12, Straight);
        return Mathf.Min(required, cap);
    }

    public bool Blocked(Vector2I cell)
    {
        Fresh();
        return !InBounds(cell) || IsBlocked(IndexOf(cell));
    }

    /// <summary>Сколько свободного места вокруг центра ячейки, пикселей.</summary>
    public float FreeRadius(Vector2I cell)
    {
        Fresh();

        if (!InBounds(cell))
            return 0f;

        return (DistanceOf(IndexOf(cell)) / (float)Straight - 0.5f) * CellPx;
    }

    public bool Passable(Vector2I cell, float radiusPx)
    {
        Fresh();
        return InBounds(cell) && DistanceOf(IndexOf(cell)) >= Required(radiusPx);
    }

    public bool Passable(Vector2 world, float radiusPx) => Passable(ToCell(world), radiusPx);

    /// <summary>
    /// Лежат ли точки в одной связной области. Отсекает недостижимую цель ДО поиска.
    /// Пока опубликованный снимок отстаёт от источника, проверка пропускается: иначе
    /// устаревшие метки могли бы отвергнуть ещё достижимый маршрут.
    /// </summary>
    public bool Connected(Vector2I a, Vector2I b, float radiusPx)
    {
        Fresh();

        if (!InBounds(a) || !InBounds(b))
            return false;

        if (_active == null || _activeSourceRevision != _obstacles.Revision)
            return true;

        int required = Required(radiusPx);
        RememberThreshold(required);

        if (_active.Layer(required) == null)
            return true;

        int first = _active.ComponentAt(IndexOf(a), required);
        return first != 0 && first == _active.ComponentAt(IndexOf(b), required);
    }

    /// <summary>
    /// Прямая видимость с учётом радиуса. Ходит по Брезенхэму, поэтому диагональный шаг
    /// проскакивает угловые ячейки. Для нашей задачи это допустимо: требуемый клиренс
    /// заведомо больше ячейки, и щель в один угол проходимой всё равно не окажется.
    /// </summary>
    public bool LineOfSight(Vector2 from, Vector2 to, float radiusPx)
    {
        Fresh();

        var a = ToCell(from);
        var b = ToCell(to);

        int dx = Mathf.Abs(b.X - a.X);
        int dy = Mathf.Abs(b.Y - a.Y);
        int stepX = a.X < b.X ? 1 : -1;
        int stepY = a.Y < b.Y ? 1 : -1;
        int error = dx - dy;

        int x = a.X;
        int y = a.Y;
        int required = Required(radiusPx);

        while (true)
        {
            var cell = new Vector2I(x, y);

            if (!InBounds(cell) || DistanceOf(IndexOf(cell)) < required)
                return false;

            if (x == b.X && y == b.Y)
                return true;

            int doubled = error * 2;

            if (doubled > -dy)
            {
                error -= dy;
                x += stepX;
            }

            if (doubled < dx)
            {
                error += dx;
                y += stepY;
            }
        }
    }

    /// <summary>
    /// Ближайшая проходимая ячейка. Нужна там, где цель оказалась внутри препятствия:
    /// приказ «идти сюда» по зданию не должен зависать, юнит обязан подойти к краю.
    /// </summary>
    public Vector2I NearestPassable(Vector2I cell, float radiusPx, int maxRings = 24)
    {
        Fresh();

        if (PassableWithoutFresh(cell, radiusPx))
            return cell;

        for (int ring = 1; ring <= maxRings; ring++)
        {
            for (int offset = -ring; offset <= ring; offset++)
            {
                if (TryRing(cell, offset, -ring, radiusPx, out var found) ||
                    TryRing(cell, offset, ring, radiusPx, out found) ||
                    TryRing(cell, -ring, offset, radiusPx, out found) ||
                    TryRing(cell, ring, offset, radiusPx, out found))
                    return found;
            }
        }

        return cell;
    }

    private bool TryRing(Vector2I center, int dx, int dy, float radiusPx, out Vector2I found)
    {
        found = new Vector2I(center.X + dx, center.Y + dy);
        return PassableWithoutFresh(found, radiusPx);
    }

    private bool PassableWithoutFresh(Vector2I cell, float radiusPx) =>
        InBounds(cell) && DistanceOf(IndexOf(cell)) >= Required(radiusPx);

    // ── тайлы ─────────────────────────────────────────────────────────────────────

    /// <summary>Номер тайла по ячейке. Отрицателен, если ячейка вне поля.</summary>
    public static int TileOf(Vector2I cell) =>
        InBounds(cell) ? cell.Y / NavTile.Size * TilesPerSide + cell.X / NavTile.Size : -1;

    /// <summary>
    /// Метка последней правки тайла. У неизвестного тайла метка равна текущей ревизии:
    /// это худший случай, при котором путь считается устаревшим.
    /// </summary>
    public int TileStamp(int tile) =>
        (uint)tile < (uint)_tileStamp.Length ? _tileStamp[tile] : Revision;

    /// <summary>
    /// Менялся ли хоть один из перечисленных тайлов после указанной ревизии.
    /// Это и есть выборочная отмена путей: перестройка одного тайла не трогает пути,
    /// проложенные в стороне от него.
    /// </summary>
    public bool Touched(List<int> tiles, int since)
    {
        if (tiles == null)
            return true;

        for (int i = 0; i < tiles.Count; i++)
        {
            if (TileStamp(tiles[i]) > since)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Тайлы, через которые проходит отрезок. Обход идёт по границам тайлов, а не выборкой
    /// точек: пропуск тайла означал бы пропуск отмены пути, то есть движение по устаревшей
    /// ломаной сквозь новое здание.
    /// </summary>
    public static void TilesAlong(Vector2 from, Vector2 to, List<int> into)
    {
        float size = TilePx;
        float x0 = (from.X - World.Min.X) / size;
        float y0 = (from.Y - World.Min.Y) / size;
        float x1 = (to.X - World.Min.X) / size;
        float y1 = (to.Y - World.Min.Y) / size;

        int tx = Mathf.FloorToInt(x0);
        int ty = Mathf.FloorToInt(y0);
        int lastX = Mathf.FloorToInt(x1);
        int lastY = Mathf.FloorToInt(y1);

        AddTile(tx, ty, into);

        float dx = x1 - x0;
        float dy = y1 - y0;
        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;

        float deltaX = stepX == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
        float deltaY = stepY == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dy);

        float nextX = stepX == 0
            ? float.PositiveInfinity
            : (stepX > 0 ? tx + 1 - x0 : x0 - tx) / Mathf.Abs(dx);

        float nextY = stepY == 0
            ? float.PositiveInfinity
            : (stepY > 0 ? ty + 1 - y0 : y0 - ty) / Mathf.Abs(dy);

        // Предел обхода — периметр поля с запасом: отрезок за границей растра тайлов
        // не задевает, а зацикливаться на вырожденных числах поиску незачем
        int guard = TilesPerSide * 4 + 8;

        while ((tx != lastX || ty != lastY) && guard-- > 0)
        {
            if (nextX < nextY)
            {
                tx += stepX;
                nextX += deltaX;
            }
            else
            {
                ty += stepY;
                nextY += deltaY;
            }

            AddTile(tx, ty, into);
        }
    }

    private static void AddTile(int tx, int ty, List<int> into)
    {
        int per = TilesPerSide;

        if (tx < 0 || ty < 0 || tx >= per || ty >= per)
            return;

        int index = ty * per + tx;

        if (!into.Contains(index))
            into.Add(index);
    }

    private void EnsureStamps()
    {
        if (_tileStamp.Length == TileCount)
            return;

        _tileStamp = new int[TileCount];
        StampAll();
    }

    private void StampAll()
    {
        for (int i = 0; i < _tileStamp.Length; i++)
            _tileStamp[i] = Revision;
    }

    /// <summary>
    /// Пометить тайлы, задетые изменением области источника. Пустая область означает,
    /// что состав правок неизвестен, и тогда помечаются все тайлы; область за пределами
    /// поля не помечает ни одного, поскольку на растр она не влияет.
    /// </summary>
    private void StampArea(Rect2 area)
    {
        if (area.Size.X <= 0f || area.Size.Y <= 0f)
        {
            StampAll();
            return;
        }

        int influence = NavBuilder.InfluenceCells(Mathf.Max(Settings?.MaxClearance ?? 12, Straight));

        if (!NavBuilder.TileSpan(area, World.Min, CellPx, Width, NavTile.Size, influence,
                out int tx0, out int ty0, out int tx1, out int ty1))
            return;

        int per = TilesPerSide;

        for (int ty = ty0; ty <= ty1; ty++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                int index = ty * per + tx;

                if ((uint)index < (uint)_tileStamp.Length)
                    _tileStamp[index] = Revision;
            }
        }
    }

    // ── жизненный цикл ────────────────────────────────────────────────────────────

    /// <summary>
    /// Сверить ревизию источника, принять готовое фоновое задание и при необходимости
    /// запустить следующее. Зовут перед запросами и из шага поиска пути.
    /// </summary>
    public void Fresh() => Poll();

    public void Poll()
    {
        ReportBackgroundError();
        Fit();
        EnsureStamps();
        SyncRequest();
        CompleteTask();
        RefreshPendingMask();
        TryStartBuild();
    }

    /// <summary>Остановить фоновое задание при разборке сессии. Обычный кадр не вызывает.</summary>
    public void Dispose()
    {
        _cancel.Cancel();

        try
        {
            _task?.Wait(100);
        }
        catch (AggregateException)
        {
        }

        _cancel.Dispose();
        _cancel = new CancellationTokenSource();
        _task = null;
    }

    private void Fit()
    {
        if (_fittedWidth == Width)
            return;

        _fittedWidth = Width;

        if (_task != null)
        {
            _cancel.Cancel();
            _cancel.Dispose();
            _cancel = new CancellationTokenSource();
            _task = null;
        }

        _active = null;
        _activeSourceRevision = -1;
        _requestedRevision = -1;
        _buildingRevision = -1;
        _pendingBlocked.Clear();
        _pendingMaskObstacleRevision = int.MinValue;
        _pendingMaskActiveRevision = int.MinValue;
        Revision++;
        _stampedRevision = -1;
        StampAll();
    }

    private void SyncRequest()
    {
        int source = _obstacles.Revision;

        if (source == _requestedRevision)
            return;

        _requestedRevision = source;
        Revision++;

        // Временная маска непроходимости уже действует, поэтому пути через изменённую
        // область устаревают сразу, не дожидаясь публикации снимка
        StampArea(_obstacles.ChangesSince(_stampedRevision));
        _stampedRevision = source;
    }

    private void CompleteTask()
    {
        if (_task == null || !_task.IsCompleted)
            return;

        Task<NavSnapshot> finished = _task;
        _task = null;

        if (finished.IsCanceled)
        {
            _buildingRevision = -1;
            return;
        }

        if (finished.IsFaulted)
        {
            lock (_exceptionLock)
                _backgroundError = finished.Exception?.GetBaseException();

            _buildingRevision = -1;
            return;
        }

        NavSnapshot snapshot = finished.Result;

        // Устаревший результат не заменяет более новую опубликованную карту
        if (snapshot.SourceRevision < _activeSourceRevision)
        {
            _buildingRevision = -1;
            return;
        }

        // Пересборка на той же ревизии источника содержимого тайлов не меняет: она бывает
        // только при появлении нового порога клиренса. Метки тайлов тогда не трогаем,
        // иначе такой пересчёт обесценил бы все пути разом
        bool sameSource = snapshot.SourceRevision == _activeSourceRevision;

        _active = snapshot;
        _activeSourceRevision = snapshot.SourceRevision;
        _buildingRevision = -1;
        LastBuildMs = snapshot.BuildMs;
        LastRebuiltTiles = snapshot.RebuiltTiles;
        _pendingMaskObstacleRevision = int.MinValue;
        _pendingMaskActiveRevision = int.MinValue;
        Revision++;

        if (!sameSource)
            StampTiles(snapshot.RebuiltTileIndices);
    }

    private void StampTiles(int[] tiles)
    {
        if (tiles == null)
            return;

        for (int i = 0; i < tiles.Length; i++)
        {
            int index = tiles[i];

            if ((uint)index < (uint)_tileStamp.Length)
                _tileStamp[index] = Revision;
        }
    }

    private void TryStartBuild()
    {
        if (_task != null || _requestedRevision < 0)
            return;

        bool thresholdsGrew = _builtThresholds != _componentThresholds.Count;

        if (_activeSourceRevision == _requestedRevision && _active != null &&
            _active.Width == Width && !thresholdsGrew)
            return;

        if (_buildingRevision == _requestedRevision && !thresholdsGrew)
            return;

        StartBuild(_requestedRevision);
    }

    private void StartBuild(int targetRevision)
    {
        _buildingRevision = targetRevision;
        _builtThresholds = _componentThresholds.Count;

        var previous = _active;
        bool rebuildAll = previous == null || previous.Width != Width;
        var dirty = rebuildAll
            ? World.Bounds
            : _obstacles.ChangesSince(previous.SourceRevision);

        // Если журнал уже не помнит промежуток, безопаснее пересчитать всё поле
        if (!rebuildAll && (dirty.Size.X <= 0f || dirty.Size.Y <= 0f) &&
            targetRevision != previous.SourceRevision)
        {
            rebuildAll = true;
            dirty = World.Bounds;
        }

        int[] thresholds = _componentThresholds.Count > 0
            ? _componentThresholds.ToArray()
            : new[] { Required(Const.Unit * 0.35f) };

        var request = new NavBuilder.Request
        {
            SourceRevision = targetRevision,
            Width = Width,
            WorldMin = World.Min,
            CellPx = CellPx,
            MaxClearance = Mathf.Max(Settings?.MaxClearance ?? 12, Straight),
            Shapes = _obstacles.SnapshotShapes(),
            DirtyWorld = dirty,
            RebuildAll = rebuildAll,
            Previous = previous,
            ComponentThresholds = thresholds,
        };

        CancellationToken token = _cancel.Token;

        _task = Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return NavBuilder.Build(request);
        }, token);
    }

    private void RefreshPendingMask()
    {
        int obstacleRevision = _obstacles.Revision;
        int active = _activeSourceRevision;

        if (_pendingMaskObstacleRevision == obstacleRevision &&
            _pendingMaskActiveRevision == active)
            return;

        _pendingBlocked.Clear();
        _pendingAdds.Clear();

        if (_active == null || active != obstacleRevision)
        {
            _obstacles.CopyAddsSince(active, _pendingAdds);

            foreach (var shape in _pendingAdds)
                RasterizePending(shape);
        }

        _pendingMaskObstacleRevision = obstacleRevision;
        _pendingMaskActiveRevision = active;
    }

    private void RasterizePending(in Obb shape)
    {
        if (shape.IsEmpty)
            return;

        var bounds = shape.Bounds;
        var min = ToCell(bounds.Position);
        var max = ToCell(bounds.End - new Vector2(0.001f, 0.001f));

        int x0 = Mathf.Max(min.X, 0);
        int y0 = Mathf.Max(min.Y, 0);
        int x1 = Mathf.Min(max.X, Width - 1);
        int y1 = Mathf.Min(max.Y, Width - 1);

        bool square = Mathf.IsZeroApprox(Mathf.Sin(shape.Angle * 2f));

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!square && !shape.Intersects(CellShape(x, y)))
                    continue;

                _pendingBlocked.Add(y * Width + x);
            }
        }
    }

    private static Obb CellShape(int x, int y) => Obb.FromRect(new Rect2(
        World.Min + new Vector2(x, y) * CellPx,
        new Vector2(CellPx, CellPx)));

    private void RememberThreshold(int required)
    {
        if (_componentThresholds.Contains(required))
            return;

        _componentThresholds.Add(required);
    }

    private void ReportBackgroundError()
    {
        Exception error;

        lock (_exceptionLock)
        {
            error = _backgroundError;
            _backgroundError = null;
        }

        if (error != null)
            GD.PushError($"[NavGrid] фоновый пересчёт: {error.Message}");
    }

    private bool IsBlocked(int index) =>
        _pendingBlocked.Contains(index) || (_active != null && _active.BlockedAt(index));

    private int DistanceOf(int index)
    {
        if (_pendingBlocked.Contains(index))
            return 0;

        if (_active != null)
            return _active.DistanceAt(index);

        // До первого снимка открытое поле считается свободным; здания закрыты маской
        return Mathf.Max(Settings?.MaxClearance ?? 12, Straight);
    }

    // ── чтение для отрисовки ──────────────────────────────────────────────────────

    /// <summary>Сторона растра. Свойство статическое, отсюда явная реализация интерфейса.</summary>
    int IClearanceField.Width => Width;

    /// <summary>Непроходимость по номеру ячейки. Перед массовым чтением нужен <see cref="Fresh"/>.</summary>
    public bool BlockedAt(int index) => IsBlocked(index);

    public int DistanceAt(int index) => DistanceOf(index);

    /// <summary>
    /// Слой областей на пороге клиренса для радиуса. Null, если снимка ещё нет либо слой
    /// на этот порог не строился: порог запоминается, и следующий пересчёт его добавит.
    /// </summary>
    public NavRegionLayer Layer(float radiusPx)
    {
        int required = Required(radiusPx);
        RememberThreshold(required);
        return _active?.Layer(required);
    }

    /// <summary>Метка связной области для отрисовки. Порог берётся у типового юнита.</summary>
    public int ComponentAt(int index, float radiusPx)
    {
        int required = Required(radiusPx);
        RememberThreshold(required);
        return _active != null ? _active.ComponentAt(index, required) : 0;
    }
}
