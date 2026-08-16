using System;
using System.Collections.Generic;

/// <summary>
/// Раздача одного вида приказа: заводит общую очередь на первом получателе и подписывает
/// на неё остальных.
///
/// ЧТО ДЕЛАЕТ ДОПИСЫВАНИЕ ПО SHIFT. Получатели заняты разным: у одного своя очередь,
/// у второго своя, третий свободен. Приказ заводится ОДИН и в одной ветке, а хвосты
/// разных очередей к ней пристёгиваются: каждый доделывает своё и переходит в общую.
/// Так одно намерение хранится один раз, сколько бы очередей в него ни сошлось, —
/// а значит и вставка в него потом будет одна (см. <see cref="BuildPlan"/>).
///
/// Приказ создаётся отложенно: видов разбирается несколько, а находит получателя не всякий,
/// и заводить ветку под несостоявшийся вид незачем.
/// </summary>
public sealed class Assignment
{
    private readonly List<IOrderable> _recipients;
    private readonly bool _queue;

    /// <summary>Хвосты, уже приведённые в общую ветку. Пристёгивать второй раз нечего.</summary>
    private readonly HashSet<OrderList> _linked = new();

    private OrderList _branch;
    private Order _order;

    public Assignment(List<IOrderable> recipients, bool queue)
    {
        _recipients = recipients;
        _queue = queue;
    }

    public bool Give(IOrderable actor, Func<Order> compose)
    {
        _order ??= compose();

        if (!actor.Orders.Allows(_order.Kind))
            return false;

        bool taken = _queue ? Enqueue(actor) : Adopt(actor);

        if (taken && actor is Unit unit)
            unit.SetAnchor(_order.Point);

        return taken;
    }

    /// <summary>
    /// Раздать один и тот же приказ всем получателям, пропустив того, кто сам оказался целью.
    /// Вид приказа здесь уже выбран, поэтому перебора видов нет: не принявший его получатель
    /// просто остаётся без приказа.
    /// </summary>
    public void Deal(List<IOrderable> recipients, object target, Func<Order> compose)
    {
        foreach (var actor in recipients)
            if (target == null || CursorTargets.Other(target, actor))
                Give(actor, compose);
    }

    /// <summary>
    /// Приказ вместо прежних: получатель подписывается на общую ветку, бросая свою.
    /// Ветка заводится на первом получателе и достаётся всем остальным той же самой.
    /// </summary>
    private bool Adopt(IOrderable actor)
    {
        actor.Orders.Adopt(Branch());
        return true;
    }

    /// <summary>
    /// Приказ в дополнение к прежним: хвост очереди получателя ПРИСТЁГИВАЕТСЯ к общей
    /// ветке. Получатель доделывает своё и переходит в неё, а само намерение хранится
    /// один раз — сколько бы разных очередей ни сошлось в эту ветку.
    ///
    /// Свободному пристёгивать нечего, и он подписывается на ветку напрямую.
    ///
    /// Если конец цепочки общий с теми, кого игрок не выделял, получатель сперва
    /// забирает свой остаток себе (<see cref="OrderQueue.Fork"/>): приказ, отданный
    /// части отряда, делает из неё другой отряд, и навязывать его остальным нельзя.
    /// </summary>
    private bool Enqueue(IOrderable actor)
    {
        if (actor.Orders.List == null)
            return Adopt(actor);

        if (!Within(actor.Orders.List.Tail))
            actor.Orders.Fork();

        var tail = actor.Orders.List.Tail;

        // Хвост уже ведёт в эту ветку — второй раз его пристёгивать нечем и незачем
        if (tail == Branch() || !_linked.Add(tail))
            return true;

        tail.LinkNext(Branch());
        return true;
    }

    /// <summary>
    /// Следующий приказ той же раздачи. Ложится в ту же ветку, что и предыдущий:
    /// партия планов, размеченная одним протаскиванием, — это одна задача из многих
    /// шагов, а не сотня отдельных веток, сцепленных в цепочку.
    /// </summary>
    public void Continue() => _order = null;

    /// <summary>Общая ветка раздачи. Приказ ложится в неё при первом же получателе.</summary>
    private OrderList Branch()
    {
        _branch ??= OrderList.Open();

        if (_branch.IndexOf(_order) < 0)
            _branch.Add(_order);

        return _branch;
    }

    /// <summary>Все ли, кто способен дойти до ветки, — из числа получателей приказа.</summary>
    private bool Within(OrderList list) => list.Within(_recipients);
}
