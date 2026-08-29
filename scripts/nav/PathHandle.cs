using System.Collections.Generic;
using Godot;

public enum PathStatus
{
    /// <summary>Запрос принят, расчёт ещё не прошёл.</summary>
    Pending,

    Ready,

    /// <summary>Цель в другой связной области либо исчерпан бюджет узлов.</summary>
    Unreachable,
}

/// <summary>
/// Найденный путь и место в нём.
///
/// Об игре не знает: ни приказов, ни сущностей, ни сторон. Знает только геометрию и то,
/// как по ней идти, — поэтому один и тот же путь одинаково годится юниту, врагу и всему,
/// что появится позже.
///
/// МЕТОД Direction — ЭТО ШОВ ПОД FLOWFIELD. Тот, кто движется, спрашивает направление
/// из точки и не знает, чем оно посчитано: ломаной сейчас или векторным полем потом.
/// Условия, при которых поле стоит вводить, описаны в docs/pathfinding.md.
/// </summary>
public sealed class PathHandle
{
    private readonly List<Vector2> _points = new();

    private NavGrid _grid;

    public PathStatus Status { get; private set; } = PathStatus.Pending;

    public IReadOnlyList<Vector2> Points => _points;

    private Vector2 _target;

    /// <summary>
    /// Куда просили дойти. Служит сверке кеша: сместилась запрошенная точка — путь
    /// пересчитывается.
    /// </summary>
    public Vector2 Target
    {
        get => _target;
        internal set
        {
            _target = value;

            // Пока результата нет, настоящей целью служит запрошенная точка. Иначе Goal
            // у непосчитанного пути остаётся нулевым вектором, то есть НАЧАЛОМ КООРДИНАТ
            // МИРА: ломаная пуста, Arrived поэтому истинно, и движение либо правит курс
            // на точку (0,0), либо объявляет прибытие, если сущность стоит рядом с нею.
            // А (0,0) — это середина базы игрока, где стоит почти всё, что она строит
            if (_points.Count == 0)
                Goal = value;
        }
    }

    /// <summary>
    /// Куда путь ведёт на самом деле. Отличается от запрошенного, когда точка лежит внутри
    /// постройки: поиск переносит её на ближайшее свободное место, и вести юнита нужно
    /// именно туда. Иначе он упирается в стену, считая, что не дошёл.
    /// </summary>
    public Vector2 Goal { get; private set; }

    public float Radius { get; internal set; }

    /// <summary>Номер следующей точки. Пройденные не выбрасываются — их рисует отладка.</summary>
    public int Cursor { get; private set; }

    /// <summary>Ревизия растра на момент расчёта. По ней сверяются метки тайлов.</summary>
    internal int Revision { get; set; }

    /// <summary>
    /// Тайлы растра, через которые проложена ломаная.
    ///
    /// Ради них путь и хранит собственный список: перестройка тайла в стороне от маршрута
    /// прежде обесценивала все пути разом, и групповой пересчёт занимал кадр целиком.
    /// Пустой список означает, что покрытие неизвестно, и тогда сверка идёт по ревизии
    /// растра целиком — так ведут себя недостижимые цели, у которых ломаной нет.
    /// </summary>
    internal List<int> Tiles { get; } = new();

    /// <summary>
    /// Середины областей, внутри которых поиску было позволено раскрывать ячейки, —
    /// полоса макро-поиска, посчитанная для ЭТОГО пути.
    ///
    /// ПОЧЕМУ ТОЧКИ, А НЕ НОМЕРА ОБЛАСТЕЙ. Номер осмыслен только вместе со слоем областей,
    /// а слой сменяется с каждым снимком растра, и хранить ссылку на него у пути значило бы
    /// удерживать в памяти устаревший снимок ради отладочной отрисовки. Мировая точка живёт
    /// сама по себе и после смены слоя всего лишь показывает, где полоса была.
    ///
    /// ПОЧЕМУ У ПУТИ, А НЕ У ПОИСКА. Вычислитель один на систему и помнит только последний
    /// запрос, поэтому показать полосу выделенного юнита он не может: к моменту показа
    /// в нём лежит чужая. Стоимость — несколько десятков точек на путь.
    /// </summary>
    public IReadOnlyList<Vector2> Band => _band;

    /// <summary>Цепочка макро-поиска от старта к цели: середины областей по порядку.</summary>
    public IReadOnlyList<Vector2> Chain => _chain;

    private readonly List<Vector2> _band = new();
    private readonly List<Vector2> _chain = new();

    /// <summary>Принять полосу и цепочку от системы. Списки правит только она.</summary>
    internal void Trace(List<Vector2> band, List<Vector2> chain)
    {
        _band.Clear();
        _band.AddRange(band);
        _chain.Clear();
        _chain.AddRange(chain);
    }

    /// <summary>Когда путь спрашивали последний раз. По этому числу кеш чистится.</summary>
    internal double Touched { get; set; }

    /// <summary>Ждёт ли путь пересчёта. Ставится системой, снимается расчётом.</summary>
    internal bool Dirty { get; set; } = true;

    /// <summary>Стоит ли запрос в очереди. Без признака он попал бы туда дважды.</summary>
    internal bool Queued { get; set; }

    internal void Attach(NavGrid grid) => _grid = grid;

    /// <summary>
    /// Принять результат поиска. Настоящей целью становится последняя точка ломаной:
    /// поиск мог сдвинуть её с занятого места, и знать об этом должен тот, кто идёт.
    /// </summary>
    internal void Fill(List<Vector2> points, PathStatus status)
    {
        _points.Clear();
        _points.AddRange(points);
        Cursor = 0;
        Status = status;
        Dirty = false;
        Goal = points.Count > 0 ? points[^1] : Target;
    }

    /// <summary>Путь пройден до конца — дальше ведёт сама цель.</summary>
    public bool Arrived => Cursor >= _points.Count;

    /// <summary>Куда идти сейчас. Пройдя ломаную, ведём прямо к цели.</summary>
    public Vector2 Waypoint => Arrived ? Goal : _points[Cursor];

    /// <summary>
    /// Подвинуть место в пути. Две работы сразу: снять достигнутые точки и срезать те,
    /// до которых уже видно напрямую.
    ///
    /// Срез делается не больше одного за вызов намеренно: проверка видимости стоит прохода
    /// по растру, а срезать всю ломаную за кадр незачем — юнит всё равно идёт к одной точке.
    /// </summary>
    public void Advance(Vector2 at, float reachPx)
    {
        while (!Arrived && at.DistanceTo(_points[Cursor]) <= reachPx)
            Cursor++;

        if (Arrived || _grid == null || Cursor + 1 >= _points.Count)
            return;

        if (_grid.LineOfSight(at, _points[Cursor + 1], Radius))
            Cursor++;
    }

    /// <summary>Куда двигаться из точки. Нулевой вектор означает «уже пришли».</summary>
    public Vector2 Direction(Vector2 at)
    {
        var delta = Waypoint - at;
        return delta.LengthSquared() > 0.0001f ? delta.Normalized() : Vector2.Zero;
    }
}
