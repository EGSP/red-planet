using System.Collections.Generic;
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
        bool stale = handle.Revision != GM.Nav.Revision;

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

    /// <summary>Забыть путь. Звать не обязательно — невостребованное вычищается само.</summary>
    public void Release(object key)
    {
        if (key != null)
            _cache.Remove(key);
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
    private static Vector2 From(object key, PathHandle handle) =>
        key is Node2D node && Alive.Is(node) ? node.GlobalPosition : handle.Target;

    private void Solve(object key, PathHandle handle, Vector2 from)
    {
        _served++;

        _search.RecordExpanded = DebugFlags.PathsExpanded;

        bool found = _search.TryFind(from, handle.Target, handle.Radius, MaxNodesPerSearch, _points);

        handle.Fill(_points, found ? PathStatus.Ready : PathStatus.Unreachable);
        handle.Revision = GM.Nav.Revision;

        LastExpanded = _search.LastExpanded;
        WorstExpanded = Mathf.Max(WorstExpanded, LastExpanded);
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
