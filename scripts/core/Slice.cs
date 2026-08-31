using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Разрез индекса — окно в живой список, а не его копия.
///
/// Перебор идёт по индексу элемента и на ходу отсеивает мёртвых, поэтому системе больше
/// не нужно ни приводить тип, ни проверять живость: обход отдаёт только то, с чем можно
/// работать. Своей памяти разрез не занимает, и обойти его можно сколько угодно раз за кадр.
///
/// ФИЛЬТР НИЧЕГО НЕ ЗАВОДИТ. Where возвращает ту же структуру, указывающую на тот же самый
/// список, плюс делегат: нового набора не появляется, ничего не регистрируется и между
/// кадрами не живёт. По сути это то же самое «if … continue» в теле обхода, только вынесенное
/// на место вызова, — и нужен он ровно затем, чтобы сузить разрез для Nearest, First и Count,
/// которые владеют циклом сами. Пишете свой foreach — пишите обычное условие, оно и понятнее,
/// и не стоит вызова делегата на каждом шаге.
/// </summary>
/// <summary>
/// Элемент разреза: сам объект и его признак живости, взятый один раз при укладке.
///
/// ЗАЧЕМ ХРАНИТЬ ПРИЗНАК РЯДОМ. Живость спрашивается на каждом элементе каждого обхода,
/// а объявляет её интерфейс <see cref="ILive"/>. Приведение к интерфейсу стоит проверки
/// таблицы типов, и по замеру одно только это приведение занимало пять процентов
/// собственного времени главного потока. Приведение делается один раз — при попадании
/// в разрез, — а обход читает готовую ссылку.
/// </summary>
public readonly struct Member<T> where T : class
{
    public readonly T Item;

    /// <summary>Признак живости объекта либо null, если объект его не несёт.</summary>
    public readonly ILive Live;

    public Member(T item)
    {
        Item = item;
        Live = item as ILive;
    }
}

public readonly struct Slice<T> where T : class
{
    private static readonly List<Member<T>> Nothing = new();

    private readonly List<Member<T>> _items;
    private readonly Index _index;
    private readonly Func<T, bool> _filter;

    internal Slice(List<Member<T>> items, Index index, Func<T, bool> filter = null)
    {
        _items = items ?? Nothing;
        _index = index;
        _filter = filter;
    }

    /// <summary>
    /// Сузить разрез для Nearest, First или Count. Фильтры складываются, а не вытесняют
    /// друг друга — но каждое сложение стоит замыкания, поэтому лучше одно условие.
    /// </summary>
    public Slice<T> Where(Func<T, bool> filter)
    {
        if (filter == null)
            return this;

        var previous = _filter;

        return new Slice<T>(_items, _index,
            previous == null ? filter : item => previous(item) && filter(item));
    }

    public Enumerator GetEnumerator() => new(_items, _index, _filter);

    /// <summary>Сколько живых. Считается обходом — держать точный счётчик разрезу нечем.</summary>
    public int Count
    {
        get
        {
            int count = 0;

            foreach (var _ in this)
                count++;

            return count;
        }
    }

    public bool Any
    {
        get
        {
            foreach (var _ in this)
                return true;

            return false;
        }
    }

    public T First
    {
        get
        {
            foreach (var item in this)
                return item;

            return null;
        }
    }

    /// <summary>
    /// Ближайший к точке, не дальше maxDistance пикселей.
    ///
    /// Позиция берётся из объекта переданной функцией: у разреза нет и не должно быть мнения
    /// о том, что такое положение — в него складывают и ноды, и обычные данные.
    /// </summary>
    public T Nearest(Vector2 from, Func<T, Vector2> position, float maxDistance = float.MaxValue)
    {
        T best = null;

        float bestDistance = maxDistance >= float.MaxValue
            ? float.MaxValue
            : maxDistance * maxDistance;

        foreach (var item in this)
        {
            float distance = from.DistanceSquaredTo(position(item));
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = item;
        }

        return best;
    }

    /// <summary>
    /// Перебор с пропуском мёртвых. Идёт по номеру, а не перечислителем списка: состав
    /// меняется только в конце кадра, но лишняя возможность сломаться посреди обхода нам не нужна.
    /// </summary>
    public struct Enumerator
    {
        private readonly List<Member<T>> _items;
        private readonly Index _index;
        private readonly Func<T, bool> _filter;
        private int _at;

        internal Enumerator(List<Member<T>> items, Index index, Func<T, bool> filter)
        {
            _items = items;
            _index = index;
            _filter = filter;
            _at = -1;
            Current = null;
        }

        public T Current { get; private set; }

        public bool MoveNext()
        {
            if (_index == null)
                return false;

            while (++_at < _items.Count)
            {
                var member = _items[_at];

                // Признак живости взят при укладке; спрашивать индекс приходится лишь
                // о тех, кто его не несёт, — а таких в разрезах почти нет
                if (member.Live != null)
                {
                    if (!member.Live.Live)
                        continue;
                }
                else if (!_index.IsLive(member.Item))
                {
                    continue;
                }

                var item = member.Item;

                if (_filter != null && !_filter(item))
                    continue;

                Current = item;
                return true;
            }

            Current = null;
            return false;
        }
    }
}

/// <summary>
/// Постоянный разрез по ключу: одно множество, разложенное по значению — цели по сторонам,
/// сущности по клетке сетки. Заводится через Index.SliceBy и живёт до конца сессии.
///
/// ЗАЧЕМ. Перебирать всех и отбрасывать чужих можно и фильтром, но фильтр платит полным
/// обходом за каждый спрос. Здесь обход один на кадр, а спросов за кадр — по числу тех,
/// кто ищет: каждый ствол, каждый снаряд, каждый враг.
///
/// Пересобирается лениво и не чаще раза за кадр — по метке, которую ставит уборка индекса.
/// Отсюда единственное требование к ключу: посреди кадра он меняться не должен.
/// </summary>
public sealed class KeySlice<TKey, T> : Index.IKeySlice where T : class
{
    private readonly Index _index;
    private readonly Func<T, TKey> _key;
    private readonly Dictionary<TKey, List<Member<T>>> _byKey = new();

    private bool _stale = true;

    internal KeySlice(Index index, Func<T, TKey> key)
    {
        _index = index;
        _key = key;
    }

    public Slice<T> this[TKey key]
    {
        get
        {
            Rebuild();

            return new Slice<T>(_byKey.TryGetValue(key, out var items) ? items : null, _index);
        }
    }

    public IReadOnlyCollection<TKey> Keys
    {
        get
        {
            Rebuild();
            return _byKey.Keys;
        }
    }

    void Index.IKeySlice.Invalidate() => _stale = true;

    private void Rebuild()
    {
        if (!_stale)
            return;

        _stale = false;

        // Списки чистим, но не выбрасываем: разрез пересобирается каждый кадр,
        // и заново выделять под него память было бы напрасной работой
        foreach (var items in _byKey.Values)
            items.Clear();

        foreach (var item in _index.All<T>())
        {
            var key = _key(item);

            if (!_byKey.TryGetValue(key, out var items))
                _byKey[key] = items = new List<Member<T>>();

            items.Add(new Member<T>(item));
        }
    }
}
