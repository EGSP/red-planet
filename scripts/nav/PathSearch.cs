using System.Collections.Generic;
using Godot;

/// <summary>
/// Поиск пути A* по растру навигации и сглаживание найденного.
///
/// ЧИСТЫЙ ВЫЧИСЛИТЕЛЬ. Об игре не знает ничего: на входе две точки и радиус, на выходе
/// ломаная. Кеш, бюджет и всё, что связано с тем, кто и зачем спросил, живут
/// в <see cref="PathfindingSystem"/>.
///
/// СЛУЖЕБНЫЕ МАССИВЫ ПЕРЕИСПОЛЬЗУЮТСЯ. Чистить 27 тысяч ячеек перед каждым поиском незачем:
/// вместо этого у каждой записи стоит номер прогона, и запись с чужим номером считается
/// пустой. Отсюда требование: один экземпляр — один поток и никакой вложенности вызовов.
/// </summary>
public sealed class PathSearch
{
    /// <summary>Стоимость шага по стороне. Целые числа, чтобы не копить ошибку сложения.</summary>
    private const int Straight = 10;

    private const int Diagonal = 14;

    private readonly NavGrid _grid;

    private readonly int[] _cost = new int[NavGrid.Area];
    private readonly int[] _from = new int[NavGrid.Area];
    private readonly int[] _stamp = new int[NavGrid.Area];
    private readonly bool[] _closed = new bool[NavGrid.Area];

    private readonly Heap _open = new(1024);

    private readonly List<Vector2> _raw = new();

    private int _run;

    /// <summary>Сколько узлов раскрыл последний поиск. Показывает панель отладки.</summary>
    public int LastExpanded { get; private set; }

    /// <summary>
    /// Раскрытые узлы последнего поиска — только для отрисовки, и только когда её просят:
    /// список стоит памяти и заполнения, а нужен раз в сессию при разборе странного пути.
    /// </summary>
    public List<Vector2> Expanded { get; } = new();

    public bool RecordExpanded { get; set; }

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

        if (RecordExpanded)
            Expanded.Clear();

        _grid.Fresh();

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

        if (!Search(start, goal, radiusPx, maxNodes))
            return false;

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

    private bool Search(Vector2I start, Vector2I goal, float radiusPx, int maxNodes)
    {
        _run++;
        _open.Clear();

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
                return false;

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
                    _open.Push(next, cost + Estimate(new Vector2I(nx, ny), goal));
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

    /// <summary>
    /// Двоичная куча минимума. Своя, потому что System.Collections.Generic.PriorityQueue
    /// не даёт переиспользовать хранилище между поисками, а поисков за кадр несколько.
    /// </summary>
    private sealed class Heap
    {
        private int[] _items;
        private int[] _keys;

        public int Count { get; private set; }

        public Heap(int capacity)
        {
            _items = new int[capacity];
            _keys = new int[capacity];
        }

        public void Clear() => Count = 0;

        public void Push(int item, int key)
        {
            if (Count == _items.Length)
            {
                System.Array.Resize(ref _items, Count * 2);
                System.Array.Resize(ref _keys, Count * 2);
            }

            int at = Count++;
            _items[at] = item;
            _keys[at] = key;

            while (at > 0)
            {
                int parent = (at - 1) / 2;

                if (_keys[parent] <= _keys[at])
                    break;

                Swap(parent, at);
                at = parent;
            }
        }

        public int Pop()
        {
            int top = _items[0];

            Count--;
            _items[0] = _items[Count];
            _keys[0] = _keys[Count];

            int at = 0;

            while (true)
            {
                int left = at * 2 + 1;
                int right = left + 1;
                int least = at;

                if (left < Count && _keys[left] < _keys[least])
                    least = left;

                if (right < Count && _keys[right] < _keys[least])
                    least = right;

                if (least == at)
                    break;

                Swap(least, at);
                at = least;
            }

            return top;
        }

        private void Swap(int a, int b)
        {
            (_items[a], _items[b]) = (_items[b], _items[a]);
            (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
        }
    }
}
