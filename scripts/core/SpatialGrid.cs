using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Пространственная сетка: раскладка сущностей по клеткам ради вопроса «кто рядом».
///
/// ЗАЧЕМ. Поиск ближайшей цели перебирал всех живых противоположной стороны, а разведка —
/// всех носителей обзора. Стоимость такого перебора есть произведение численностей, и при
/// двух сотнях сущностей на выбор цели уходило свыше сорока процентов времени главного
/// потока. Сетка отвечает на тот же вопрос обходом окрестности вместо обхода всех.
///
/// РАСКЛАДКА ПЕРЕСОБИРАЕТСЯ ЦЕЛИКОМ И РАЗ ЗА ШАГ. Сущности движутся каждый кадр, и вести
/// раскладку изменениями означало бы снимать и класть заново почти всех — то есть ту же
/// работу, только с бухгалтерией. Полная пересборка стоит одного прохода по составу, что
/// на фоне запросов не заметно.
///
/// ЗАПРОС БЛИЖАЙШЕГО ИДЁТ КОЛЬЦАМИ, А НЕ КВАДРАТОМ. Обход квадратом со стороной по радиусу
/// поиска не даёт выигрыша там, где радиус велик: дальность ствола покрывает десятки клеток,
/// и в ответ пришла бы заметная часть карты. Кольцевой обход идёт от центра наружу и
/// прекращается, как только найденное ближе, чем может оказаться что-либо в неосмотренных
/// кольцах. Условие остановки не «нашли»: ближайший по клеткам не обязан быть ближайшим
/// по расстоянию, поэтому после находки осматривается ещё одно кольцо — ровно то, в котором
/// ещё может лежать что-то ближе.
///
/// СВОДКА ПО КЛЕТКЕ. Клетка помнит, чьи сущности в ней лежат, и запрос по стороне пропускает
/// чужие клетки целиком, не обходя содержимое. Вокруг стрелка почти все клетки заняты своими,
/// поэтому пропуск экономит больше, чем сам обход колец. Счётчика численности клетка не
/// держит: он выводится из длины списка и отдельного ответа ни на один вопрос не даёт.
///
/// ЧТО СЕТКА НЕ РЕШАЕТ. Вопрос «видит ли кто-нибудь эту точку»: радиус там принадлежит
/// не запросу, а каждому наблюдателю по отдельности, и обходить пришлось бы кольца
/// по наибольшему радиусу обзора среди всех. Такой вопрос решается растром видимости.
/// </summary>
public sealed class SpatialGrid<T> where T : class
{
    /// <summary>Содержимое одной клетки: сущности и сводка о том, чьи они.</summary>
    public sealed class Cell
    {
        public readonly List<T> Items = new();

        /// <summary>Стороны, чьи сущности лежат в клетке, битами по значению Faction.</summary>
        public int Sides;

        public void Clear()
        {
            Items.Clear();
            Sides = 0;
        }
    }

    /// <summary>
    /// Клетки полем, а не словарём.
    ///
    /// ЗАЧЕМ. Кольцевой обход опрашивает клетки подряд, включая пустые, и на каждую тратил
    /// хеширование пары целых плюс сравнение ключа: по замеру около трёх процентов
    /// собственного времени главного потока. Поле мира ограничено и невелико — при клетке
    /// в 128 пикселей вся арена укладывается в несколько тысяч клеток, — поэтому обращение
    /// сводится к вычислению номера и чтению массива.
    ///
    /// Пустые клетки хранятся как null: заводить объект под каждую клетку арены незачем,
    /// занята из них в бою дай бог сотня.
    /// </summary>
    private Cell[] _cells = System.Array.Empty<Cell>();

    private Vector2I _origin;
    private int _width;
    private int _height;

    /// <summary>Клетки, в которые в этой пересборке что-то положили: по ним идут очистка и показ.</summary>
    private readonly List<Cell> _filled = new();

    private readonly List<Vector2I> _filledAt = new();

    /// <summary>
    /// Границы занятых клеток ПО КАЖДОЙ СТОРОНЕ отдельно, а не по раскладке целиком.
    ///
    /// Общие границы для этого не годятся, и обошлось это дорого. Триста своих юнитов
    /// при полном отсутствии противника заставляли каждого из них обходить кольцами всю
    /// занятую область — своими же клетками и занятую, — чтобы не найти ничего: сорок пять
    /// тысяч осмотренных клеток за кадр при нуле просмотренных сущностей. По границам стороны
    /// такой обход прекращается сразу, а при пустой стороне не начинается вовсе.
    /// </summary>
    private readonly int[] _sideCount = new int[Sides];

    /// <summary>
    /// Состав каждой стороны списком. Нужен второму способу поиска — прямому перебору.
    ///
    /// ПОЧЕМУ СПОСОБОВ ДВА. Обход кольцами дешевле перебора, только пока искомых на карте
    /// много: он платит за каждую осмотренную клетку, а клеток в круге дальности ствола
    /// выходит порядка сотни. Когда противника осталось три десятка, перебрать их все дешевле,
    /// чем опросить сто клеток, большая часть которых к этой стороне отношения не имеет.
    /// Замер показал ровно этот случай: двести запросов осматривали двадцать пять тысяч клеток
    /// и находили в них полтора десятка целей.
    ///
    /// Поэтому способ выбирается сравнением объёмов работы, а не назначается заранее.
    /// Худший случай при таком выборе есть меньшее из двух, то есть лучше любого из способов
    /// по отдельности.
    /// </summary>
    private readonly List<T>[] _sideItems = new List<T>[Sides];

    private readonly Vector2I[] _sideMin = new Vector2I[Sides];
    private readonly Vector2I[] _sideMax = new Vector2I[Sides];

    /// <summary>
    /// Насколько далеко поверхность сущности отстоит от её середины — наибольшее по стороне.
    ///
    /// Нужно поиску досягаемых: предел там задан расстоянием до КРАЯ цели, а обход идёт
    /// по расстоянию до её середины, и разницу приходится закладывать в предел поиска.
    /// Прежде здесь стояла постоянная в пять клеток «на самую крупную постройку», отчего
    /// поиск ближайшего юнита расходился на пять колец там, где хватило бы одного.
    /// </summary>
    private readonly float[] _sideExtent = new float[Sides];

    /// <summary>Сколько сторон учитывается. Значение Faction служит номером в этих рядах.</summary>
    private const int Sides = 8;

    public SpatialGrid(int cellPx) => CellPx = Mathf.Max(1, cellPx);

    /// <summary>Сторона клетки в пикселях.</summary>
    public int CellPx { get; }

    /// <summary>Сколько сущностей разложено в последнюю пересборку.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Счётчики запросов за шаг. Ведутся всегда, а не по признаку отладки: сложение целого
    /// на фоне самого запроса не стоит ничего, а без них о работе сетки судить нечем —
    /// по одному лишь времени кадра не отличить удачный обход от обхода всей карты.
    /// </summary>
    public int Queries { get; private set; }

    /// <summary>Сколько клеток осмотрено запросами за шаг.</summary>
    public int Visited { get; private set; }

    /// <summary>Сколько сущностей просмотрено запросами за шаг — то, что раньше было полным обходом.</summary>
    public int Scanned { get; private set; }

    /// <summary>Обнулить счётчики запросов. Зовётся пересборкой, то есть раз за шаг.</summary>
    private void ResetCounters()
    {
        Queries = 0;
        Visited = 0;
        Scanned = 0;
    }

    /// <summary>Занятые клетки. Нужно отладочной отрисовке и ей одной.</summary>
    public IReadOnlyList<Vector2I> FilledAt => _filledAt;

    public Cell At(Vector2I cell)
    {
        int at = Offset(cell.X, cell.Y);
        return at < 0 ? null : _cells[at];
    }

    public void Clear()
    {
        Fit();

        // Списки чистим, но словарь не выбрасываем: раскладка пересобирается каждый шаг,
        // и заново выделять память под те же клетки было бы напрасной работой
        for (int i = 0; i < _filled.Count; i++)
            _filled[i].Clear();

        _filled.Clear();
        _filledAt.Clear();
        Count = 0;
        ResetCounters();

        for (int side = 0; side < Sides; side++)
        {
            _sideCount[side] = 0;
            _sideExtent[side] = 0f;
            _sideItems[side]?.Clear();
        }
    }

    /// <summary>Есть ли в раскладке хоть кто-нибудь этой стороны.</summary>
    public bool Has(Faction side) => _sideCount[(int)side] > 0;

    /// <summary>
    /// Наибольшее удаление поверхности от середины среди сущностей стороны. Ноль, если
    /// стороны в раскладке нет.
    /// </summary>
    public float ExtentOf(Faction side) => _sideExtent[(int)side];

    /// <summary>
    /// Положить сущность в клетку по её месту. <paramref name="extent"/> — насколько её
    /// поверхность отстоит от середины; по нему поиск досягаемых назначает предел обхода.
    /// </summary>
    public void Add(T item, Vector2 at, Faction side, float extent = 0f)
    {
        Put(ToCell(at), item, side, extent);
        Enlist(item, side);
        Count++;
    }

    /// <summary>
    /// Положить сущность во все клетки, которые задевает её прямоугольник.
    ///
    /// Постройка два на четыре занимает несколько клеток, и запись её в одну лишь клетку
    /// центра означала бы, что с дальнего края она не находится: кольцевой обход дошёл бы
    /// до края постройки раньше, чем до её середины, и остановился, ничего не увидев.
    /// </summary>
    public void AddArea(T item, in Obb area, Faction side)
    {
        var bounds = area.Bounds;
        var min = ToCell(bounds.Position);
        var max = ToCell(bounds.End);

        // Наибольшее удаление поверхности от середины у прямоугольника есть его полудиагональ
        float extent = area.Size.Length() * 0.5f;

        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
                Put(new Vector2I(x, y), item, side, extent);

        Enlist(item, side);
        Count++;
    }

    /// <summary>
    /// Ближайшая к точке сущность указанной стороны, не дальше maxDistance.
    ///
    /// Расстояние меряется до места сущности, которое отдаёт <paramref name="position"/>:
    /// сетка не знает, что такое положение у того, что в неё сложили. Поправка на габарит
    /// цели сюда не входит намеренно — она зависит от направления и стоит вычисления,
    /// а нужна лишь победителю; считает её вызывающий.
    ///
    /// <paramref name="accept"/> отсеивает негодных по причинам вызывающего. Отбор идёт
    /// при выборе, а не после него: отвергнутая позже цель оставила бы спрашивающего вовсе
    /// без цели, хотя рядом есть подходящая.
    ///
    /// РАВЕНСТВО РАЗРЕШАЕТСЯ ПО id. Порядок обхода у сетки свой, и при двух целях на равном
    /// расстоянии выбор без правила зависел бы от того, в каком порядке их разложили. Партия
    /// восстанавливается из журнала событий, поэтому такая зависимость недопустима.
    /// </summary>
    public T Nearest(Vector2 from, Faction side, float maxDistance,
        Func<T, Vector2> position, Func<T, bool> accept = null, Func<T, int> id = null)
    {
        // Стороны в раскладке нет вовсе — обходить нечего. Случай не редкий: между волнами
        // противника на карте не остаётся, а стрелки продолжают спрашивать цель каждый кадр
        var listed = _sideItems[(int)side];

        if (listed == null || listed.Count == 0)
            return null;

        Queries++;

        int mask = 1 << (int)side;
        var center = ToCell(from);

        // Дальше этого кольца искать негде: либо предел запроса, либо край клеток ЭТОЙ стороны
        var min = _sideMin[(int)side];
        var max = _sideMax[(int)side];

        int rings = Mathf.Max(
            Mathf.Max(center.X - min.X, max.X - center.X),
            Mathf.Max(center.Y - min.Y, max.Y - center.Y));

        if (rings < 0)
            return null;

        if (maxDistance < float.MaxValue)
            rings = Mathf.Min(rings, Mathf.CeilToInt(maxDistance / CellPx) + 1);

        float limit = maxDistance >= float.MaxValue
            ? float.MaxValue
            : maxDistance * maxDistance;

        T best = null;
        float bestDistance = limit;
        int bestId = int.MaxValue;

        // Способ выбирается сравнением объёмов работы: клеток в квадрате обхода против
        // численности стороны — см. пояснение у _sideItems
        long cells = (2L * rings + 1) * (2L * rings + 1);

        if (listed.Count <= cells)
        {
            Scanned += listed.Count;
            Scan(listed, from, position, accept, id, ref best, ref bestDistance, ref bestId);
            return best;
        }

        for (int ring = 0; ring <= rings; ring++)
        {
            // Всё, что лежит в неосмотренных кольцах, отстоит не ближе этой границы: от точки
            // внутри центральной клетки до любой клетки кольца ring+1 не может быть меньше
            // ring клеток. Отсюда и берётся запас в одно кольцо
            float reachable = (float)ring * CellPx;

            if (best != null && bestDistance <= reachable * reachable)
                break;

            Ring(center, ring, from, mask, position, accept, id,
                ref best, ref bestDistance, ref bestId);
        }

        return best;
    }

    /// <summary>
    /// Все сущности из клеток, которые задевает квадрат радиуса, в список вызывающего.
    /// Для запроса «кто рядом», где нужны все: расталкивание, обход соседей, урон по области.
    ///
    /// Отбор по самому расстоянию здесь не делается: у спрашивающего он свой — у одного
    /// по краю корпуса, у другого по чутью, у третьего по направлению движения, — и сетка
    /// выбирала бы за него.
    /// </summary>
    public void Collect(Vector2 from, float radius, List<T> into, T except = null)
    {
        into.Clear();

        if (Count == 0)
            return;

        Queries++;

        var min = ToCell(from - new Vector2(radius, radius));
        var max = ToCell(from + new Vector2(radius, radius));

        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
            {
                int offset = Offset(x, y);

                if (offset < 0 || _cells[offset] is not { } cell)
                    continue;

                var items = cell.Items;
                Visited++;
                Scanned += items.Count;

                for (int i = 0; i < items.Count; i++)
                    if (!ReferenceEquals(items[i], except))
                        into.Add(items[i]);
            }
    }

    public Vector2I ToCell(Vector2 at) => new(
        Mathf.FloorToInt(at.X / CellPx),
        Mathf.FloorToInt(at.Y / CellPx));

    /// <summary>
    /// Подогнать поле клеток под границы мира. Границы меняются только правкой настроек,
    /// поэтому сравнение размеров почти всегда отвечает «то же самое» и ничего не делает.
    ///
    /// Поле берётся с запасом в несколько клеток по каждой стороне: снаряд успевает вылететь
    /// за границу арены до того, как его срок выйдет, а сущность за границей должна попадать
    /// в раскладку, а не теряться.
    /// </summary>
    private void Fit()
    {
        const int Margin = 4;

        var bounds = World.ArenaBounds;
        var min = ToCell(bounds.Position) - new Vector2I(Margin, Margin);
        var max = ToCell(bounds.End) + new Vector2I(Margin, Margin);

        int width = max.X - min.X + 1;
        int height = max.Y - min.Y + 1;

        if (_origin == min && _width == width && _height == height)
            return;

        _origin = min;
        _width = Mathf.Max(1, width);
        _height = Mathf.Max(1, height);
        _cells = new Cell[_width * _height];

        // Прежние клетки выброшены вместе с массивом, а список занятых на них ссылается
        _filled.Clear();
        _filledAt.Clear();
    }

    /// <summary>Номер клетки в поле либо −1, если клетка лежит за его пределами.</summary>
    private int Offset(int x, int y)
    {
        x -= _origin.X;
        y -= _origin.Y;

        return x < 0 || y < 0 || x >= _width || y >= _height ? -1 : y * _width + x;
    }

    /// <summary>Мировой угол клетки — им пользуется отладочная отрисовка.</summary>
    public Vector2 Corner(Vector2I cell) => new(cell.X * CellPx, cell.Y * CellPx);

    private void Put(Vector2I at, T item, Faction side, float extent)
    {
        // Сущность за краем поля кладётся в ближайшую крайнюю клетку: терять её нельзя,
        // а погрешность в этом случае игры не касается — там никого нет
        int offset = Offset(
            Mathf.Clamp(at.X, _origin.X, _origin.X + _width - 1),
            Mathf.Clamp(at.Y, _origin.Y, _origin.Y + _height - 1));

        if (offset < 0)
            return;

        var cell = _cells[offset] ??= new Cell();

        if (cell.Items.Count == 0)
        {
            _filled.Add(cell);
            _filledAt.Add(at);
        }

        cell.Items.Add(item);
        cell.Sides |= 1 << (int)side;

        int number = (int)side;

        if (_sideCount[number]++ == 0)
        {
            _sideMin[number] = at;
            _sideMax[number] = at;
        }
        else
        {
            _sideMin[number] = new Vector2I(
                Mathf.Min(_sideMin[number].X, at.X), Mathf.Min(_sideMin[number].Y, at.Y));
            _sideMax[number] = new Vector2I(
                Mathf.Max(_sideMax[number].X, at.X), Mathf.Max(_sideMax[number].Y, at.Y));
        }

        if (extent > _sideExtent[number])
            _sideExtent[number] = extent;
    }

    /// <summary>Занести сущность в состав стороны — по одному разу, сколько бы клеток она ни заняла.</summary>
    private void Enlist(T item, Faction side)
    {
        int number = (int)side;

        _sideItems[number] ??= new List<T>();
        _sideItems[number].Add(item);
    }

    /// <summary>Обойти одно кольцо клеток вокруг центра — рамку толщиной в клетку.</summary>
    private void Ring(Vector2I center, int ring, Vector2 from, int mask,
        Func<T, Vector2> position, Func<T, bool> accept, Func<T, int> id,
        ref T best, ref float bestDistance, ref int bestId)
    {
        int minX = center.X - ring;
        int maxX = center.X + ring;
        int minY = center.Y - ring;
        int maxY = center.Y + ring;

        for (int y = minY; y <= maxY; y++)
        {
            // У внутренних строк кольца осматриваются только левый и правый края
            bool edge = y == minY || y == maxY;
            int step = edge || maxX == minX ? 1 : maxX - minX;

            for (int x = minX; x <= maxX; x += step)
            {
                int offset = Offset(x, y);

                if (offset < 0 || _cells[offset] is not { } cell)
                    continue;

                Visited++;

                // Клетка без сущностей нужной стороны пропускается целиком — ради этого
                // сводка и ведётся
                if ((cell.Sides & mask) == 0)
                    continue;

                Scanned += cell.Items.Count;

                Scan(cell.Items, from, position, accept, id,
                    ref best, ref bestDistance, ref bestId);
            }
        }
    }

    private static void Scan(List<T> items, Vector2 from,
        Func<T, Vector2> position, Func<T, bool> accept, Func<T, int> id,
        ref T best, ref float bestDistance, ref int bestId)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            float distance = from.DistanceSquaredTo(position(item));

            if (distance > bestDistance)
                continue;

            int number = id?.Invoke(item) ?? 0;

            // Равенство расстояний разрешается меньшим id — см. пояснение у Nearest
            if (distance == bestDistance && best != null && number >= bestId)
                continue;

            if (accept != null && !accept(item))
                continue;

            best = item;
            bestDistance = distance;
            bestId = number;
        }
    }
}
