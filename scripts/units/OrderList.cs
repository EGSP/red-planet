using System.Collections.Generic;
using Godot;

/// <summary>
/// Ветка приказов: список приказов, те, кто по нему работает, и продолжение — ветка,
/// в которую исполнители переходят, закончив эту.
///
/// ЗАЧЕМ ОТДЕЛЬНОЙ СУЩНОСТЬЮ, А НЕ ПОЛЕМ ЮНИТА. Пока список принадлежал юниту, владельцем
/// времени жизни очереди оказывался последний живой её участник, а взять на неё ссылку
/// можно было только у него же. Отсюда три вещи, которых не хватало: подписать на работающую
/// очередь нового исполнителя было нечем, перечислить идущие в игре задачи негде, выписать
/// очередь в документ невозможно по той же причине.
///
/// ЗАЧЕМ ВЕТКИ СЦЕПЛЯЮТСЯ. Приказ по Shift достаётся тем, кто занят разным: у одного своя
/// очередь, у второго своя, третий свободен. Дописать его каждому по отдельности значит
/// размножить одно намерение по спискам — а оно одно, и вставка в него потом должна быть
/// одна. Поэтому дописанное заводится отдельной веткой, а к ней ПРИСТЁГИВАЮТСЯ хвосты
/// тех очередей, что её получили: каждый доделывает своё и переходит в общую.
///
/// ССЫЛКА СТАВИТСЯ ТОЛЬКО В ХВОСТ И ТОЛЬКО ВПЕРЁД. Кольцо здесь означало бы очередь,
/// которая держит саму себя и потому не умрёт никогда, — <see cref="LinkNext"/> его
/// не допускает.
///
/// СПИСОК ОБЩИЙ, УКАЗАТЕЛЬ ЛИЧНЫЙ. Двое работают по одной ветке, но каждый держит свой
/// указатель (см. <see cref="OrderQueue"/>): один уже взялся за второй приказ, пока другой
/// заканчивает первый. Поэтому приказ снимается из списка не тогда, когда его исполнил
/// первый, а когда мимо него прошли все.
/// </summary>
public sealed class OrderList
{
    /// <summary>Предел длины цепочки при обходах. Защита от вырожденных случаев.</summary>
    private const int ChainLimit = 64;

    private readonly List<Order> _items = new();
    private readonly List<IOrderable> _subscribers = new();

    /// <summary>Ветки, пристёгнутые к этой: их исполнители придут сюда, доделав своё.</summary>
    private readonly List<OrderList> _referrers = new();

    private OrderList _next;

    /// <summary>
    /// Завести ветку и внести её в индекс. Условие живости объявляется здесь же:
    /// индекс сам снимет ветку, когда работать по ней станет некому.
    /// </summary>
    public static OrderList Open()
    {
        var list = new OrderList();
        GameManager.I?.Index.Add(list, () => list.Live);
        return list;
    }

    public IReadOnlyList<Order> Items => _items;

    public IReadOnlyList<IOrderable> Subscribers => _subscribers;

    public int Count => _items.Count;

    /// <summary>Куда исполнитель перейдёт, доделав эту ветку.</summary>
    public OrderList Next => _next;

    /// <summary>Конец цепочки: ветка, за которой продолжения уже нет.</summary>
    public OrderList Tail
    {
        get
        {
            var list = this;

            for (int step = 0; step < ChainLimit && list._next != null; step++)
                list = list._next;

            return list;
        }
    }

    /// <summary>
    /// Есть ли кому работать по ветке. Считается по всем, кто способен до неё дойти:
    /// пристёгнутая ветка живёт, пока живы исполнители тех очередей, что в неё ведут,
    /// даже если своих подписчиков у неё пока нет ни одного.
    ///
    /// Условие живости для индекса, поэтому оно только отвечает и ничего не трогает:
    /// побочных действий предикат иметь не вправе.
    /// </summary>
    public bool Live
    {
        get
        {
            foreach (var actor in _subscribers)
                if (Alive.Is(actor as Node))
                    return true;

            foreach (var referrer in _referrers)
                if (referrer.Live)
                    return true;

            return false;
        }
    }

    /// <summary>
    /// Пристегнуть ветку продолжением. Ставится только в хвост и только вперёд: ссылка
    /// на ветку, из которой эта достижима, замкнула бы цепочку в кольцо.
    ///
    /// Пристёгнутая ветка получает в состав своих приказов всех, кто теперь способен
    /// до неё дойти: состав нужен приказу движения, чтобы дождаться отставших, а отставшие
    /// здесь — как раз те, кто ещё доделывает предыдущую ветку.
    /// </summary>
    public bool LinkNext(OrderList next)
    {
        if (next == null || next == this || _next != null || next.Reaches(this))
            return false;

        _next = next;
        next._referrers.Add(this);

        foreach (var actor in _subscribers)
            next.EnlistDownstream(actor);

        foreach (var referrer in _referrers)
            referrer.EnlistForward(next);

        return true;
    }

    /// <summary>
    /// Все ли, кто способен дойти до этой ветки, — из числа перечисленных.
    ///
    /// Спрашивается ВВЕРХ ПО ССЫЛКАМ, а не по своим подписчикам: у общей ветки прямых
    /// подписчиков может не быть вовсе — в неё приходят из пристёгнутых очередей, — и судить
    /// по пустому составу значило бы счесть своей ту ветку, куда придут посторонние.
    /// </summary>
    public bool AudienceWithin(List<IOrderable> allowed)
    {
        foreach (var actor in _subscribers)
            if (!allowed.Contains(actor))
                return false;

        foreach (var referrer in _referrers)
            if (!referrer.AudienceWithin(allowed))
                return false;

        return true;
    }

    /// <summary>Достижима ли ветка отсюда по ссылкам продолжения.</summary>
    public bool Reaches(OrderList other)
    {
        var list = _next;

        for (int step = 0; step < ChainLimit && list != null; step++)
        {
            if (list == other)
                return true;

            list = list._next;
        }

        return false;
    }

    /// <summary>
    /// Подписать исполнителя. Он записывается в состав приказов не только этой ветки,
    /// но и всех, что за ней: дойдёт он и до них.
    /// </summary>
    public void Subscribe(IOrderable actor)
    {
        if (actor == null || _subscribers.Contains(actor))
            return;

        _subscribers.Add(actor);
        EnlistDownstream(actor);
    }

    public void Unsubscribe(IOrderable actor)
    {
        if (actor == null || !_subscribers.Remove(actor))
            return;

        DismissDownstream(actor);
    }

    /// <summary>Дописать приказ в хвост ветки — всем, кто по ней работает или придёт.</summary>
    public void Add(Order order)
    {
        if (order == null)
            return;

        _items.Add(order);
        Enlist(order);
    }

    /// <summary>
    /// Вставить приказ следом за тем, что нацелен на указанное место работы. Зовёт план,
    /// когда превращается в каркас: приказ на каркас обязан оказаться сразу после приказа
    /// на план, а не в конце очереди, где его отделили бы от него чужие дела.
    ///
    /// Замены нет намеренно — игрок должен видеть оба шага, а не обнаруживать подмену
    /// уже отданного приказа.
    /// </summary>
    public bool InsertAfter(IWorkSite site, Order order)
    {
        if (site == null || order == null)
            return false;

        for (int i = 0; i < _items.Count; i++)
        {
            if (!ReferenceEquals(_items[i].Target, site))
                continue;

            _items.Insert(i + 1, order);
            Enlist(order);
            return true;
        }

        return false;
    }

    public int IndexOf(Order order) => order == null ? -1 : _items.IndexOf(order);

    /// <summary>Приказ на позиции или null за концом списка.</summary>
    public Order At(int position) =>
        position >= 0 && position < _items.Count ? _items[position] : null;

    /// <summary>
    /// Снять все приказы на уходящую из мира сущность — по всей цепочке. Касается всех
    /// разом: цели больше нет, и приказ на неё невыполним у любого.
    /// </summary>
    public void DropAllFor(Node2D target)
    {
        var list = this;

        for (int step = 0; step < ChainLimit && list != null; step++)
        {
            list.DropOwnFor(target);
            list = list._next;
        }
    }

    /// <summary>Очистить список целиком. Зовёт очередь, когда приказ отдан взамен прежних.</summary>
    public void Clear()
    {
        foreach (var order in _items)
            Retire(order);

        _items.Clear();
    }

    /// <summary>
    /// Убрать с головы то, мимо чего прошли все. Приказ, исполненный одним подписчиком,
    /// остаётся в списке, пока на него смотрит хоть один другой: список общий, а указатели
    /// личные, и вынимать приказ из-под ещё работающего нельзя.
    /// </summary>
    public void Compact()
    {
        Prune();

        while (_items.Count > 0 && !Awaited(_items[0]))
        {
            Retire(_items[0]);
            _items.RemoveAt(0);
        }
    }

    private void DropOwnFor(Node2D target)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var order = _items[i];

            if (!ReferenceEquals(order.Target, target) && order.Entity != target)
                continue;

            Retire(order);
            _items.RemoveAt(i);
        }
    }

    /// <summary>Смотрит ли на приказ хоть кто-то из способных до него дойти.</summary>
    private bool Awaited(Order order)
    {
        foreach (var actor in _subscribers)
            if (actor.Orders.Current == order)
                return true;

        foreach (var referrer in _referrers)
            if (referrer.Awaited(order))
                return true;

        return false;
    }

    /// <summary>Записать в состав приказа всех, кто способен до этой ветки дойти.</summary>
    private void Enlist(Order order)
    {
        foreach (var actor in _subscribers)
            order.Enlist(actor.EntityId);

        foreach (var referrer in _referrers)
            referrer.Enlist(order);
    }

    private void Retire(Order order)
    {
        foreach (var actor in _subscribers)
            order.Dismiss(actor.EntityId);

        foreach (var referrer in _referrers)
            referrer.Retire(order);
    }

    /// <summary>Записать исполнителя в состав приказов этой ветки и всех, что за ней.</summary>
    private void EnlistDownstream(IOrderable actor)
    {
        var list = this;

        for (int step = 0; step < ChainLimit && list != null; step++)
        {
            foreach (var order in list._items)
                order.Enlist(actor.EntityId);

            list = list._next;
        }
    }

    private void DismissDownstream(IOrderable actor)
    {
        var list = this;

        for (int step = 0; step < ChainLimit && list != null; step++)
        {
            foreach (var order in list._items)
                order.Dismiss(actor.EntityId);

            list = list._next;
        }
    }

    /// <summary>Записать своих подписчиков в состав приказов новой ветки — вверх по ссылкам.</summary>
    private void EnlistForward(OrderList target)
    {
        foreach (var actor in _subscribers)
            target.EnlistDownstream(actor);

        foreach (var referrer in _referrers)
            referrer.EnlistForward(target);
    }

    /// <summary>
    /// Убрать из состава тех, кого больше нет. Обычно подписчик снимается сам — очередь
    /// он отпускает при гибели, — но сущность может уйти из мира и помимо этого пути.
    /// </summary>
    private void Prune()
    {
        for (int i = _subscribers.Count - 1; i >= 0; i--)
            if (!Alive.Is(_subscribers[i] as Node))
                _subscribers.RemoveAt(i);
    }
}
