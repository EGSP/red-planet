using System.Collections.Generic;
using Godot;

/// <summary>
/// Поиск пути A* по растру навигации и сглаживание найденного.
///
/// ЧИСТЫЙ ВЫЧИСЛИТЕЛЬ. Об игре не знает ничего: на входе две точки и радиус, на выходе
/// ломаная. Кеш, бюджет и всё, что связано с тем, кто и зачем спросил, живут
/// в <see cref="PathfindingSystem"/>.
///
/// СЛУЖЕБНЫЕ МАССИВЫ ПЕРЕИСПОЛЬЗУЮТСЯ. Чистить сотни тысяч ячеек перед каждым поиском незачем:
/// вместо этого у каждой записи стоит номер прогона, и запись с чужим номером считается
/// пустой. Отсюда требование: один экземпляр — один поток и никакой вложенности вызовов.
/// </summary>
public sealed class PathSearch
{
    /// <summary>Стоимость шага по стороне. Целые числа, чтобы не копить ошибку сложения.</summary>
    private const int Straight = 10;

    private const int Diagonal = 14;

    /// <summary>
    /// Насколько поправка по графу областей вправе превысить октильную оценку, в долях
    /// от неё: одна восьмая.
    ///
    /// Ограничение подобрано замером (<c>scenes/tools/NavSelfCheck.tscn</c>). Без него
    /// поправка переоценивает остаток в открытом поле, и расход узлов растёт: 9 154 → 13 049
    /// на обходе гребёнки тупиков. С ограничением в одну восьмую расход падает во всех трёх
    /// проверяемых положениях: 3 142 → 1 272 через стену, 9 154 → 8 464 мимо тупиков,
    /// 3 096 → 1 912 внутрь плотной застройки.
    /// </summary>
    private const int GuidanceSlack = 8;

    private readonly NavGrid _grid;

    private readonly int[] _cost = new int[NavGrid.Area];
    private readonly int[] _from = new int[NavGrid.Area];
    private readonly int[] _stamp = new int[NavGrid.Area];
    private readonly bool[] _closed = new bool[NavGrid.Area];

    private readonly NavHeap _open = new(1024);

    private readonly List<Vector2> _raw = new();

    /// <summary>
    /// Обратный поиск по графу областей. Хранится у поиска, а не у запроса: соседние
    /// запросы за кадр обычно идут к одной цели, и расстояния для них считаются один раз.
    /// </summary>
    private readonly NavMacroSearch _route = new();

    /// <summary>Слой областей, по которому построена полоса текущего поиска.</summary>
    private NavRegionLayer _layer;

    /// <summary>Ограничивать ли раскрытие полосой. Снимается на повторе без ограничения.</summary>
    private bool _banded;

    /// <summary>Направлять ли оценку расстоянием по графу областей.</summary>
    private bool _guided;

    /// <summary>
    /// Последний поиск остановлен бюджетом узлов, а не исчерпанием очереди. Признак
    /// различает две причины отказа: «в полосе пути нет» и «кончились узлы».
    /// </summary>
    private bool _exhausted;

    private int _run;

    /// <summary>Сколько узлов раскрыл последний поиск. Показывает панель отладки.</summary>
    public int LastExpanded { get; private set; }

    /// <summary>Сколько поисков прошло по полосе макро-поиска. Показывает панель отладки.</summary>
    public int Bands { get; private set; }

    /// <summary>Сколько раз полоса не дала пути и поиск повторялся без ограничения.</summary>
    public int Fallbacks { get; private set; }

    /// <summary>Областей в полосе последнего поиска.</summary>
    public int LastBand { get; private set; }

    /// <summary>Слой областей последнего поиска; null, если поиск шёл по всему растру.</summary>
    public NavRegionLayer LastLayer => _banded ? _layer : null;

    /// <summary>Полоса последнего поиска; null, если поиск шёл по всему растру.</summary>
    public NavMacroSearch LastMacro => _banded ? _route : null;

    /// <summary>
    /// Раскрытые узлы последнего поиска — только для отрисовки, и только когда её просят:
    /// список стоит памяти и заполнения, а нужен раз в сессию при разборе странного пути.
    /// </summary>
    public List<Vector2> Expanded { get; } = new();

    public bool RecordExpanded { get; set; }

    /// <summary>
    /// Ограничивать ли поиск полосой макро-поиска. Снимается только проверками и разбором:
    /// сравнение расхода узлов с полосой и без неё — единственный способ судить о том,
    /// что полоса даёт на конкретной карте.
    /// </summary>
    public bool UseBand { get; set; } = true;

    /// <summary>
    /// Направлять ли оценку расстоянием по графу областей. Признак отдельный от полосы,
    /// поскольку приёмы независимы: полоса отсекает поле, оценка уводит от тупиков.
    /// </summary>
    public bool UseGuidance { get; set; } = true;

    public PathSearch(NavGrid grid) => _grid = grid;

    /// <summary>
    /// Найти путь. Возвращает false, когда цель недостижима или исчерпан бюджет узлов;
    /// в обоих случаях результат пуст.
    ///
    /// Первой точкой ломаной идёт первый поворот, а не исходное положение: следовать
    /// по пути начинают со следующей точки, и своя собственная в списке только мешала бы.
    /// </summary>
    public bool TryFind(Vector2 from, Vector2 to, float radiusPx, int maxNodes,
        List<Vector2> result)
    {
        result.Clear();
        LastExpanded = 0;

        _grid.Fresh();

        // Пока первый снимок не опубликован, поиск по маске допустим: открытое поле
        // проходимо, новые здания закрыты. После публикации сравнение ревизий кеша
        // подхватывает освобождённые клетки.
        var start = NavGrid.ToCell(from);
        var goal = NavGrid.ToCell(to);

        bool startOutside = !NavGrid.InBounds(start);

        // Цель за растром поля застройки не обслуживается: приказы ведут внутрь мира.
        // Старт снаружи допустим — подход до края считается открытым пространством.
        if (!NavGrid.InBounds(goal))
            return false;

        if (startOutside)
            start = EntryCell(from, radiusPx);

        // Цель внутри здания: ведём к ближайшему свободному месту, а не отказываем.
        // Приказ «идти сюда» по постройке должен приводить юнита к её краю
        if (!_grid.Passable(goal, radiusPx))
        {
            goal = _grid.NearestPassable(goal, radiusPx);
            to = NavGrid.ToWorld(goal);
        }

        // Стартовая ячейка может оказаться непроходимой: юнита вытолкнуло вплотную
        // к стене или на нём построили. Тогда отсчёт ведём от ближайшей свободной
        if (!_grid.Passable(start, radiusPx))
            start = _grid.NearestPassable(start, radiusPx);

        if (!_grid.Passable(start, radiusPx) || !_grid.Passable(goal, radiusPx))
            return false;

        // Прямая видимость — самый частый случай на нашей карте: поиск не запускаем вовсе.
        // Снаружи растра сегмент до входа свободен, поэтому смотрим от точки входа.
        var sightFrom = startOutside ? NavGrid.ToWorld(start) : from;

        if (_grid.LineOfSight(sightFrom, to, radiusPx))
        {
            result.Add(to);
            return true;
        }

        if (!_grid.Connected(start, goal, radiusPx))
            return false;

        bool restricted = Restrict(start, goal, radiusPx);

        if (!Search(start, goal, radiusPx, maxNodes))
        {
            // Полоса строится по снимку, который может отставать от источника, а сам граф
            // не знает о временной маске непроходимости. Поэтому неудача в полосе есть
            // не отсутствие пути, а причина повторить поиск по всему растру.
            if (!restricted)
                return false;

            // Бюджет узлов исчерпан внутри полосы, то есть поиск не дошёл до её границ.
            // Повтор по всему растру раскрыл бы те же узлы и остановился на том же пределе,
            // удвоив расход самого дорогого случая. Отказ возвращаем сразу
            if (_exhausted)
                return false;

            _banded = false;
            Fallbacks++;

            if (!Search(start, goal, radiusPx, maxNodes))
                return false;
        }

        Trace(start, goal, from, to);
        Smooth(radiusPx, result);
        return true;
    }

    /// <summary>
    /// Ближайшая проходимая ячейка на краю растра со стороны внешней точки: снаружи
    /// препятствий нет, поэтому достаточно зажать координаты ячейки в границы сетки.
    /// </summary>
    private Vector2I EntryCell(Vector2 from, float radiusPx)
    {
        var cell = NavGrid.ToCell(from);
        int last = NavGrid.Width - 1;

        cell = new Vector2I(
            Mathf.Clamp(cell.X, 0, last),
            Mathf.Clamp(cell.Y, 0, last));

        return _grid.NearestPassable(cell, radiusPx);
    }

    /// <summary>
    /// Подготовить полосу макро-поиска. Возвращает false, если поиск придётся вести
    /// по всему растру: слоя нет, область старта или цели неизвестна либо спуск по графу
    /// не дошёл до цели.
    /// </summary>
    private bool Restrict(Vector2I start, Vector2I goal, float radiusPx)
    {
        _banded = false;
        _guided = false;
        _layer = UseBand || UseGuidance ? _grid.Layer(radiusPx) : null;
        LastBand = 0;

        if (_layer == null)
            return false;

        int goalRegion = _layer.RegionAt(NavGrid.IndexOf(goal));
        int startRegion = _layer.RegionAt(NavGrid.IndexOf(start));

        if (goalRegion == 0 || startRegion == 0)
            return false;

        if (!_route.Matches(_layer, goalRegion))
            _route.Build(_layer, goalRegion);

        _guided = UseGuidance;

        if (!UseBand || !_route.BuildBand(startRegion))
            return _guided;

        _banded = true;
        LastBand = _route.LastBand;
        Bands++;
        return true;
    }

    private bool Search(Vector2I start, Vector2I goal, float radiusPx, int maxNodes)
    {
        _exhausted = false;
        _run++;
        _open.Clear();

        // Список раскрытых очищается здесь, а не при входе в запрос. Запрос, отвеченный
        // прямой видимостью или отсеянный связностью, поиска не ведёт, и стирать показанное
        // ему нечем: на открытой карте такие запросы составляют большинство, и отладочный
        // слой оставался бы пустым всегда
        if (RecordExpanded)
            Expanded.Clear();

        int required = NavGrid.Required(radiusPx);
        int startIndex = NavGrid.IndexOf(start);
        int goalIndex = NavGrid.IndexOf(goal);

        Touch(startIndex);
        _cost[startIndex] = 0;
        _from[startIndex] = -1;
        _open.Push(startIndex, Estimate(start, goal));

        while (_open.Count > 0)
        {
            int at = _open.Pop();

            if (_closed[at])
                continue;

            _closed[at] = true;
            LastExpanded++;

            if (RecordExpanded)
                Expanded.Add(NavGrid.ToWorld(at));

            if (at == goalIndex)
                return true;

            if (LastExpanded > maxNodes)
            {
                _exhausted = true;
                return false;
            }

            int cx = at % NavGrid.Width;
            int cy = at / NavGrid.Width;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = cx + dx;
                    int ny = cy + dy;

                    if (nx < 0 || ny < 0 || nx >= NavGrid.Width || ny >= NavGrid.Width)
                        continue;

                    int next = ny * NavGrid.Width + nx;

                    if (_grid.DistanceAt(next) < required)
                        continue;

                    // Область ячейки нужна дважды: полосой она отсекает лишнее поле,
                    // а расстоянием до цели по графу направляет поиск в обход тупиков
                    int region = _banded || _guided ? _layer.RegionAt(next) : 0;

                    if (_banded && !_route.InBand(region))
                        continue;

                    // Срезание углов запрещено: при зазоре в одну ячейку путь иначе
                    // протечёт по диагонали сквозь щель между двумя зданиями
                    if (dx != 0 && dy != 0 &&
                        (_grid.DistanceAt(cy * NavGrid.Width + nx) < required ||
                         _grid.DistanceAt(ny * NavGrid.Width + cx) < required))
                        continue;

                    Touch(next);

                    if (_closed[next])
                        continue;

                    int cost = _cost[at] + (dx != 0 && dy != 0 ? Diagonal : Straight);

                    if (_stamp[next] == _run && cost >= _cost[next])
                        continue;

                    _cost[next] = cost;
                    _from[next] = at;
                    _open.Push(next, cost + Guided(nx, ny, region, goal));
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Пометить запись прогоном. Запись с чужим номером считается пустой — так снимается
    /// очистка массивов перед каждым поиском.
    /// </summary>
    private void Touch(int index)
    {
        if (_stamp[index] == _run)
            return;

        _stamp[index] = _run;
        _cost[index] = int.MaxValue;
        _from[index] = -1;
        _closed[index] = false;
    }

    /// <summary>
    /// Оценка с поправкой по графу областей: не меньше расстояния от области ячейки
    /// до цели, посчитанного обратным поиском.
    ///
    /// ЗАЧЕМ. Октильная оценка о зданиях не знает, поэтому в тупике она тем меньше, чем
    /// глубже туда зашёл поиск, — отсюда и брались тысячи раскрытых узлов. Расстояние
    /// по графу у тупика велико, поскольку выйти из него можно только тем же путём,
    /// каким вошли, и поиск туда не сворачивает.
    ///
    /// Оценка перестаёт быть нижней границей, а значит, найденный путь не обязан быть
    /// кратчайшим. Это осознанный размен: на нашей карте разница в длине мала, а разница
    /// в расходе узлов — на порядок.
    /// </summary>
    private int Guided(int nx, int ny, int region, Vector2I goal)
    {
        int value = Estimate(new Vector2I(nx, ny), goal);

        if (!_guided || region == 0)
            return value;

        int distance = _route.Distance(region);

        if (distance == int.MaxValue)
            return value;

        // Расстояния графа хранятся в пикселях, оценка поиска — в десятых долях шага.
        // Добавка октильной оценки снимает плато: внутри одной области расстояние по графу
        // постоянно, и без добавки порядок раскрытия внутри неё определялся бы только
        // накопленной стоимостью, то есть вырождался бы в обход Дейкстры
        int scaled = distance * Straight / NavGrid.CellPx + value / 64;

        if (scaled <= value)
            return value;

        // Оценка ограничена сверху: неограниченная поправка в открытом поле переоценивает
        // остаток в полтора раза и заставляет поиск обходить широким веером
        int cap = value + value / GuidanceSlack;
        return scaled < cap ? scaled : cap;
    }

    /// <summary>
    /// Октильная оценка с разрешителем ничьих. Без разрешителя A* на открытом поле раскрывает
    /// широкий веер равноценных путей, а поля у нас много.
    /// </summary>
    private static int Estimate(Vector2I at, Vector2I goal)
    {
        int dx = Mathf.Abs(at.X - goal.X);
        int dy = Mathf.Abs(at.Y - goal.Y);

        int value = Straight * (dx + dy) + (Diagonal - 2 * Straight) * Mathf.Min(dx, dy);
        return value + value / 1000;
    }

    /// <summary>Развернуть цепочку предков в ломаную по центрам ячеек.</summary>
    private void Trace(Vector2I start, Vector2I goal, Vector2 from, Vector2 to)
    {
        _raw.Clear();

        for (int at = NavGrid.IndexOf(goal); at >= 0; at = _from[at])
        {
            _raw.Add(NavGrid.ToWorld(at));

            if (at == NavGrid.IndexOf(start))
                break;
        }

        _raw.Reverse();

        // Края уточняем настоящими координатами: центр стартовой ячейки — не то место,
        // где юнит стоит, а центр конечной — не то, куда его послали
        if (_raw.Count > 0)
        {
            _raw[0] = from;
            _raw[^1] = to;
        }
    }

    /// <summary>
    /// Протягивание прямой: от опорной точки идём вперёд, пока сохраняется видимость,
    /// и оставляем только точки перегиба. Сеточный аналог funnel-алгоритма на навмеше —
    /// тот же результат кратно меньшим объёмом кода.
    /// </summary>
    private void Smooth(float radiusPx, List<Vector2> result)
    {
        if (_raw.Count == 0)
            return;

        int anchor = 0;

        while (anchor < _raw.Count - 1)
        {
            int next = anchor + 1;

            for (int probe = _raw.Count - 1; probe > anchor + 1; probe--)
            {
                if (!ClearSight(_raw[anchor], _raw[probe], radiusPx))
                    continue;

                next = probe;
                break;
            }

            result.Add(_raw[next]);
            anchor = next;
        }
    }

    /// <summary>
    /// Видимость с учётом подхода снаружи растра: сегмент за пределами поля застройки
    /// считается свободным, проверка идёт только по отрезку внутри сетки.
    /// </summary>
    private bool ClearSight(Vector2 a, Vector2 b, float radiusPx)
    {
        var cellA = NavGrid.ToCell(a);
        var cellB = NavGrid.ToCell(b);
        bool aOut = !NavGrid.InBounds(cellA);
        bool bOut = !NavGrid.InBounds(cellB);

        if (aOut && bOut)
            return true;

        if (aOut)
            a = NavGrid.ToWorld(EntryCell(a, radiusPx));

        if (bOut)
            b = NavGrid.ToWorld(EntryCell(b, radiusPx));

        return _grid.LineOfSight(a, b, radiusPx);
    }

}
