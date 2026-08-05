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
/// ССЫЛКА ОДНА: КУРСОР ВСЕГДА ПОДПИСАН НА ТУ ВЕТКУ, В КОТОРОЙ СТОИТ. Доделав свою ветку,
/// исполнитель переходит в пристёгнутую к ней, и подписка переезжает вместе с указателем.
/// Отсюда правило сбора отряда: приказ движения ждёт только тех, кто уже здесь, а тот, кто
/// ещё доделывает предыдущую ветку, никого не задерживает. Ждать его значило бы, что двое
/// свободных, посланных с Shift вслед за третьим, простоят в первой точке до конца его
/// стройки, — со стороны это неотличимо от зависшего приказа.
///
/// УКАЗАТЕЛЬ — ССЫЛКА НА ПРИКАЗ, А НЕ НОМЕР В СПИСКЕ: список общий, из него убирают
/// пройденное, и номер после уборки означал бы уже другой приказ.
///
/// ФИЛЬТР ЖИВЁТ ЗДЕСЬ, и обойти его нельзя: очередь знает своего хозяина и спрашивает
/// у него набор допустимых приказов на каждой постановке. Поэтому раздающей системе
/// не нужно разбираться, кому она отдаёт приказ, — достаточно попробовать.
///
/// Ставим всё или ничего: если хоть один приказ цепочки недопустим, очередь не меняется
/// вовсе. Иначе «дойти и починить» превратилось бы в «дойти», и юнит молча встал бы
/// посреди карты вместо того, чтобы не принять приказ.
/// </summary>
public sealed class OrderQueue : IOrderFollower
{
    private readonly IOrderable _owner;

    /// <summary>Ветка, в которой стоит указатель и на которую подписан. Ноль — приказов нет.</summary>
    private OrderList _list;

    private Order _current;

    /// <summary>
    /// Последний исполненный приказ этой ветки. По нему указатель находит своё место,
    /// когда текущего приказа нет: в ветку могли дописать приказ уже после того, как
    /// исполнитель со всем управился, и вернуться ему надо ровно за сделанное, а не в начало.
    /// </summary>
    private Order _done;

    public OrderQueue(IOrderable owner) => _owner = owner;

    /// <summary>Ветка, в которой исполнитель работает прямо сейчас. Читают раздача и отладка.</summary>
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

    /// <summary>
    /// Есть ли приказ после текущего. Нужен сопровождению: Follow с хвостом очереди
    /// обязан когда-то уступить место следующему, а одиночный Follow — нет.
    /// </summary>
    public bool HasMore
    {
        get
        {
            Advance();

            if (_current == null)
                return false;

            bool seen = false;

            foreach (var _ in Remaining)
            {
                if (!seen)
                {
                    seen = true;
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    public bool Allows(OrderKind kind) => _owner.AllowedOrders.Allows(kind);

    // ── роль подписчика ветки ──────────────────────────────────────────────────

    /// <summary>Курсор жив, пока жив его хозяин: работать по ветке больше некому.</summary>
    public bool Live => Alive.Is(_owner as Node);

    /// <summary>
    /// Стоит ли указатель на этом приказе — или встанет на него, когда его спросят.
    ///
    /// СПРАШИВАЕТСЯ ИЗ УБОРКИ, ПОЭТОМУ НИЧЕГО НЕ ТРОГАЕТ. Обращение к <see cref="Current"/>
    /// двигает указатель, а двигать чужие указатели уборка не вправе: она обходит подписчиков
    /// ветки, среди которых наш — чужой. Поэтому смотрим наперёд, но состояние не меняем.
    /// </summary>
    public bool Awaits(Order order) => order != null && ReferenceEquals(Ahead, order);

    public bool Within(List<IOrderable> allowed) => allowed.Contains(_owner);

    public void Enlist(Order order) => order?.Enlist(_owner.EntityId);

    public void Dismiss(Order order) => order?.Dismiss(_owner.EntityId);

    // ── постановка приказов ────────────────────────────────────────────────────

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
            _list.Clear();
        }
        else
        {
            Leave();
            _list = OrderList.Open();
            _list.Subscribe(this);
        }

        foreach (var order in orders)
            _list.Add(order);

        _done = null;
        _current = _list.At(0);
        return true;
    }

    /// <summary>
    /// Подписаться на ветку и встать в её начало, отпустив прежнюю.
    ///
    /// Вместе с брошенным остатком снимаются планы построек, на которые больше никто
    /// не нацелен: отказ от приказа — отказ и от размеченного намерения. Гибель
    /// исполнителя идёт через <see cref="Clear"/> и планы не трогает.
    /// </summary>
    public void Adopt(OrderList list)
    {
        if (list == null || list == _list)
            return;

        List<BuildPlan> abandoned = null;

        foreach (var order in Remaining)
        {
            if (order.Kind != OrderKind.Build || order.Target is not BuildPlan { NeedsWork: true } plan)
                continue;

            abandoned ??= new List<BuildPlan>();

            if (!abandoned.Contains(plan))
                abandoned.Add(plan);
        }

        Leave();

        _list = list;
        _done = null;
        list.Subscribe(this);
        _current = list.At(0);

        BuildPlan.ReleaseAbandoned(abandoned);
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
        if (_list == null || Personal)
            return;

        var carried = new List<Order>(Remaining);

        Leave();

        _list = OrderList.Open();
        _list.Subscribe(this);

        foreach (var order in carried)
            _list.Add(order);

        _done = null;
        _current = _list.At(0);
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
        if (_list == null)
            return;

        var resume = Survivor(target);

        _list.DropAllFor(target);

        if (_current != null && _list.IndexOf(_current) >= 0)
            return;

        if (resume == null)
        {
            // Остатка не осталось вовсе. Встаём в конец своей ветки, а не в начало:
            // дописанное в неё будет замечено, а пройденное не повторится
            _current = null;
            _done = _list.At(_list.Count - 1);
            return;
        }

        var where = ListOf(resume);

        if (where != null && where != _list)
            Move(where);

        _current = resume;
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

        for (var list = _list; list != null; list = list.Next)
            if (list.IndexOf(order) >= 0)
                return list;

        return null;
    }

    /// <summary>
    /// Найти себе текущий приказ, если его нет, и перейти в пристёгнутую ветку, если своя
    /// закончилась. Проверяется при каждом обращении к текущему приказу, а не только при
    /// шаге, — и ветку пристёгивают, и приказ в неё дописывают уже после того, как
    /// исполнитель со всем управился и встал.
    /// </summary>
    private void Advance()
    {
        if (_current != null || _list == null)
            return;

        var found = Seek(out var where);

        if (found == null)
            return;

        if (where != _list)
            Move(where);

        _current = found;
    }

    /// <summary>
    /// Куда указатель встанет: тот же обход, что и в <see cref="Advance"/>, но ничего
    /// не меняющий. Отсюда же берёт ответ <see cref="Awaits"/>.
    /// </summary>
    private Order Ahead => _current ?? Seek(out _);

    /// <summary>
    /// Первый приказ, до которого указателю есть дело: сперва в своей ветке за последним
    /// сделанным, затем в пристёгнутой к ней.
    ///
    /// Сделанного в ветке может уже не быть: приказ, мимо которого прошли все, из неё
    /// убирают. Тогда убрано и всё, что было до него, а значит следующий по порядку —
    /// это первый оставшийся.
    /// </summary>
    private Order Seek(out OrderList where)
    {
        where = _list;
        var done = _done;

        for (int step = 0; step < OrderList.ChainLimit && where != null; step++)
        {
            var found = where.At(done == null ? 0 : where.IndexOf(done) + 1);

            if (found != null)
                return found;

            where = where.Next;
            done = null;
        }

        return null;
    }

    /// <summary>
    /// Перейти в пристёгнутую ветку. ПОДПИСКА ПЕРЕЕЗЖАЕТ ВМЕСТЕ С УКАЗАТЕЛЕМ: исполнитель
    /// выходит из состава приказов прежней ветки и входит в состав приказов новой. Это и есть
    /// правило сбора — ждут друг друга те, кто уже здесь.
    ///
    /// Прежняя ветка от этого может остаться без подписчиков вовсе и уйти из индекса.
    /// Так и задумано: работать по ней больше некому.
    /// </summary>
    private void Move(OrderList target)
    {
        _list?.Unsubscribe(this);
        _list = target;
        _done = null;
        target.Subscribe(this);
    }

    /// <summary>Ветка принадлежит одному этому исполнителю и никуда не ведёт.</summary>
    private bool Personal => _list != null && _list.Only(this);

    private void Leave()
    {
        _list?.Unsubscribe(this);
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

    /// <summary>
    /// Приказы, которые очередь принимает: пересечение объявленных в определении
    /// и фактически исполнимых. Пустой набор сам по себе не запрещает выделение —
    /// см. <see cref="SoftOrders"/>.
    /// </summary>
    OrderSet AllowedOrders { get; }

    /// <summary>
    /// Исполнимые, но не объявленные в определении. Панель показывает их со звёздочкой;
    /// в очередь они не ставятся.
    /// </summary>
    OrderSet SoftOrders { get; }

    /// <summary>Род при выделении рамкой — см. <see cref="SelectionGroups"/>.</summary>
    SelectionGroup SelectionGroup { get; }

    OrderQueue Orders { get; }

    /// <summary>Отработать текущий приказ за кадр.</summary>
    void RunOrder(Order order, double dt);

    /// <summary>Приказов нет: отпустить работу и вести себя как обычно.</summary>
    void OnIdle(double dt);
}
