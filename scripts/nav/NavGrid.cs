using System.Collections.Generic;
using Godot;

/// <summary>
/// Растровая карта навигации: непроходимость, клиренс и связность.
///
/// НЕ СИСТЕМА И НЕ ПРОЕКЦИЯ. Проекции впитывают документы из журнала, а навигация выводится
/// из положений живых сущностей — то есть из <see cref="ObstacleMap"/>. Поэтому карта просто
/// объект на композиционном корне, а пересобирается лениво: перед любым запросом сверяется
/// ревизия источника, и работа делается один раз на изменение, а не раз в кадр.
///
/// КЛИРЕНС ВМЕСТО ШИРИНЫ КОРИДОРА. Задача юнитов разного размера решается одним сравнением:
/// ячейка годится, если расстояние до ближайшего препятствия не меньше радиуса. На навмеше
/// то же самое требует вычисления ширины треугольника и построения расширенных вершин.
/// Это и есть главный довод в пользу растра для этого проекта.
///
/// РАССТОЯНИЕ СЧИТАЕТСЯ ЧЕМФЕРОМ 3-4, в третях ячейки: шаг по стороне стоит 3, по диагонали 4.
/// Приближение к евклидову расстоянию всегда снизу (4/3 меньше корня из двух), поэтому
/// ошибка идёт в сторону осторожности: коридор скорее сочтётся узким, чем широким.
/// </summary>
public sealed class NavGrid
{
    public const int Cell = Const.NavCell;

    /// <summary>
    /// Ячеек по стороне. Величина перестала быть константой вместе с тем, как размер мира
    /// переехал в настройки: растр покрывает поле целиком, поэтому его сторона есть
    /// следствие размера поля, а не самостоятельное число.
    ///
    /// Массивы под неё выделяются при создании карты и пересоздаются, если размер поля
    /// изменился, — см. <see cref="Fit"/>. По ходу партии этого не происходит: настройки
    /// правятся между партиями и в редакторе.
    /// </summary>
    public static int Width => World.NavWidth;

    public static int Area => Width * Width;

    /// <summary>Расстояние в третях ячейки: шаг по стороне.</summary>
    private const int Straight = 3;

    /// <summary>Расстояние в третях ячейки: шаг по диагонали.</summary>
    private const int Diagonal = 4;

    /// <summary>Заведомо больше любого расстояния на этой карте: 164 ячейки по 4 трети.</summary>
    private const int Far = 30000;

    private readonly ObstacleMap _obstacles;

    private bool[] _blocked = new bool[Area];

    /// <summary>Расстояние до ближайшей непроходимой ячейки, в третях ячейки.</summary>
    private int[] _distance = new int[Area];

    /// <summary>
    /// Метки связных областей, своя раскладка на каждый порог клиренса. Считаются лениво:
    /// разных радиусов у сущностей мало, и заводить раскладку под неспрошенный порог незачем.
    /// </summary>
    private readonly Dictionary<int, int[]> _components = new();

    private readonly Queue<int> _flood = new();

    private int _sourceRevision = -1;

    /// <summary>Сколько раз карта пересобиралась. По нему устаревают кешированные пути.</summary>
    public int Revision { get; private set; }

    /// <summary>Сколько заняла последняя пересборка, миллисекунд. Показывает панель отладки.</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Прямоугольник последнего изменения источника.</summary>
    public Rect2 LastChange => _obstacles.LastChange;

    public NavGrid(ObstacleMap obstacles) => _obstacles = obstacles;

    // ── координаты ────────────────────────────────────────────────────────────────

    public static Vector2I ToCell(Vector2 world) => new(
        Mathf.FloorToInt((world.X - World.Min.X) / Cell),
        Mathf.FloorToInt((world.Y - World.Min.Y) / Cell));

    public static Vector2 ToWorld(Vector2I cell) =>
        World.Min + new Vector2(cell.X + 0.5f, cell.Y + 0.5f) * Cell;

    public static Vector2 ToWorld(int index) =>
        ToWorld(new Vector2I(index % Width, index / Width));

    public static bool InBounds(Vector2I cell) =>
        cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Width;

    public static int IndexOf(Vector2I cell) => cell.Y * Width + cell.X;

    // ── запросы ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Какое расстояние в третях ячейки требуется, чтобы поместился радиус.
    ///
    /// Половина ячейки вычитается потому, что расстояние меряется до ЦЕНТРА непроходимой
    /// ячейки, а препятствие занимает её целиком: ближняя граница на полклетки ближе центра.
    /// </summary>
    public static int Required(float radiusPx) =>
        Mathf.Max(1, Mathf.CeilToInt((radiusPx / Cell + 0.5f) * Straight));

    public bool Blocked(Vector2I cell)
    {
        Fresh();
        return !InBounds(cell) || _blocked[IndexOf(cell)];
    }

    /// <summary>Сколько свободного места вокруг центра ячейки, пикселей.</summary>
    public float FreeRadius(Vector2I cell)
    {
        Fresh();

        if (!InBounds(cell))
            return 0f;

        return (_distance[IndexOf(cell)] / (float)Straight - 0.5f) * Cell;
    }

    public bool Passable(Vector2I cell, float radiusPx)
    {
        Fresh();
        return InBounds(cell) && _distance[IndexOf(cell)] >= Required(radiusPx);
    }

    public bool Passable(Vector2 world, float radiusPx) => Passable(ToCell(world), radiusPx);

    /// <summary>
    /// Лежат ли точки в одной связной области. Отсекает недостижимую цель ДО поиска:
    /// иначе запрос к запертой точке разворачивает A* на всё поле и стоит максимума
    /// из возможного. При свободной постановке игрок запирает области регулярно.
    /// </summary>
    public bool Connected(Vector2I a, Vector2I b, float radiusPx)
    {
        Fresh();

        if (!InBounds(a) || !InBounds(b))
            return false;

        var labels = Components(Required(radiusPx));
        int first = labels[IndexOf(a)];

        return first != 0 && first == labels[IndexOf(b)];
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

            if (!InBounds(cell) || _distance[IndexOf(cell)] < required)
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
    /// Поиск идёт кольцами наружу и ограничен: за пределами возвращается сама ячейка.
    /// </summary>
    public Vector2I NearestPassable(Vector2I cell, float radiusPx, int maxRings = 24)
    {
        Fresh();

        if (Passable(cell, radiusPx))
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
        return Passable(found, radiusPx);
    }

    // ── пересборка ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Сверить ревизию источника и пересобрать, если он менялся. Зовётся из каждого запроса:
    /// так карта не может ответить по устаревшему состоянию, и следить за этим никому не надо.
    /// </summary>
    public void Fresh()
    {
        if (_sourceRevision == _obstacles.Revision && _blocked.Length == Area)
            return;

        Fit();
        Rebuild();
    }

    /// <summary>
    /// Подогнать массивы под текущий размер поля.
    ///
    /// Нужно потому, что размер мира стал настройкой: карта создаётся раньше, чем сцена
    /// успевает назначить настройки, и в редакторе поле правят прямо во время работы.
    /// Проверка стоит одно сравнение длины на запрос, а без неё изменённый размер означал бы
    /// обращение за границы массива.
    /// </summary>
    private void Fit()
    {
        if (_blocked.Length == Area)
            return;

        _blocked = new bool[Area];
        _distance = new int[Area];
        _components.Clear();
    }

    /// <summary>
    /// Полная пересборка. Инкрементальность сознательно не делается: поле в 27 тысяч ячеек
    /// пересчитывается за доли миллисекунды, а меняется несколько раз в секунду в самом
    /// плотном случае. Инкрементальный пересчёт по области требует аккуратной обработки
    /// границы и даёт выигрыш, которого никто не заметит.
    /// </summary>
    private void Rebuild()
    {
        ulong started = Time.GetTicksUsec();

        _sourceRevision = _obstacles.Revision;
        _components.Clear();

        System.Array.Clear(_blocked);

        foreach (var obstacle in _obstacles.All)
            Rasterize(_obstacles.ShapeOf(obstacle));

        Chamfer();

        Revision++;
        LastBuildMs = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>
    /// Растеризация консервативная: ячейка, задетая прямоугольником хотя бы краем,
    /// помечается целиком. Ошибка идёт в пользу безопасности — юнит не пройдёт там,
    /// где формально помещался бы на пару пикселей.
    ///
    /// Обход идёт по осепараллельным границам, а помечается ячейка по пересечению с самим
    /// прямоугольником: у повёрнутого границы прихватывают углы, где его нет вовсе,
    /// и без второй проверки диагональная стена перегораживала бы вдвое больше места.
    /// </summary>
    private void Rasterize(in Obb shape)
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

        // Осепараллельному хватает и обхода: пересечение с ячейкой у него заведомо есть,
        // а проверять его заново значило бы платить за поворот там, где поворота нет
        bool square = Mathf.IsZeroApprox(Mathf.Sin(shape.Angle * 2f));

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!square && !shape.Intersects(CellShape(x, y)))
                    continue;

                _blocked[y * Width + x] = true;
            }
        }
    }

    /// <summary>Ячейка растра как прямоугольник мира — для точной проверки пересечения.</summary>
    private static Obb CellShape(int x, int y) => Obb.FromRect(new Rect2(
        World.Min + new Vector2(x, y) * Cell,
        new Vector2(Cell, Cell)));

    /// <summary>
    /// Расстояние до ближайшего препятствия за два прохода. Соседи за краем мира считаются
    /// непроходимыми: граница карты и есть стена, и юнит не должен планировать путь наружу.
    /// </summary>
    private void Chamfer()
    {
        for (int i = 0; i < Area; i++)
            _distance[i] = _blocked[i] ? 0 : Far;

        for (int y = 0; y < Width; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                if (_distance[i] == 0)
                    continue;

                int best = _distance[i];
                best = Mathf.Min(best, Read(x - 1, y) + Straight);
                best = Mathf.Min(best, Read(x, y - 1) + Straight);
                best = Mathf.Min(best, Read(x - 1, y - 1) + Diagonal);
                best = Mathf.Min(best, Read(x + 1, y - 1) + Diagonal);
                _distance[i] = best;
            }
        }

        for (int y = Width - 1; y >= 0; y--)
        {
            for (int x = Width - 1; x >= 0; x--)
            {
                int i = y * Width + x;
                if (_distance[i] == 0)
                    continue;

                int best = _distance[i];
                best = Mathf.Min(best, Read(x + 1, y) + Straight);
                best = Mathf.Min(best, Read(x, y + 1) + Straight);
                best = Mathf.Min(best, Read(x + 1, y + 1) + Diagonal);
                best = Mathf.Min(best, Read(x - 1, y + 1) + Diagonal);
                _distance[i] = best;
            }
        }
    }

    /// <summary>Расстояние соседа. За краем мира — ноль, то есть стена.</summary>
    private int Read(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Width ? 0 : _distance[y * Width + x];

    /// <summary>
    /// Раскладка связных областей под порог клиренса. Заливка в ширину, метки с единицы:
    /// ноль означает «ячейка не годится под этот порог».
    /// </summary>
    private int[] Components(int required)
    {
        if (_components.TryGetValue(required, out var cached))
            return cached;

        var labels = new int[Area];
        int label = 0;

        for (int start = 0; start < Area; start++)
        {
            if (labels[start] != 0 || _distance[start] < required)
                continue;

            label++;
            labels[start] = label;
            _flood.Clear();
            _flood.Enqueue(start);

            while (_flood.Count > 0)
            {
                int at = _flood.Dequeue();
                int cx = at % Width;
                int cy = at / Width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int nx = cx + dx;
                        int ny = cy + dy;

                        if (nx < 0 || ny < 0 || nx >= Width || ny >= Width)
                            continue;

                        // Диагональ без срезания угла: иначе связность нашлась бы
                        // там, где путь протекает между двумя углами зданий
                        if (dx != 0 && dy != 0 &&
                            (_distance[cy * Width + nx] < required ||
                             _distance[ny * Width + cx] < required))
                            continue;

                        int next = ny * Width + nx;

                        if (labels[next] != 0 || _distance[next] < required)
                            continue;

                        labels[next] = label;
                        _flood.Enqueue(next);
                    }
                }
            }
        }

        _components[required] = labels;
        return labels;
    }

    // ── чтение для отрисовки ──────────────────────────────────────────────────────

    /// <summary>Непроходимость по номеру ячейки. Отрисовщику нужен весь массив разом.</summary>
    public bool BlockedAt(int index)
    {
        Fresh();
        return _blocked[index];
    }

    public int DistanceAt(int index)
    {
        Fresh();
        return _distance[index];
    }

    /// <summary>Метка связной области для отрисовки. Порог берётся у типового юнита.</summary>
    public int ComponentAt(int index, float radiusPx)
    {
        Fresh();
        return Components(Required(radiusPx))[index];
    }
}
