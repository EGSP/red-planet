using System;
using System.Collections.Generic;

/// <summary>
/// Множество объектов и разрезы над ним.
///
/// ЗАЧЕМ. Системе почти всегда нужен не один объект, а все объекты определённого рода:
/// все вооружённые, все участники экономики, все каркасы. Раньше этот вопрос задавали
/// движку через группы нод, и у такого способа три беды: ключом была строка (её не
/// проверяет компилятор и не задевает переименование), ответом — нетипизированный массив
/// (каждая система приводила и отсеивала его сама), а порядок брался из дерева сцены
/// и плыл при удалениях.
///
/// Здесь ключ разреза — тип-признак, обычно интерфейс: <c>All&lt;IDamageable&gt;()</c>.
/// Отсюда сразу множественное членство — сущность попадает в столько разрезов, сколько
/// интерфейсов реализует, — и стабильный порядок добавления.
///
/// О НОДАХ НЕ ЗНАЕТ. Складывать можно любой объект, не только Node: разрезы одинаково
/// нужны и живым сущностям, и обычным данным. Единственное, что индекс знает про движок, —
/// как проверить живость ноды, и это ровно одно место на весь проект.
///
/// СОСТАВ МЕНЯЕТСЯ НА ГРАНИЦЕ КАДРА. Добавление и снятие копятся и применяются в Sweep,
/// который зовёт GameManager после всех систем. Иначе рождённое посреди кадра попадало бы
/// в одни обходы и не попадало в другие — в зависимости от порядка систем. Заодно снимается
/// вечная беда обхода: список не меняется у идущей по нему системы под ногами.
///
/// ЖИВОСТЬ ОБЪЯВЛЯЕТ САМ ОБЪЕКТ. У ноды признак актуальности берётся из движка: освобождённая
/// или помеченная QueueFree нода перестаёт попадать в разрезы без единого вызова. Всё
/// остальное объявляет своё условие при добавлении — <see cref="Add(object, Func{bool})"/>
/// принимает предикат, по которому индекс сам решает, что объект вышел из игры. Это и есть
/// замена ручному снятию: тот, кто кладёт объект в индекс, обязан сказать, как узнать о его
/// смерти, а не помнить о вызове Remove в каждой ветке кода. Remove остаётся для случаев,
/// когда момент снятия известен точно и предикат заводить незачем.
///
/// ПОДПИСКИ. <see cref="Watch{T}"/> сообщает читателю о входе и выходе объектов из разреза,
/// поэтому производное состояние — счётчики, метрики, собственные раскладки — пересчитывается
/// по изменению, а не полным обходом каждый кадр. Тем, кому хватает признака «состав менялся»,
/// служат <see cref="Revision"/> и <see cref="RevisionOf{T}"/>.
/// </summary>
public sealed class Index
{
    /// <summary>Все объекты множества — из него добираются заново заведённые разрезы.</summary>
    private readonly List<object> _items = new();

    /// <summary>
    /// Членство для проверки O(1). Нужно не только читателю: без него повторное добавление
    /// одного объекта дало бы два вхождения, и системы обошли бы его дважды.
    /// </summary>
    private readonly HashSet<object> _members = new(ByReference.Instance);

    private readonly List<object> _pending = new();

    /// <summary>Снятые вручную. Сравниваем по ссылке, чтобы не звать Equals у чужих объектов.</summary>
    private readonly HashSet<object> _retired = new(ByReference.Instance);

    /// <summary>
    /// Объявленные условия живости — только для тех, кто их передал. Ноды сюда не попадают:
    /// платить словарём за тысячи снарядов ради ответа, который и так знает движок, незачем.
    /// </summary>
    private readonly Dictionary<object, Func<bool>> _live = new(ByReference.Instance);

    private readonly Dictionary<Type, IBucket> _buckets = new();

    /// <summary>
    /// Тот же состав, что и в словаре, но списком: по нему идут обходы уборки и рассылки.
    /// Обработчик подписки вправе спросить новый разрез, а это заводит разрез и меняет
    /// словарь — обход по списку с индексом такое переживает, обход словаря нет.
    /// </summary>
    private readonly List<IBucket> _bucketList = new();

    private readonly List<IKeySlice> _keySlices = new();

    /// <summary>
    /// Сколько раз менялся состав множества. Читателю, которому не нужны подписки, довольно
    /// сверить это число со своим: не изменилось — производное состояние пересчитывать не надо.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Объект войдёт в множество на границе кадра.</summary>
    public void Add(object item) => Add(item, null);

    /// <summary>
    /// Объект с объявленным условием актуальности: пока предикат отвечает true, объект
    /// в разрезах, ответил false — выметается ближайшей уборкой, из всех разрезов сразу.
    ///
    /// Предикат — это и есть тот самый признак конца жизни для всего, о чьей смерти движок
    /// не знает: снятая подписка, закрытый заказ, разряженный источник. Он спрашивается на
    /// каждой проверке живости, поэтому должен быть дешёвым и не иметь побочных действий.
    /// Ноде предикат тоже можно задать — тогда он главнее признаков движка.
    /// </summary>
    public void Add(object item, Func<bool> live)
    {
        if (item == null)
            return;

        _pending.Add(item);

        if (live != null)
            _live[item] = live;
    }

    /// <summary>
    /// Убрать объект из множества. Нодам не нужно: их уход виден по самому движку.
    /// Снятие, как и добавление, применяется в конце кадра.
    /// </summary>
    public void Remove(object item)
    {
        if (item != null)
            _retired.Add(item);
    }

    /// <summary>Объект уже в множестве. Ждущие входа в счёт не идут — они войдут в Sweep.</summary>
    public bool Contains(object item) => item != null && _members.Contains(item);

    /// <summary>Все, кто подходит под признак. Разрез заводится при первом спросе.</summary>
    public Slice<T> All<T>() where T : class => new(BucketOf<T>().Items, this);

    /// <summary>Сколько раз менялся состав именно этого разреза.</summary>
    public int RevisionOf<T>() where T : class => BucketOf<T>().Revision;

    /// <summary>
    /// Подписка на состав разреза: <c>added</c> зовётся, когда объект вошёл, <c>removed</c> —
    /// когда вышел, по любой причине.
    ///
    /// ЗАЧЕМ. Производное от индекса состояние — численность стороны, суммарная мощность,
    /// собственная раскладка читателя — иначе пересчитывается полным обходом на каждый спрос,
    /// хотя менялось оно последний раз десять кадров назад. Подписка переводит такой счёт
    /// на изменения и заодно избавляет от вопроса «а не устарело ли»: устареть между
    /// уведомлениями оно не может.
    ///
    /// КОГДА ЗОВЁТСЯ. Текущий состав проигрывается через <c>added</c> прямо при подписке —
    /// поэтому читатель пишется одним способом и стартового обхода ему не нужно. Дальше
    /// уведомления приходят в конце кадра, из Sweep, когда состав уже согласован: изнутри
    /// обработчика индекс можно читать, и он ответит новым составом. Рождённое и погибшее
    /// в пределах одного кадра не порождает пары событий — такой объект в множество не входил.
    ///
    /// ЧТО МОЖНО С ВЫШЕДШИМ ОБЪЕКТОМ. Только опознать: сравнить по ссылке, вычесть из своих
    /// структур. Читать его поля нельзя — к этому мигу нода обычно уже освобождена движком.
    ///
    /// ОТПИСКА. Dispose у возвращённого значения. Если подписчик живёт меньше индекса, лучше
    /// передать его как <c>owner</c>: подписку мёртвого владельца индекс снимает сам, по тому
    /// же признаку живости, что и своих объектов.
    /// </summary>
    public IndexWatch Watch<T>(Action<T> added, Action<T> removed = null, object owner = null)
        where T : class => BucketOf<T>().Watch(added, removed, owner);

    /// <summary>
    /// Постоянный разрез по ключу: цели по сторонам, сущности по клетке сетки.
    ///
    /// Единственный метод, который что-то ЗАВОДИТ помимо разреза по признаку. Зовётся один
    /// раз — обычно в композиционном корне, рядом с проекциями, — и дальше разрез живёт сам,
    /// пересобираясь не чаще раза за кадр. Поэтому ключ должен быть таким, который посреди
    /// кадра не меняется; для мигающих значений это не подходит, там нужен фильтр разреза.
    /// </summary>
    public KeySlice<TKey, T> SliceBy<T, TKey>(Func<T, TKey> key) where T : class
    {
        var slice = new KeySlice<TKey, T>(this, key);
        _keySlices.Add(slice);
        return slice;
    }

    /// <summary>
    /// Конец кадра: впустить новых, вымести мёртвых, пометить разрезы по ключу устаревшими
    /// и разослать изменения подписчикам. Единственное место, где состав множества меняется.
    ///
    /// Порядок внутри важен дважды. Разрезы чистятся ДО общего списка: уборка общего списка
    /// забывает объявленные условия живости, а разрезам они ещё нужны, чтобы признать объект
    /// мёртвым. Рассылка идёт ПОСЛЕ всей уборки: обработчик вправе читать индекс, и увидеть
    /// он должен готовое состояние, а не половину его.
    /// </summary>
    public void Sweep()
    {
        foreach (var item in _pending)
        {
            // Родился и погиб внутри одного кадра — в множество не входит вовсе
            if (!IsLive(item))
            {
                _live.Remove(item);
                continue;
            }

            // Второе добавление того же объекта не должно удваивать его в обходах
            if (!_members.Add(item))
                continue;

            _items.Add(item);
            Revision++;

            for (int i = 0; i < _bucketList.Count; i++)
                _bucketList[i].TryAdd(item);
        }

        _pending.Clear();

        for (int i = 0; i < _bucketList.Count; i++)
            _bucketList[i].Sweep();

        Compact();

        // Только после выметания: до него снятые ещё должны считаться мёртвыми
        _retired.Clear();

        for (int i = 0; i < _keySlices.Count; i++)
            _keySlices[i].Invalidate();

        for (int i = 0; i < _bucketList.Count; i++)
            _bucketList[i].Notify();
    }

    public int Count => _items.Count;

    /// <summary>
    /// Объект ещё в игре. Единственное место во всём проекте, где живость проверяется руками:
    /// снятый мёртв всегда, объявивший условие — по своему условию, нода — пока цела
    /// и не помечена на удаление, всё прочее — пока его не сняли.
    /// </summary>
    internal bool IsLive(object item)
    {
        if (item == null || _retired.Contains(item))
            return false;

        if (_live.TryGetValue(item, out var live))
            return live();

        if (item is not Godot.GodotObject obj)
            return true;

        return Alive.Is(obj) && (obj is not Godot.Node node || !node.IsQueuedForDeletion());
    }

    private Bucket<T> BucketOf<T>() where T : class
    {
        if (_buckets.TryGetValue(typeof(T), out var existing))
            return (Bucket<T>)existing;

        // Признак спросили впервые — набираем разрез из тех, кто уже в множестве.
        // Событиями это не считается: подписчика у нового разреза ещё нет, а тот,
        // кто подпишется, получит текущий состав при самой подписке
        var bucket = new Bucket<T>(this);

        foreach (var item in _items)
            bucket.Fill(item);

        _buckets[typeof(T)] = bucket;
        _bucketList.Add(bucket);
        return bucket;
    }

    /// <summary>
    /// Выбросить мёртвых из общего списка, сохранив порядок остальных. Вместе с объектом
    /// уходит и его объявленное условие живости — иначе словарь рос бы вечно.
    /// </summary>
    private void Compact()
    {
        int write = 0;

        for (int read = 0; read < _items.Count; read++)
        {
            var item = _items[read];

            if (IsLive(item))
            {
                _items[write++] = item;
                continue;
            }

            _members.Remove(item);
            _live.Remove(item);
            Revision++;
        }

        if (write < _items.Count)
            _items.RemoveRange(write, _items.Count - write);
    }

    private interface IBucket
    {
        void TryAdd(object item);
        void Sweep();
        void Notify();
    }

    /// <summary>
    /// Разрез по одному признаку. Проверка «подходит ли» — обычное приведение типа,
    /// поэтому раскладка ничего не знает ни про наследование, ни про интерфейсы отдельно.
    /// </summary>
    private sealed class Bucket<T> : IBucket where T : class
    {
        private readonly Index _owner;

        public readonly List<T> Items = new();

        private readonly List<Watcher<T>> _watchers = new();

        /// <summary>Изменения кадра, накопленные для рассылки в конце Sweep.</summary>
        private readonly List<T> _added = new();
        private readonly List<T> _removed = new();

        /// <summary>
        /// Снимок подписчиков на время рассылки: обработчик вправе отписаться сам
        /// или подписать кого-то ещё, и обход живого списка на этом сорвался бы.
        /// </summary>
        private readonly List<Watcher<T>> _dispatch = new();

        public int Revision { get; private set; }

        public Bucket(Index owner) => _owner = owner;

        /// <summary>Первичный набор разреза из уже накопленного множества — без событий.</summary>
        public void Fill(object item)
        {
            if (item is T typed)
                Items.Add(typed);
        }

        public void TryAdd(object item)
        {
            if (item is not T typed)
                return;

            Items.Add(typed);
            _added.Add(typed);
            Revision++;
        }

        public void Sweep()
        {
            int write = 0;

            for (int read = 0; read < Items.Count; read++)
            {
                var item = Items[read];

                if (_owner.IsLive(item))
                {
                    Items[write++] = item;
                    continue;
                }

                _removed.Add(item);
                Revision++;
            }

            if (write < Items.Count)
                Items.RemoveRange(write, Items.Count - write);
        }

        public IndexWatch Watch(Action<T> added, Action<T> removed, object owner)
        {
            var watcher = new Watcher<T>
            {
                Added = added,
                Removed = removed,
                Owner = owner,
            };

            _watchers.Add(watcher);

            // Текущий состав — те же события добавления, только сразу. Погибшие, но ещё
            // не выметенные сюда не идут: читателю незачем знать о том, чего уже нет
            foreach (var item in Items)
            {
                if (!_owner.IsLive(item))
                {
                    // Ближайшая уборка объявит их выбывшими, а этот читатель об их входе
                    // не слышал. Без подавления его счётчики ушли бы в минус
                    watcher.Skip ??= new List<T>();
                    watcher.Skip.Add(item);
                    continue;
                }

                added?.Invoke(item);
            }

            return new IndexWatch(() =>
            {
                watcher.Active = false;
                _watchers.Remove(watcher);
            });
        }

        public void Notify()
        {
            if (_added.Count == 0 && _removed.Count == 0)
                return;

            // Подписка мёртвого владельца снимается сама — по тому же признаку живости,
            // что и объекты индекса. Иначе за читателем пришлось бы следить вручную
            for (int i = _watchers.Count - 1; i >= 0; i--)
            {
                var watcher = _watchers[i];

                if (watcher.Owner == null || _owner.IsLive(watcher.Owner))
                    continue;

                watcher.Active = false;
                _watchers.RemoveAt(i);
            }

            _dispatch.Clear();
            _dispatch.AddRange(_watchers);

            foreach (var watcher in _dispatch)
            {
                // Сначала ушедшие, потом пришедшие: читатель успевает освободить место
                // под новых, а его счётчики не проходят через завышенное значение
                if (watcher.Removed != null)
                    foreach (var item in _removed)
                    {
                        if (!watcher.Active)
                            break;

                        if (Skipped(watcher, item))
                            continue;

                        watcher.Removed(item);
                    }

                // Подавление живёт ровно до первой рассылки: всё, чего читатель не видел,
                // выметается той же уборкой, что и породила эти события
                watcher.Skip = null;

                if (watcher.Added != null)
                    foreach (var item in _added)
                    {
                        if (!watcher.Active)
                            break;

                        watcher.Added(item);
                    }
            }

            _added.Clear();
            _removed.Clear();
            _dispatch.Clear();
        }
    }

    /// <summary>Один читатель разреза. Владелец задан не всегда — он нужен для самоотписки.</summary>
    private sealed class Watcher<T> where T : class
    {
        public Action<T> Added;
        public Action<T> Removed;
        public object Owner;
        public bool Active = true;

        /// <summary>
        /// Погибшие к моменту подписки: об их выбытии этому читателю сообщать не о чем.
        /// Почти всегда null — список заводится только если такие объекты нашлись.
        /// </summary>
        public List<T> Skip;
    }

    /// <summary>Событие выбытия относится к тому, о чьём входе читатель не слышал.</summary>
    private static bool Skipped<T>(Watcher<T> watcher, T item) where T : class
    {
        if (watcher.Skip == null)
            return false;

        foreach (var skipped in watcher.Skip)
            if (ReferenceEquals(skipped, item))
                return true;

        return false;
    }

    internal interface IKeySlice
    {
        void Invalidate();
    }
}

/// <summary>
/// Подписка на состав разреза. Держать её обязан тот, кто подписался: пока значение живо
/// и не освобождено, обработчики зовутся. Повторный Dispose безвреден.
/// </summary>
public sealed class IndexWatch : IDisposable
{
    private Action _cancel;

    internal IndexWatch(Action cancel) => _cancel = cancel;

    public void Dispose()
    {
        var cancel = _cancel;
        _cancel = null;
        cancel?.Invoke();
    }
}
