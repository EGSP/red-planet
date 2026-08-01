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

    /// <summary>
    /// Куда просили дойти. Служит только сверке кеша: сместилась запрошенная точка —
    /// путь пересчитывается.
    /// </summary>
    public Vector2 Target { get; internal set; }

    /// <summary>
    /// Куда путь ведёт на самом деле. Отличается от запрошенного, когда точка лежит внутри
    /// постройки: поиск переносит её на ближайшее свободное место, и вести юнита нужно
    /// именно туда. Иначе он упирается в стену, считая, что не дошёл.
    /// </summary>
    public Vector2 Goal { get; private set; }

    public float Radius { get; internal set; }

    /// <summary>Номер следующей точки. Пройденные не выбрасываются — их рисует отладка.</summary>
    public int Cursor { get; private set; }

    /// <summary>Ревизия растра на момент расчёта. Разошлась — путь устарел.</summary>
    internal int Revision { get; set; }

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
