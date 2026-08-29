using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// Обслуживание запросов пути и кеш найденного.
///
/// ГРАНИЦА ОТВЕТСТВЕННОСТИ. Система не знает об игре ничего: ни приказов, ни очередей,
/// ни сторон, ни того, что ключ — это юнит. Запрос описывается двумя точками и радиусом,
/// ключ для неё непрозрачен и сравнивается по ссылке. Отсюда следует, что система
/// не решает, куда идти, не отменяет приказы и не сообщает о прибытии: она отвечает
/// на вопрос «как пройти отсюда туда» и запоминает ответ.
///
/// ВЫЧИСТКА ПО НЕВОСТРЕБОВАННОСТИ, а не по гибели ключа. Подписка на выбытие сущности
/// потребовала бы знания о том, что ключи — сущности, и это ровно то знание, которого
/// здесь быть не должно. Побочная выгода: вызывающему не нужно помнить о Release
/// в каждой ветке кода.
///
/// БЮДЖЕТ существует не ради нынешней нагрузки, а чтобы всплеск приказов не давал провала
/// кадра: групповой приказ полусотне юнитов иначе запустил бы полсотни поисков подряд.
/// </summary>
public partial class PathfindingSystem : GameSystem
{
    /// <summary>Сколько поисков разрешено за кадр. Остальное ждёт в очереди.</summary>
    [Export] public int MaxSearchesPerFrame = 4;

    /// <summary>Потолок раскрытых узлов на поиск. Превышение считается недостижимостью.</summary>
    [Export] public int MaxNodesPerSearch = 4000;

    /// <summary>
    /// Потолок узлов на местную починку. Меньше основного намеренно: починка выгодна лишь
    /// пока она дешевле полного поиска, а не найдя обхода за отведённое число узлов,
    /// разумнее пересчитать путь целиком.
    /// </summary>
    [Export] public int MaxNodesPerRepair = 600;

    /// <summary>
    /// Во сколько раз обход вправе оказаться длиннее заменяемого участка. Починка местная
    /// и об остальном маршруте не знает, поэтому слишком длинный обход означает, что дешевле
    /// пересчитать путь целиком.
    /// </summary>
    [Export] public float RepairSlack = 1.5f;

    /// <summary>Через сколько секунд без обращения путь забывается.</summary>
    [Export] public float CacheTtl = 2f;

    /// <summary>
    /// На сколько цель может сместиться, не требуя пересчёта. Меньше ячейки растра смысла
    /// не имеет: путь всё равно проложен по ячейкам.
    /// </summary>
    [Export] public float GoalTolerance = Const.NavCell;

    private readonly Dictionary<object, PathHandle> _cache = new(ByReference.Instance);
    private readonly List<object> _queue = new();
    private readonly List<Vector2> _points = new();
    private readonly List<object> _expired = new();
    private readonly List<Vector2> _repaired = new();
    private readonly List<Vector2> _band = new();
    private readonly List<Vector2> _chain = new();
    private readonly List<int> _tiles = new();

    private PathSearch _search;

    private double _now;
    private double _sweptAt;
    private int _served;

    /// <summary>Запросов за прошедший кадр. Показывает панель отладки.</summary>
    public int Requests { get; private set; }

    /// <summary>Из них отвеченных готовым путём.</summary>
    public int Hits { get; private set; }

    public int Pending => _queue.Count;

    public int Cached => _cache.Count;

    public int LastExpanded { get; private set; }

    public int WorstExpanded { get; private set; }

    /// <summary>Сколько поисков прошло по полосе макро-поиска.</summary>
    public int Bands => _search?.Bands ?? 0;

    /// <summary>Сколько раз полоса не дала пути и поиск повторялся по всему растру.</summary>
    public int Fallbacks => _search?.Fallbacks ?? 0;

    /// <summary>Областей в полосе последнего поиска.</summary>
    public int LastBand => _search?.LastBand ?? 0;

    /// <summary>Сколько путей починено местным обходом за сессию.</summary>
    public int Repairs { get; private set; }

    /// <summary>Сколько раз починка не удалась и путь считался заново.</summary>
    public int RepairMisses { get; private set; }

    private int _requests;
    private int _hits;

    /// <summary>Пути под ключами — читает отрисовка. Спрос через него кеш не освежает.</summary>
    public IReadOnlyDictionary<object, PathHandle> Paths => _cache;

    protected override void OnRegister() => _search = new PathSearch(GM.Nav);

    /// <summary>Путь по ключу, если он есть. Отрисовке — да, движению — нет: ему нужен Request.</summary>
    public PathHandle Peek(object key) =>
        key != null && _cache.TryGetValue(key, out var handle) ? handle : null;

    /// <summary>
    /// Запросить или обновить путь под ключом. Пересчёт назначается, когда цель сместилась
    /// дальше допуска либо растр менялся с прошлого расчёта; иначе возвращается готовое.
    ///
    /// Первый запрос за кадр считается сразу, если бюджет не исчерпан: иначе приказ
    /// откликался бы на кадр позже, и это было бы заметно на одиночном юните.
    /// </summary>
    public PathHandle Request(object key, Vector2 from, Vector2 to, float radiusPx)
    {
        if (key == null)
            return null;

        _requests++;

        if (!_cache.TryGetValue(key, out var handle))
        {
            handle = new PathHandle();
            handle.Attach(GM.Nav);
            _cache[key] = handle;
        }

        handle.Touched = _now;
        handle.Radius = radiusPx;

        bool moved = handle.Target.DistanceTo(to) > GoalTolerance;
        bool stale = Stale(handle);

        // Починка идёт до объявления пути грязным: она либо возвращает годную ломаную,
        // либо отказывается, и тогда путь пересчитывается обычным порядком
        if (stale && !moved && TryRepair(handle, from))
            return handle;

        if (moved || stale)
        {
            handle.Target = to;
            handle.Dirty = true;
        }

        if (!handle.Dirty)
        {
            _hits++;
            return handle;
        }

        if (_served < MaxSearchesPerFrame)
            Solve(key, handle, from);
        else
            Enqueue(key, handle);

        return handle;
    }

    /// <summary>
    /// Устарел ли путь. Готовая ломаная сверяется по тайлам, через которые проложена;
    /// путь без ломаной — недостижимая цель либо ещё не посчитанный запрос — сверяется
    /// по ревизии растра целиком, иначе он не пересчитался бы никогда.
    /// </summary>
    private bool Stale(PathHandle handle) =>
        handle.Status == PathStatus.Ready && handle.Tiles.Count > 0
            ? GM.Nav.Touched(handle.Tiles, handle.Revision)
            : handle.Revision != GM.Nav.Revision;

    /// <summary>
    /// Починить путь местным обходом вместо полного пересчёта.
    ///
    /// ЗАЧЕМ. Постройка задевает один-четыре тайла, а прежде обесценивала весь маршрут.
    /// Хвост ломаной за изменённой областью остаётся верным, поэтому пересчитывать
    /// достаточно начало — от нынешнего положения до первой точки за этой областью.
    ///
    /// ГРАНИЦА УЧАСТКА БЕРЁТСЯ ИЗ САМОЙ ЛОМАНОЙ. Цепочка ячеек после сглаживания не хранится,
    /// и восстанавливать её ради точных границ пришлось бы памятью на каждого идущего.
    /// Точка перегиба за изменённой областью годится не хуже и уже есть.
    ///
    /// Возвращает false, когда починка неуместна: изменение задело последний участок пути,
    /// обхода не нашлось за отведённые узлы либо он вышел заметно длиннее прежнего.
    /// </summary>
    private bool TryRepair(PathHandle handle, Vector2 from)
    {
        if (handle.Status != PathStatus.Ready || handle.Points.Count == 0)
            return false;

        var points = handle.Points;
        int last = -1;

        for (int i = handle.Cursor; i < points.Count; i++)
        {
            _tiles.Clear();
            NavGrid.TilesAlong(i == handle.Cursor ? from : points[i - 1], points[i], _tiles);

            if (GM.Nav.Touched(_tiles, handle.Revision))
                last = i;
        }

        // Изменение осталось позади идущего: впереди ломаная по-прежнему верна, и всё,
        // что требуется, — заново описать её покрытие
        if (last < 0)
        {
            handle.Revision = GM.Nav.Revision;
            Cover(handle, from);
            return true;
        }

        // Задет последний участок: чинить нечего, обход и есть весь остаток пути
        if (last >= points.Count - 1)
            return false;

        int join = last + 1;

        if (_served >= MaxSearchesPerFrame)
            return false;

        _served++;

        if (!_search.TryFind(from, points[join], handle.Radius, MaxNodesPerRepair, _points))
        {
            RepairMisses++;
            return false;
        }

        float replaced = from.DistanceTo(points[handle.Cursor]);

        for (int i = handle.Cursor; i < join; i++)
            replaced += points[i].DistanceTo(points[i + 1]);

        if (PolylineLength(from, _points) > Mathf.Max(replaced, 1f) * RepairSlack)
        {
            RepairMisses++;
            return false;
        }

        _repaired.Clear();
        _repaired.AddRange(_points);

        for (int i = join + 1; i < points.Count; i++)
            _repaired.Add(points[i]);

        handle.Fill(_repaired, PathStatus.Ready);
        handle.Revision = GM.Nav.Revision;
        Cover(handle, from);
        Macro(handle);
        Repairs++;
        return true;
    }

    /// <summary>Забыть путь. Звать не обязательно — невостребованное вычищается само.</summary>
    public void Release(object key)
    {
        if (key != null)
            _cache.Remove(key);
    }

    /// <summary>
    /// Длина пути от <paramref name="from"/> до <paramref name="to"/> без записи в кеш
    /// и вне бюджета кадра. Нужна разовым оценкам (выбор выезда завода), а не движению:
    /// движению отвечает <see cref="Request"/>, и именно он делит кадр между запросами.
    /// </summary>
    public bool TryMeasure(Vector2 from, Vector2 to, float radiusPx, out float length)
    {
        length = float.PositiveInfinity;

        if (_search == null)
            return false;

        bool found = _search.TryFind(from, to, radiusPx, MaxNodesPerSearch, _points);
        if (!found)
            return false;

        length = PolylineLength(from, _points);
        return true;
    }

    private static float PolylineLength(Vector2 from, List<Vector2> points)
    {
        float length = 0f;
        var prev = from;

        foreach (var point in points)
        {
            length += prev.DistanceTo(point);
            prev = point;
        }

        return length;
    }

    public override void Step(double dt)
    {
        GM.Nav.Poll();

        _now += dt;

        Requests = _requests;
        Hits = _hits;
        _requests = 0;
        _hits = 0;
        _served = 0;

        Drain();

        // Чистка раз в секунду: обход словаря дешёвый, но делать его каждый кадр
        // ради записей, живущих секундами, незачем
        if (_now - _sweptAt >= 1.0)
            Sweep();
    }

    public override void CaptureSnapshot(JsonObject data)
    {
        data["requests"] = Requests;
        data["hits"] = Hits;
        data["hit_ratio"] = Requests > 0 ? Hits / (double)Requests : 0.0;
        data["pending"] = Pending;
        data["cached"] = Cached;
        data["last_expanded"] = LastExpanded;
        data["worst_expanded"] = WorstExpanded;
        data["max_searches_per_frame"] = MaxSearchesPerFrame;
        data["max_nodes_per_search"] = MaxNodesPerSearch;
        data["repairs"] = Repairs;
        data["repair_misses"] = RepairMisses;
        data["macro_bands"] = Bands;
        data["macro_fallbacks"] = Fallbacks;
    }

    /// <summary>
    /// Отложенные запросы. Точка отправления берётся не запомненная, а сегодняшняя:
    /// юнит за время ожидания сдвинулся, и путь от старой точки начинался бы позади него.
    /// </summary>
    private void Drain()
    {
        int at = 0;

        while (at < _queue.Count && _served < MaxSearchesPerFrame)
        {
            var key = _queue[at];
            at++;

            if (!_cache.TryGetValue(key, out var handle))
                continue;

            handle.Queued = false;

            if (!handle.Dirty)
                continue;

            Solve(key, handle, From(key, handle));
        }

        if (at > 0)
            _queue.RemoveRange(0, at);
    }

    /// <summary>
    /// Откуда вести отложенный путь. Ключ для системы непрозрачен, поэтому положение
    /// у него спрашивается единственным допустимым способом — через общий признак
    /// подвижной сущности, и только если ключ им оказался.
    /// </summary>
    private static Vector2 From(object key, PathHandle handle) => key switch
    {
        // Сущность мира спрашивается первой: у неё положение лежит в поле и внутри шага
        // верно, тогда как узел показывает конец прошлого шага
        Entity entity when entity.Live => entity.GlobalPosition,
        Node2D node when Alive.Is(node) => node.GlobalPosition,
        _ => handle.Target,
    };

    private void Solve(object key, PathHandle handle, Vector2 from)
    {
        _served++;

        _search.RecordExpanded = DebugFlags.PathsExpanded;

        bool found = _search.TryFind(from, handle.Target, handle.Radius, MaxNodesPerSearch, _points);

        handle.Fill(_points, found ? PathStatus.Ready : PathStatus.Unreachable);
        handle.Revision = GM.Nav.Revision;
        Cover(handle, from);
        Macro(handle);

        LastExpanded = _search.LastExpanded;
        WorstExpanded = Mathf.Max(WorstExpanded, LastExpanded);
    }

    /// <summary>
    /// Запомнить тайлы, через которые прошла ломаная. Отправная точка входит в покрытие
    /// наравне с ломаной: первый отрезок начинается там, где юнит стоял в момент расчёта.
    /// </summary>
    private static void Cover(PathHandle handle, Vector2 from)
    {
        handle.Tiles.Clear();

        var previous = from;

        foreach (var point in handle.Points)
        {
            NavGrid.TilesAlong(previous, point, handle.Tiles);
            previous = point;
        }
    }

    /// <summary>
    /// Запомнить у пути полосу и цепочку макро-поиска, которыми он посчитан. Пустые списки
    /// означают, что поиск шёл по всему растру: либо слоя областей нет, либо полоса не дала
    /// пути и поиск повторился без ограничения.
    /// </summary>
    private void Macro(PathHandle handle)
    {
        _band.Clear();
        _chain.Clear();

        var macro = _search?.LastMacro;

        if (macro != null)
        {
            foreach (int region in macro.Members)
                _band.Add(macro.CenterOf(region));

            foreach (int region in macro.Chain)
                _chain.Add(macro.CenterOf(region));
        }

        handle.Trace(_band, _chain);
    }

    private void Enqueue(object key, PathHandle handle)
    {
        if (handle.Queued)
            return;

        handle.Queued = true;
        _queue.Add(key);
    }

    private void Sweep()
    {
        _sweptAt = _now;
        _expired.Clear();

        foreach (var pair in _cache)
            if (_now - pair.Value.Touched > CacheTtl)
                _expired.Add(pair.Key);

        foreach (var key in _expired)
            _cache.Remove(key);

        _expired.Clear();
    }

    /// <summary>Раскрытые узлы последнего поиска — рисует отладка, когда её об этом просят.</summary>
    public IReadOnlyList<Vector2> Expanded => _search?.Expanded;
}
