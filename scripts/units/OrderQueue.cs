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
    /// Есть ли исполнителю куда перейти после текущего приказа.
    ///
    /// Нужен двоим. Сопровождению: Follow с хвостом очереди обязан когда-то уступить место
    /// следующему, а одиночный Follow — нет. Патрулю: приказ, после которого идти некуда,
    /// не кончается вовсе — на том и держится вечный обход одной точки или одной области.
    ///
    /// ВОЗВРАТ ПО КОЛЬЦУ СЧИТАЕТСЯ ПЕРЕХОДОМ. Кольцо из двух патрулей на последнем своём
    /// приказе остатка не имеет, но перейти исполнителю есть куда — к началу кольца, —
    /// и без этой поправки он застрял бы на последней точке маршрута навсегда.
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

            return Ringed && !ReferenceEquals(RingHead, _current);
        }
    }

    /// <summary>
    /// Принимает ли очередь такой приказ. Спрашивается не сам вид, а вид, которым он
    /// разрешается (см. <see cref="Order.Permission"/>): у приказа по области своего
    /// разрешения нет, он наследует разрешение той же работы по точке.
    /// </summary>
    public bool Allows(OrderKind kind) => _owner.AllowedOrders.Allows(Order.Permission(kind));

    // ── кольцо патруля ─────────────────────────────────────────────────────────

    /// <summary>
    /// Замкнута ли очередь в кольцо: последний приказ цепочки — патруль.
    ///
    /// ПОЧЕМУ ПРИЗНАК У ОЧЕРЕДИ, А НЕ У ПРИКАЗА. Кольцевание есть свойство маршрута, а не
    /// отдельного шага: два приказа, помеченных «вечными», сами по себе не говорят, что между
    /// ними надо ходить по кругу. Здесь же оно выражается одним правилом — дойдя до конца,
    /// вернуться к первому патрулю, — из которого следуют все частные случаи. Стоит поставить
    /// после патруля обычный приказ, и кольца нет: маршрут проходится однажды. Обычные же
    /// приказы ПЕРЕД патрулём отработаются один раз и не повторятся, потому что возврат идёт
    /// не в начало очереди, а к первому патрулю.
    /// </summary>
    public bool Ringed => Last()?.Kind == OrderKind.Patrol;

    /// <summary>Приказ, с которого начинается повтор, либо null, если кольца нет.</summary>
    public Order RingHead => Ringed ? FirstPatrol(out _) : null;

    /// <summary>
    /// Весь маршрут кольца по порядку, от его начала до конца цепочки, — включая шаги,
    /// которые исполнитель уже прошёл.
    ///
    /// ПРОЙДЕННОЕ ЗДЕСЬ НЕ ЛИШНЕЕ, и в этом отличие от <see cref="Remaining"/>. Остаток
    /// отвечает на вопрос «что исполнителю ещё делать», а маршрут — на вопрос «где он ходит»,
    /// и на следующем круге пройденные шаги станут предстоящими снова. Показывать игроку
    /// один остаток значило бы, что круг патрулирования на глазах укорачивается, хотя ничего
    /// не меняется.
    /// </summary>
    public IEnumerable<Order> Ring
    {
        get
        {
            if (!Ringed)
                yield break;

            var head = FirstPatrol(out var list);

            if (head == null || list == null)
                yield break;

            int position = list.IndexOf(head);

            for (int step = 0; step < OrderList.ChainLimit && list != null && position >= 0; step++)
            {
                for (int i = position; i < list.Count; i++)
                    yield return list.Items[i];

                list = list.Next;
                position = 0;
            }
        }
    }

    /// <summary>Последний приказ достижимой цепочки веток.</summary>
    private Order Last()
    {
        Order last = null;
        var list = _list;

        for (int step = 0; step < OrderList.ChainLimit && list != null; step++)
        {
            if (list.Count > 0)
                last = list.At(list.Count - 1);

            list = list.Next;
        }

        return last;
    }

    private Order FirstPatrol(out OrderList where)
    {
        where = _list;

        for (int step = 0; step < OrderList.ChainLimit && where != null; step++)
        {
            for (int i = 0; i < where.Count; i++)
                if (where.Items[i].Kind == OrderKind.Patrol)
                    return where.Items[i];

            where = where.Next;
        }

        return null;
    }

/// <summary>
    /// Вернётся ли исполнитель к этому приказу на новом круге. Спрашивает отрисовка,
    /// чтобы отличить пройденный шаг маршрута от пройденного шага обычной очереди:
    /// первый вернётся, второй нет.
    /// </summary>
    public bool Repeats(Order order) => Ringed && InRing(order);

    /// <summary>Входит ли приказ в кольцо: он лежит на первом патруле или за ним.</summary>
    private bool InRing(Order order)
    {
        var head = FirstPatrol(out var list);

        if (head == null || list == null)
            return false;

        int position = list.IndexOf(head);

        for (int step = 0; step < OrderList.ChainLimit && list != null && position >= 0; step++)
        {
            for (int i = position; i < list.Count; i++)
                if (ReferenceEquals(list.Items[i], order))
                    return true;

            list = list.Next;
            position = 0;
        }

        return false;
    }

    /// <summary>
    /// Кольцо пройдено до конца — вернуться к его началу. Зовётся из <see cref="Advance"/>,
    /// когда впереди ничего не осталось.
    /// </summary>
    private Order Rewind(out OrderList where)
    {
        where = _list;
        return Ringed ? FirstPatrol(out where) : null;
    }

    /// <summary>
    /// Свернуть кольцо в собственную ветку, впервые дойдя до патруля.
    ///
    /// ЗАЧЕМ. Указатель, переходя в пристёгнутую ветку, отписывается от прежней, а та,
    /// оставшись без подписчиков, уходит из индекса. Маршрут же набирается по Shift, то есть
    /// ветка на каждый щелчок, — и вернуться по кольцу к первой точке было бы уже некуда:
    /// её ветки к тому времени нет. Поэтому исполнитель, дойдя до патруля, забирает весь
    /// остаток цепочки себе, и дальше кольцо крутится внутри одной ветки, которая жива,
    /// пока жив он сам.
    ///
    /// Пройденное до патруля не переносится: возврат идёт к первому патрулю, а не в начало
    /// очереди, — обычные приказы перед маршрутом отрабатываются однажды.
    /// </summary>
    private void Collapse()
    {
        if (_list == null || Personal)
            return;

        var head = FirstPatrol(out var list);

        if (head == null || list == null)
            return;

        var carried = Carry(list, list.IndexOf(head));

        if (carried.Count == 0)
            return;

        var current = _current;

        Leave();

        _list = OrderList.Open();
        _list.Subscribe(this);

        foreach (var order in carried)
            _list.Add(order);

        _current = current;
    }

    /// <summary>
    /// Приказы цепочки начиная с указанного места. Отличается от <see cref="Remaining"/>
    /// тем, что ничего не двигает и отсчитывается не от указателя: кольцо забирается
    /// от своего начала, а начало это лежит ПОЗАДИ указателя, когда тот уже прошёл первую
    /// точку маршрута.
    /// </summary>
    private static List<Order> Carry(OrderList list, int position)
    {
        var carried = new List<Order>();

        for (int step = 0; step < OrderList.ChainLimit && list != null && position >= 0; step++)
        {
            for (int i = position; i < list.Count; i++)
                carried.Add(list.Items[i]);

            list = list.Next;
            position = 0;
        }

        return carried;
    }

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
    public bool Awaits(Order order)
    {
        if (order == null)
            return false;

        if (ReferenceEquals(Ahead(out _), order))
            return true;

        // Приказ кольца ждут всегда, даже пройденный: исполнитель вернётся к нему по кругу,
        // а убранный из ветки приказ вернуть было бы неоткуда
        return Ringed && InRing(order);
    }

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

        var marked = Marked();

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

        BuildPlan.ReleaseAbandoned(marked);
        return true;
    }

    /// <summary>
    /// Подписаться на ветку и встать в её начало, отпустив прежнюю.
    ///
    /// Вместе с брошенным остатком снимаются планы построек, до которых больше никто
    /// не дойдёт: отказ от приказа — отказ и от размеченного намерения.
    /// </summary>
    public void Adopt(OrderList list)
    {
        if (list == null || list == _list)
            return;

        var marked = Marked();

        Leave();

        _list = list;
        _done = null;
        list.Subscribe(this);
        _current = list.At(0);

        BuildPlan.ReleaseAbandoned(marked);
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

        var marked = Marked();
        var carried = new List<Order>(Remaining);

        Leave();

        _list = OrderList.Open();
        _list.Subscribe(this);

        foreach (var order in carried)
            _list.Add(order);

        _done = null;
        _current = _list.At(0);

        // Отделение приказов не теряет — весь остаток унесён в личную ветку, и проверка
        // находит их там же. Она стоит здесь не ради обычного хода дел, а ради того, чтобы
        // правило снятия было одним на все пути ухода из ветки, а не тремя из четырёх
        BuildPlan.ReleaseAbandoned(marked);
    }

    /// <summary>
    /// Уйти из ветки, никуда не переходя: исполнитель погиб либо каркас достроен и передал
    /// ветку преемнику. Планы, до которых после этого не дойдёт никто, снимаются — размеченное
    /// намерение живёт ровно столько, сколько живёт поручение.
    /// </summary>
    public void Clear()
    {
        var marked = Marked();

        Leave();

        BuildPlan.ReleaseAbandoned(marked);
    }

    /// <summary>
    /// Планы построек, поручённые этому исполнителю: снимок для проверки после ухода
    /// из ветки. Делается ДО ухода, потому что после него ни ветки, ни указателя уже нет,
    /// а проверка — ПОСЛЕ, потому что до подписки на новую ветку приказ не лежит нигде,
    /// и любой поручённый план выглядел бы брошенным.
    /// </summary>
    private List<BuildPlan> Marked()
    {
        List<BuildPlan> marked = null;

        foreach (var order in Remaining)
        {
            if (order.Kind != OrderKind.Build || order.Target is not BuildPlan { NeedsWork: true } plan)
                continue;

            marked ??= new List<BuildPlan>();

            if (!marked.Contains(plan))
                marked.Add(plan);
        }

        return marked;
    }

    /// <summary>
    /// Поручена ли исполнителю работа на этом месте: приказ на неё стоит под указателем
    /// либо впереди по цепочке веток. Пройденное сюда не входит — до него исполнитель
    /// уже не вернётся.
    ///
    /// СПРАШИВАЮТ СО СТОРОНЫ, ПОЭТОМУ УКАЗАТЕЛЬ НЕ ДВИГАЕТСЯ. Обращение к
    /// <see cref="Remaining"/> двигало бы чужие указатели при уборке планов, а уборка
    /// на это не вправе — по той же причине, по какой смотрит наперёд <see cref="Awaits"/>.
    /// </summary>
    public bool Assigned(IWorkSite site)
    {
        if (site == null)
            return false;

        var start = Ahead(out var list);

        if (start == null || list == null)
            return false;

        int position = list.IndexOf(start);

        for (int step = 0; step < OrderList.ChainLimit && list != null && position >= 0; step++)
        {
            for (int i = position; i < list.Count; i++)
            {
                var order = list.Items[i];

                if (order.Kind == OrderKind.Build && ReferenceEquals(order.Target, site))
                    return true;
            }

            list = list.Next;
            position = 0;
        }

        return false;
    }

    /// <summary>
    /// Шагнуть на следующий приказ. Список общий, поэтому шагает только указатель,
    /// а дойдя до конца ветки — переходит в пристёгнутую к ней.
    /// </summary>
    public void DropCurrent()
    {
        // Указатель мог остаться на приказе, снятом из ветки помимо нас. Шагать с него
        // некуда: место в ветке ищется заново при первом же обращении к текущему приказу
        Reseat();

        if (_list == null || _current == null)
            return;

        // Кольцо забирается себе ДО шага. После перехода в пристёгнутую ветку прежняя
        // остаётся без подписчиков и уходит из индекса, а с нею и начало маршрута:
        // возвращаться по кругу было бы уже некуда — см. Collapse
        if (Ringed && InRing(_current))
            Collapse();

        int position = _list.IndexOf(_current);
        var left = _list;
        var dropped = _current;

        _current.Dismiss(_owner.EntityId);
        _done = _current;

        // Приказа в ветке уже нет — его сняли уборкой по уходу цели; идти дальше не по чему
        _current = position < 0 ? null : _list.At(position + 1);

        Advance();

        // Пройденное держится в ветке, пока на него смотрит хоть кто-то ещё
        left.Compact();

        // Отказ от рабочего приказа тоже оставляет план без исполнителя: башня-сборщик
        // снимает приказ на место вне манипулятора, а исполнитель без инструмента — приказ,
        // который ему нечем выполнить. Проверка стоит только на этом случае: обычно рабочий
        // приказ снимают потому, что работа кончилась, и плана к тому мигу уже нет
        if (dropped.Kind == OrderKind.Build && dropped.Target is BuildPlan { NeedsWork: true } plan)
            BuildPlan.ReleaseAbandoned(new[] { plan });
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
        Reseat();

        if (_current != null || _list == null)
            return;

        var found = Seek(out var where);

        // Впереди пусто — но у кольца конца нет, и указатель возвращается к его началу
        if (found == null)
            found = Rewind(out where);

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
    private Order Ahead(out OrderList where)
    {
        if (!Seated)
            return Seek(out where);

        where = _list;
        return _current;
    }

    /// <summary>
    /// Стоит ли указатель на приказе, который в ветке ещё есть.
    ///
    /// Приказ уходит из ветки не только по воле его хозяина: когда цель покидает мир,
    /// приказы на неё снимаются у всех разом, и делает это тот исполнитель, которого
    /// уведомили первым. У остальных указатель остаётся на приказе, которого в ветке
    /// больше нет, и по номеру такой приказ уже не находится.
    /// </summary>
    private bool Seated => _current != null && _list != null && _list.IndexOf(_current) >= 0;

    /// <summary>
    /// Снять указатель со снятого приказа, чтобы место в ветке искалось заново.
    ///
    /// ЗАЧЕМ ЭТО НУЖНО. Обход остатка (<see cref="Remaining"/>) ищет текущий приказ по номеру
    /// и при неудаче возвращает пустоту. Пустой остаток означает «ветка доделана», и тогда
    /// исполнитель вставал в её конец и больше за неё не брался. Отсюда и получалось, что
    /// из группы строителей работу продолжал один: достроенный каркас отпускает исполнителей
    /// по очереди, первый снимает общий приказ из ветки, а всем следующим ветка кажется
    /// пройденной до конца — вместе с приказами на ещё не начатые планы.
    /// </summary>
    private void Reseat()
    {
        if (_current != null && !Seated)
            _current = null;
    }

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

        foreach (var order in orders)
            if (order == null || !Allows(order.Kind))
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
