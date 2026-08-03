using System.Collections.Generic;
using Godot;

/// <summary>
/// Очередь приказов сущности — её указатель в ветках приказов (<see cref="OrderList"/>).
///
/// РАЗДЕЛЕНИЕ ОБЯЗАННОСТЕЙ. Список приказов принадлежит не юниту, а индексу: на него можно
/// подписать кого угодно, его можно перечислить и выписать в документ. Юниту принадлежит
/// то, что у каждого своё, — место в этом списке. Отсюда и поведение отряда: двое работают
/// по одной ветке, но один уже взялся за второй приказ, пока другой заканчивает первый.
///
/// ДВЕ ССЫЛКИ, А НЕ ОДНА. Своей исполнитель считает ветку, на которую подписан (<c>Home</c>);
/// указатель же может уйти дальше — в ветку, пристёгнутую к ней продолжением. Так работает
/// приказ, отданный по Shift тем, кто занят разным: каждый доделывает своё и переходит
/// в общую ветку, не переставая быть подписчиком своей.
///
/// УКАЗАТЕЛЬ — ССЫЛКА НА ПРИКАЗ, А НЕ НОМЕР В СПИСКЕ: в середину бывает вставка,
/// и номер после неё означал бы уже другой приказ.
///
/// ФИЛЬТР ЖИВЁТ ЗДЕСЬ, и обойти его нельзя: очередь знает своего хозяина и спрашивает
/// у него набор допустимых приказов на каждой постановке. Поэтому раздающей системе
/// не нужно разбираться, кому она отдаёт приказ, — достаточно попробовать.
///
/// Ставим всё или ничего: если хоть один приказ цепочки недопустим, очередь не меняется
/// вовсе. Иначе «дойти и починить» превратилось бы в «дойти», и юнит молча встал бы
/// посреди карты вместо того, чтобы не принять приказ.
/// </summary>
public sealed class OrderQueue
{
    private readonly IOrderable _owner;

    /// <summary>Ветка, на которую исполнитель подписан. Ноль — приказов нет вовсе.</summary>
    private OrderList _home;

    /// <summary>Ветка, в которой указатель находится сейчас: своя либо пристёгнутая к ней.</summary>
    private OrderList _list;

    private Order _current;

    /// <summary>
    /// Последний исполненный приказ этой ветки. По нему указатель находит своё место,
    /// когда текущего приказа нет: в ветку могли дописать приказ уже после того, как
    /// исполнитель со всем управился, и вернуться ему надо ровно за сделанное, а не в начало.
    /// </summary>
    private Order _done;

    /// <summary>Предел длины цепочки при обходе. Защита от вырожденных случаев.</summary>
    private const int ChainLimit = 512;

    public OrderQueue(IOrderable owner) => _owner = owner;

    /// <summary>Своя ветка исполнителя. Читают раздача приказов и отладка.</summary>
    public OrderList Home => _home;

    /// <summary>Ветка, в которой исполнитель работает прямо сейчас.</summary>
    public OrderList List => _list;

    public Order Current
    {
        get
        {
            Advance();
            return _current;
        }
    }

    public bool Idle => Current == null;

    /// <summary>
    /// Что исполнителю осталось — по всей цепочке веток, начиная с текущего приказа.
    /// Пройденное сюда не попадает: список общий, и в нём остаётся то, что делают другие.
    /// </summary>
    public IEnumerable<Order> Remaining
    {
        get
        {
            Advance();

            var list = _list;
            int position = list == null || _current == null ? -1 : list.IndexOf(_current);

            while (list != null && position >= 0)
            {
                for (int i = position; i < list.Count; i++)
                    yield return list.Items[i];

                list = list.Next;
                position = 0;
            }
        }
    }

    /// <summary>Сколько приказов осталось этому исполнителю.</summary>
    public int Count
    {
        get
        {
            int count = 0;

            foreach (var _ in Remaining)
                count++;

            return count;
        }
    }

    public bool Allows(OrderKind kind) => _owner.AllowedOrders.Allows(kind);

    /// <summary>
    /// Заменить очередь целиком собственными приказами. Пользуются этим системы выдачи
    /// задач и завод: их приказы личные и общими быть не должны. Приказ игрока раздаётся
    /// иначе — через <see cref="Adopt"/>, общей веткой на весь отряд.
    /// </summary>
    public bool TrySet(params Order[] orders)
    {
        if (!Acceptable(orders))
            return false;

        // Свою собственную ветку переиспользуем: заводить новую сущность на каждую
        // самостоятельно выбранную цель значило бы сорить ими каждые полторы секунды
        if (Personal)
        {
            _home.Clear();
        }
        else
        {
            Leave();
            _home = OrderList.Open();
            _home.Subscribe(_owner);
        }

        foreach (var order in orders)
            _home.Add(order);

        _list = _home;
        _done = null;
        _current = _home.At(0);
        return true;
    }

    /// <summary>Подписаться на ветку и встать в её начало, отпустив прежнюю.</summary>
    public void Adopt(OrderList list)
    {
        if (list == null || list == _home)
            return;

        Leave();

        _home = list;
        _list = list;
        _done = null;
        list.Subscribe(_owner);
        _current = list.At(0);
    }

    /// <summary>
    /// Отделиться в собственную ветку, унеся с собой то, что ещё не исполнено.
    ///
    /// ЗАЧЕМ. Приказ, отданный части подписчиков общей очереди, — это уже другой отряд.
    /// Пристегнуть ветку к общей цепочке значило бы отдать приказ и тем, кого игрок
    /// не выделял, поэтому получатель сперва забирает свой остаток себе.
    ///
    /// Остаток переносится ссылками на те же приказы: состояние исполнения у них общее,
    /// и отделившийся продолжает видеть работу тех, с кем начинал.
    /// </summary>
    public void Fork()
    {
        if (_home == null || Personal)
            return;

        var carried = new List<Order>(Remaining);

        Leave();

        _home = OrderList.Open();
        _home.Subscribe(_owner);

        foreach (var order in carried)
            _home.Add(order);

        _list = _home;
        _done = null;
        _current = _home.At(0);
    }

    public void Clear() => Leave();

    /// <summary>
    /// Шагнуть на следующий приказ. Список общий, поэтому шагает только указатель,
    /// а дойдя до конца ветки — переходит в пристёгнутую к ней.
    /// </summary>
    public void DropCurrent()
    {
        if (_list == null || _current == null)
            return;

        int position = _list.IndexOf(_current);
        var left = _list;

        _current.Dismiss(_owner.EntityId);
        _done = _current;

        // Приказа в ветке уже нет — его сняли уборкой по уходу цели; идти дальше не по чему
        _current = position < 0 ? null : _list.At(position + 1);

        Advance();

        // Пройденное держится в ветке, пока на него смотрит хоть кто-то ещё
        left.Compact();
    }

    /// <summary>
    /// Пройти мимо всего, что больше не выполнить: цель пала, каркас достроен,
    /// план израсходован. Цикл, а не одна проверка: невыполнимой может оказаться
    /// и следующая, и та, что за ней.
    /// </summary>
    public void DropInvalid()
    {
        while (Current != null && !_current.IsValid())
            DropCurrent();
    }

    /// <summary>
    /// Сущность уходит из игры — вычищаем все приказы на неё, а не только текущий.
    ///
    /// Место в цепочке ищется ЗАРАНЕЕ: указатель мог стоять на снимаемом приказе, и после
    /// уборки восстанавливать его было бы не по чему — номера сдвинулись, а возвращать
    /// исполнителя в начало ветки нельзя, там лежит уже пройденное.
    /// </summary>
    public void DropAllFor(Node2D target)
    {
        if (_home == null)
            return;

        var resume = Survivor(target);

        _home.DropAllFor(target);

        if (_current != null && _list.IndexOf(_current) < 0)
        {
            _current = resume;
            _list = ListOf(resume) ?? _list;
        }
    }

    /// <summary>Первый начиная с текущего приказ, который уборка не тронет.</summary>
    private Order Survivor(Node2D target)
    {
        foreach (var order in Remaining)
            if (!ReferenceEquals(order.Target, target) && order.Entity != target)
                return order;

        return null;
    }

    /// <summary>В какой ветке цепочки лежит приказ.</summary>
    private OrderList ListOf(Order order)
    {
        if (order == null)
            return null;

        for (var list = _home; list != null; list = list.Next)
            if (list.IndexOf(order) >= 0)
                return list;

        return null;
    }

    /// <summary>
    /// Найти себе текущий приказ, если его нет: сперва в своей ветке за последним сделанным,
    /// затем в пристёгнутой к ней. Проверяется при каждом обращении к текущему приказу,
    /// а не только при шаге, — и ветку пристёгивают, и приказ в неё дописывают уже после
    /// того, как исполнитель со всем управился и встал.
    ///
    /// Сделанного в ветке может уже не быть: приказ, мимо которого прошли все, из неё
    /// убирают. Тогда убрано и всё, что было до него, а значит следующий по порядку —
    /// это первый оставшийся.
    /// </summary>
    private void Advance()
    {
        for (int step = 0; step < ChainLimit && _current == null && _list != null; step++)
        {
            _current = _list.At(_done == null ? 0 : _list.IndexOf(_done) + 1);

            if (_current != null || _list.Next == null)
                return;

            _list = _list.Next;
            _done = null;
        }
    }

    /// <summary>Ветка принадлежит одному этому исполнителю и никуда не ведёт.</summary>
    private bool Personal =>
        _home is { Subscribers.Count: 1, Next: null } && _home.Subscribers[0] == _owner;

    private void Leave()
    {
        _home?.Unsubscribe(_owner);
        _home = null;
        _list = null;
        _current = null;
        _done = null;
    }

    private bool Acceptable(Order[] orders)
    {
        if (orders == null || orders.Length == 0)
            return false;

        var allowed = _owner.AllowedOrders;

        foreach (var order in orders)
            if (order == null || !allowed.Allows(order.Kind))
                return false;

        return true;
    }
}

/// <summary>
/// Всё, чему можно отдать приказ. Юнит здесь — понятие широкое: это и боты, и коммандер,
/// и постройки, и враги. Разница между ними не в том, есть ли у них очередь, а в том,
/// что в их наборе: у турели один приказ, у месторождения ни одного.
///
/// РАЗДЕЛЕНИЕ ТРУДА. Приказы раздают системы выдачи задач (PlayerAiSystem, EnemyAiSystem,
/// CommandSystem по воле игрока), а исполняет их сама сущность — OrderSystem только зовёт.
/// Так политика «чем заняться» отделена от механики «как сделать», и юнит противника
/// отличается от союзного своим мозгом, а не своим кодом движения.
/// </summary>
public interface IOrderable
{
    int EntityId { get; }

    Faction Faction { get; }

    Vector2 GlobalPosition { get; }

    /// <summary>Имя для интерфейса.</summary>
    string DisplayName { get; }

    /// <summary>Что этой сущности вообще можно приказать. Пустой набор — ничего.</summary>
    OrderSet AllowedOrders { get; }

    /// <summary>Род при выделении рамкой — см. <see cref="SelectionGroups"/>.</summary>
    SelectionGroup SelectionGroup { get; }

    OrderQueue Orders { get; }

    /// <summary>Отработать текущий приказ за кадр.</summary>
    void RunOrder(Order order, double dt);

    /// <summary>Приказов нет: отпустить работу и вести себя как обычно.</summary>
    void OnIdle(double dt);
}
