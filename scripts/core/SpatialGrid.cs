using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Пространственная сетка: раскладка сущностей по корзинам ради вопроса «кто рядом».
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
/// поиска не даёт выигрыша там, где радиус велик: дальность ствола покрывает десятки корзин,
/// и в ответ пришла бы заметная часть карты. Кольцевой обход идёт от центра наружу и
/// прекращается, как только найденное ближе, чем может оказаться что-либо в неосмотренных
/// кольцах. Условие остановки не «нашли»: ближайший по корзинам не обязан быть ближайшим
/// по расстоянию, поэтому после находки осматривается ещё одно кольцо — ровно то, в котором
/// ещё может лежать что-то ближе.
///
/// СВОДКА ПО КЛЕТКЕ. Корзина помнит, чьи сущности в ней лежат, и запрос по стороне пропускает
/// чужие корзины целиком, не обходя содержимое. Вокруг стрелка почти все корзины заняты своими,
/// поэтому пропуск экономит больше, чем сам обход колец. Счётчика численности корзина не
/// держит: он выводится из длины списка и отдельного ответа ни на один вопрос не даёт.
///
/// ЧТО СЕТКА НЕ РЕШАЕТ. Вопрос «видит ли кто-нибудь эту точку»: радиус там принадлежит
/// не запросу, а каждому наблюдателю по отдельности, и обходить пришлось бы кольца
/// по наибольшему радиусу обзора среди всех. Такой вопрос решается растром видимости.
/// </summary>
public sealed class SpatialGrid<T> where T : class
{
    /// <summary>Содержимое одной корзины: сущности и сводка о том, чьи они.</summary>
    public sealed class Bucket
    {
        public readonly List<T> Items = new();

        /// <summary>Стороны, чьи сущности лежат в корзине, битами по значению Faction.</summary>
        public int Sides;

        public void Clear()
        {
            Items.Clear();
            Sides = 0;
        }
    }

    /// <summary>
    /// Корзины полем, а не словарём.
    ///
    /// ЗАЧЕМ. Кольцевой обход опрашивает корзины подряд, включая пустые, и на каждую тратил
    /// хеширование пары целых плюс сравнение ключа: по замеру около трёх процентов
    /// собственного времени главного потока. Поле мира ограничено и невелико — при корзине
    /// в 128 пикселей вся арена укладывается в несколько тысяч корзин, — поэтому обращение
    /// сводится к вычислению номера и чтению массива.
    ///
    /// Пустые корзины хранятся как null: заводить объект под каждую корзину арены незачем,
    /// занята из них в бою дай бог сотня.
    /// </summary>
    private Bucket[] _buckets = System.Array.Empty<Bucket>();

    private Vector2I _origin;
    private int _width;
    private int _height;

    /// <summary>Корзины, в которые в этой пересборке что-то положили: по ним идут очистка и показ.</summary>
    private readonly List<Bucket> _filled = new();

    private readonly List<Vector2I> _filledAt = new();

    /// <summary>
    /// Границы занятых корзин ПО КАЖДОЙ СТОРОНЕ отдельно, а не по раскладке целиком.
    ///
    /// Общие границы для этого не годятся, и обошлось это дорого. Триста своих юнитов
    /// при полном отсутствии противника заставляли каждого из них обходить кольцами всю
    /// занятую область — своими же корзинами и занятую, — чтобы не найти ничего: сорок пять
    /// тысяч осмотренных корзин за кадр при нуле просмотренных сущностей. По границам стороны
    /// такой обход прекращается сразу, а при пустой стороне не начинается вовсе.
    /// </summary>
    private readonly int[] _sideCount = new int[Sides];

    /// <summary>
    /// Состав каждой стороны списком. Нужен второму способу поиска — прямому перебору.
    ///
    /// ПОЧЕМУ СПОСОБОВ ДВА. Обход кольцами дешевле перебора, только пока искомых на карте
    /// много: он платит за каждую осмотренную корзину, а корзин в круге дальности ствола
    /// выходит порядка сотни. Когда противника осталось три десятка, перебрать их все дешевле,
    /// чем опросить сто корзин, большая часть которых к этой стороне отношения не имеет.
    /// Замер показал ровно этот случай: двести запросов осматривали двадцать пять тысяч корзин
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
    /// Прежде здесь стояла постоянная в пять корзин «на самую крупную постройку», отчего
    /// поиск ближайшего юнита расходился на пять колец там, где хватило бы одного.
    /// </summary>
    private readonly float[] _sideExtent = new float[Sides];

    /// <summary>Сколько сторон учитывается. Значение Faction служит номером в этих рядах.</summary>
    private const int Sides = 8;

    public SpatialGrid(int bucketPx) => BucketPx = Mathf.Max(1, bucketPx);

    /// <summary>Сторона корзины в пикселях.</summary>
    public int BucketPx { get; }

    /// <summary>Сколько сущностей разложено в последнюю пересборку.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Счётчики запросов за шаг. Ведутся всегда, а не по признаку отладки: сложение целого
    /// на фоне самого запроса не стоит ничего, а без них о работе сетки судить нечем —
    /// по одному лишь времени кадра не отличить удачный обход от обхода всей карты.
    /// </summary>
    public int Queries { get; private set; }

    /// <summary>Сколько корзин осмотрено запросами за шаг.</summary>
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

    /// <summary>Занятые корзины. Нужно отладочной отрисовке и ей одной.</summary>
    public IReadOnlyList<Vector2I> FilledAt => _filledAt;

    public Bucket At(Vector2I bucket)
    {
        int at = Offset(bucket.X, bucket.Y);
        return at < 0 ? null : _buckets[at];
    }

    public void Clear()
    {
        Fit();

        // Списки чистим, но словарь не выбрасываем: раскладка пересобирается каждый шаг,
        // и заново выделять память под те же корзины было бы напрасной работой
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
    /// Положить сущность в корзину по её месту. <paramref name="extent"/> — насколько её
    /// поверхность отстоит от середины; по нему поиск досягаемых назначает предел обхода.
    /// </summary>
    public void Add(T item, Vector2 at, Faction side, float extent = 0f)
    {
        Put(ToBucket(at), item, side, extent);
        Enlist(item, side);
        Count++;
    }

    /// <summary>
    /// Положить сущность во все корзины, которые задевает её прямоугольник.
    ///
    /// Постройка два на четыре занимает несколько корзин, и запись её в одну лишь корзину
    /// центра означала бы, что с дальнего края она не находится: кольцевой обход дошёл бы
    /// до края постройки раньше, чем до её середины, и остановился, ничего не увидев.
    /// </summary>
    public void AddArea(T item, in Obb area, Faction side)
    {
        var bounds = area.Bounds;
        var min = ToBucket(bounds.Position);
        var max = ToBucket(bounds.End);

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
        var center = ToBucket(from);

        // Дальше этого кольца искать негде: либо предел запроса, либо край корзин ЭТОЙ стороны
        var min = _sideMin[(int)side];
        var max = _sideMax[(int)side];

        int rings = Mathf.Max(
            Mathf.Max(center.X - min.X, max.X - center.X),
            Mathf.Max(center.Y - min.Y, max.Y - center.Y));

        if (rings < 0)
            return null;

        if (maxDistance < float.MaxValue)
            rings = Mathf.Min(rings, Mathf.CeilToInt(maxDistance / BucketPx) + 1);

        float limit = maxDistance >= float.MaxValue
            ? float.MaxValue
            : maxDistance * maxDistance;

        T best = null;
        float bestDistance = limit;
        int bestId = int.MaxValue;

        // Способ выбирается сравнением объёмов работы: корзин в квадрате обхода против
        // численности стороны — см. пояснение у _sideItems
        long buckets = (2L * rings + 1) * (2L * rings + 1);

        if (listed.Count <= buckets)
        {
            Scanned += listed.Count;
            Scan(listed, from, position, accept, id, ref best, ref bestDistance, ref bestId);
            return best;
        }

        for (int ring = 0; ring <= rings; ring++)
        {
            // Всё, что лежит в неосмотренных кольцах, отстоит не ближе этой границы: от точки
            // внутри центральной корзины до любой корзины кольца ring+1 не может быть меньше
            // ring корзин. Отсюда и берётся запас в одно кольцо
            float reachable = (float)ring * BucketPx;

            if (best != null && bestDistance <= reachable * reachable)
                break;

            Ring(center, ring, from, mask, position, accept, id,
                ref best, ref bestDistance, ref bestId);
        }

        return best;
    }

    /// <summary>
    /// Корзины, которые задевает квадрат радиуса, — без складывания их содержимого куда-либо.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНО ОТ <see cref="Collect"/>. Список нужен там, где одну выборку читают
    /// несколько правил по очереди. Когда правило одно или все они считаются за один проход,
    /// список есть чистый расход: копирование ссылок в массив с проверкой присваивания плюс
    /// повторное чтение тех же объектов из памяти. По замеру на одно это копирование уходило
    /// около двух процентов времени главного потока.
    ///
    /// Перечислитель объявлен структурой и роздан вручную, поэтому <c>foreach</c> по нему
    /// не создаёт объекта в куче: обход соседей идёт по разу на сущность каждый шаг,
    /// и мусор здесь стоил бы дороже самой работы.
    /// </summary>
    public Area Around(Vector2 from, float radius)
    {
        if (Count == 0)
            return default;

        Queries++;

        return new Area(this, ToBucket(from - new Vector2(radius, radius)),
            ToBucket(from + new Vector2(radius, radius)));
    }

    /// <summary>Прямоугольник корзин, отдающий их содержимое списками. См. <see cref="Around"/>.</summary>
    public readonly struct Area
    {
        private readonly SpatialGrid<T> _grid;
        private readonly Vector2I _min;
        private readonly Vector2I _max;

        internal Area(SpatialGrid<T> grid, Vector2I min, Vector2I max)
        {
            _grid = grid;
            _min = min;
            _max = max;
        }

        public Enumerator GetEnumerator() => new(_grid, _min, _max);

        public struct Enumerator
        {
            private readonly SpatialGrid<T> _grid;
            private readonly Vector2I _min;
            private readonly Vector2I _max;

            private int _x;
            private int _y;

            internal Enumerator(SpatialGrid<T> grid, Vector2I min, Vector2I max)
            {
                _grid = grid;
                _min = min;
                _max = max;
                _x = min.X - 1;
                _y = min.Y;
                Current = null;
            }

            public List<T> Current { get; private set; }

            public bool MoveNext()
            {
                if (_grid == null)
                    return false;

                while (true)
                {
                    if (++_x > _max.X)
                    {
                        _x = _min.X;

                        if (++_y > _max.Y)
                            return false;
                    }

                    int offset = _grid.Offset(_x, _y);

                    if (offset < 0 || _grid._buckets[offset] is not { } bucket)
                        continue;

                    _grid.Visited++;
                    _grid.Scanned += bucket.Items.Count;

                    Current = bucket.Items;
                    return true;
                }
            }
        }
    }

    /// <summary>
    /// Все сущности из корзин, которые задевает квадрат радиуса, в список вызывающего.
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

        var min = ToBucket(from - new Vector2(radius, radius));
        var max = ToBucket(from + new Vector2(radius, radius));

        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
            {
                int offset = Offset(x, y);

                if (offset < 0 || _buckets[offset] is not { } bucket)
                    continue;

                var items = bucket.Items;
                Visited++;
                Scanned += items.Count;

                for (int i = 0; i < items.Count; i++)
                    if (!ReferenceEquals(items[i], except))
                        into.Add(items[i]);
            }
    }

    public Vector2I ToBucket(Vector2 at) => new(
        Mathf.FloorToInt(at.X / BucketPx),
        Mathf.FloorToInt(at.Y / BucketPx));

    /// <summary>
    /// Подогнать поле корзин под границы мира. Границы меняются только правкой настроек,
    /// поэтому сравнение размеров почти всегда отвечает «то же самое» и ничего не делает.
    ///
    /// Поле берётся с запасом в несколько корзин по каждой стороне: снаряд успевает вылететь
    /// за границу арены до того, как его срок выйдет, а сущность за границей должна попадать
    /// в раскладку, а не теряться.
    /// </summary>
    private void Fit()
    {
        const int Margin = 4;

        var bounds = World.ArenaBounds;
        var min = ToBucket(bounds.Position) - new Vector2I(Margin, Margin);
        var max = ToBucket(bounds.End) + new Vector2I(Margin, Margin);

        int width = max.X - min.X + 1;
        int height = max.Y - min.Y + 1;

        if (_origin == min && _width == width && _height == height)
            return;

        _origin = min;
        _width = Mathf.Max(1, width);
        _height = Mathf.Max(1, height);
        _buckets = new Bucket[_width * _height];

        // Прежние корзины выброшены вместе с массивом, а список занятых на них ссылается
        _filled.Clear();
        _filledAt.Clear();
    }

    /// <summary>Номер корзины в поле либо −1, если корзина лежит за его пределами.</summary>
    private int Offset(int x, int y)
    {
        x -= _origin.X;
        y -= _origin.Y;

        return x < 0 || y < 0 || x >= _width || y >= _height ? -1 : y * _width + x;
    }

    /// <summary>Мировой угол корзины — им пользуется отладочная отрисовка.</summary>
    public Vector2 Corner(Vector2I bucket) => new(bucket.X * BucketPx, bucket.Y * BucketPx);

    private void Put(Vector2I at, T item, Faction side, float extent)
    {
        // Сущность за краем поля кладётся в ближайшую крайнюю корзину: терять её нельзя,
        // а погрешность в этом случае игры не касается — там никого нет
        int offset = Offset(
            Mathf.Clamp(at.X, _origin.X, _origin.X + _width - 1),
            Mathf.Clamp(at.Y, _origin.Y, _origin.Y + _height - 1));

        if (offset < 0)
            return;

        var bucket = _buckets[offset] ??= new Bucket();

        if (bucket.Items.Count == 0)
        {
            _filled.Add(bucket);
            _filledAt.Add(at);
        }

        bucket.Items.Add(item);
        bucket.Sides |= 1 << (int)side;

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

    /// <summary>Занести сущность в состав стороны — по одному разу, сколько бы корзин она ни заняла.</summary>
    private void Enlist(T item, Faction side)
    {
        int number = (int)side;

        _sideItems[number] ??= new List<T>();
        _sideItems[number].Add(item);
    }

    /// <summary>Обойти одно кольцо корзин вокруг центра — рамку толщиной в корзину.</summary>
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

                if (offset < 0 || _buckets[offset] is not { } bucket)
                    continue;

                Visited++;

                // Корзина без сущностей нужной стороны пропускается целиком — ради этого
                // сводка и ведётся
                if ((bucket.Sides & mask) == 0)
                    continue;

                Scanned += bucket.Items.Count;

                Scan(bucket.Items, from, position, accept, id,
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
