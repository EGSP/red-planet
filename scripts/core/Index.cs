using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
/// НОДЫ УХОДЯТ САМИ. Освобождённая или помеченная QueueFree нода перестаёт попадать
/// в разрезы без единого вызова — Remove нужен только объектам, о смерти которых движок
/// не знает. Забыть снять сущность физически нельзя, и это главное, чего не хватало группам.
/// </summary>
public sealed class Index
{
    /// <summary>Все объекты множества — из него добираются заново заведённые разрезы.</summary>
    private readonly List<object> _items = new();

    private readonly List<object> _pending = new();

    /// <summary>Снятые вручную. Сравниваем по ссылке, чтобы не звать Equals у чужих объектов.</summary>
    private readonly HashSet<object> _retired = new(ByReference.Instance);

    private readonly Dictionary<Type, IBucket> _buckets = new();

    private readonly List<IKeySlice> _keySlices = new();

    /// <summary>Объект войдёт в множество на границе кадра.</summary>
    public void Add(object item)
    {
        if (item != null)
            _pending.Add(item);
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

    /// <summary>Все, кто подходит под признак. Разрез заводится при первом спросе.</summary>
    public Slice<T> All<T>() where T : class => new(BucketOf<T>().Items, this);

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
    /// Конец кадра: впустить новых, вымести мёртвых, пометить разрезы по ключу устаревшими.
    /// Единственное место, где состав множества действительно меняется.
    /// </summary>
    public void Sweep()
    {
        foreach (var item in _pending)
        {
            // Родился и погиб внутри одного кадра — в множество не входит вовсе
            if (!IsLive(item))
                continue;

            _items.Add(item);

            foreach (var bucket in _buckets.Values)
                bucket.TryAdd(item);
        }

        _pending.Clear();

        Compact(_items);

        foreach (var bucket in _buckets.Values)
            bucket.Sweep();

        // Только после выметания: до него снятые ещё должны считаться мёртвыми
        _retired.Clear();

        foreach (var slice in _keySlices)
            slice.Invalidate();
    }

    public int Count => _items.Count;

    /// <summary>
    /// Объект ещё в игре. Единственное место во всём проекте, где живость проверяется руками:
    /// нода жива, пока цела и не помечена на удаление, всё прочее — пока его не сняли.
    /// </summary>
    internal bool IsLive(object item)
    {
        if (item == null || _retired.Contains(item))
            return false;

        if (item is not Godot.GodotObject obj)
            return true;

        return Alive.Is(obj) && (obj is not Godot.Node node || !node.IsQueuedForDeletion());
    }

    private Bucket<T> BucketOf<T>() where T : class
    {
        if (_buckets.TryGetValue(typeof(T), out var existing))
            return (Bucket<T>)existing;

        // Признак спросили впервые — набираем разрез из тех, кто уже в множестве
        var bucket = new Bucket<T>(this);

        foreach (var item in _items)
            bucket.TryAdd(item);

        _buckets[typeof(T)] = bucket;
        return bucket;
    }

    /// <summary>Выбросить мёртвых, сохранив порядок остальных.</summary>
    private void Compact<T>(List<T> items) where T : class
    {
        int write = 0;

        for (int read = 0; read < items.Count; read++)
            if (IsLive(items[read]))
                items[write++] = items[read];

        if (write < items.Count)
            items.RemoveRange(write, items.Count - write);
    }

    private interface IBucket
    {
        void TryAdd(object item);
        void Sweep();
    }

    /// <summary>
    /// Разрез по одному признаку. Проверка «подходит ли» — обычное приведение типа,
    /// поэтому раскладка ничего не знает ни про наследование, ни про интерфейсы отдельно.
    /// </summary>
    private sealed class Bucket<T> : IBucket where T : class
    {
        private readonly Index _owner;

        public readonly List<T> Items = new();

        public Bucket(Index owner) => _owner = owner;

        public void TryAdd(object item)
        {
            if (item is T typed)
                Items.Add(typed);
        }

        public void Sweep() => _owner.Compact(Items);
    }

    internal interface IKeySlice
    {
        void Invalidate();
    }

    /// <summary>
    /// Сравнение по ссылке в обход Equals и GetHashCode самого объекта: у освобождённой
    /// обёртки движка они небезопасны, а нам от снятого объекта нужна только его личность.
    /// </summary>
    private sealed class ByReference : IEqualityComparer<object>
    {
        public static readonly ByReference Instance = new();

        public new bool Equals(object a, object b) => ReferenceEquals(a, b);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
