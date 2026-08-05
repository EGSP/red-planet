using System.Collections.Generic;
using Godot;

/// <summary>
/// Ветка приказов: список приказов, подписчики, работающие по нему, и продолжение — ветка,
/// в которую подписчики переходят, закончив эту.
///
/// ЗАЧЕМ ОТДЕЛЬНОЙ СУЩНОСТЬЮ, А НЕ ПОЛЕМ ЮНИТА. Пока список принадлежал юниту, владельцем
/// времени жизни очереди оказывался последний живой её участник, а взять на неё ссылку
/// можно было только у него же. Отсюда три вещи, которых не хватало: подписать на работающую
/// очередь нового исполнителя было нечем, перечислить идущие в игре задачи негде, выписать
/// очередь в документ невозможно по той же причине.
///
/// ЗАЧЕМ ВЕТКИ СЦЕПЛЯЮТСЯ. Приказ по Shift достаётся тем, кто занят разным: у одного своя
/// очередь, у второго своя, третий свободен. Дописать его каждому по отдельности значит
/// размножить одно намерение по спискам — а оно одно. Поэтому дописанное заводится отдельной
/// веткой, а к ней ПРИСТЁГИВАЮТСЯ хвосты тех очередей, что её получили: каждый доделывает
/// своё и переходит в общую.
///
/// ПОДПИСЧИК — ЛИБО КУРСОР ИСПОЛНИТЕЛЯ, ЛИБО ДРУГАЯ ВЕТКА (<see cref="IOrderFollower"/>).
/// Список подписчиков поэтому один, а не два: ветка отвечает за своих участников ровно так же,
/// как курсор отвечает за себя, и различать их снаружи незачем. Пристегнуть ветку значит
/// подписать её — ссылка вперёд и подписка назад суть одно и то же отношение, записанное
/// с двух сторон.
///
/// ССЫЛКА СТАВИТСЯ ТОЛЬКО В ХВОСТ И ТОЛЬКО ВПЕРЁД. Кольцо здесь означало бы очередь,
/// которая держит саму себя и потому не умрёт никогда, — <see cref="LinkNext"/> его
/// не допускает.
///
/// СПИСОК ОБЩИЙ, УКАЗАТЕЛЬ ЛИЧНЫЙ. Двое работают по одной ветке, но каждый держит свой
/// указатель (см. <see cref="OrderQueue"/>): один уже взялся за второй приказ, пока другой
/// заканчивает первый. Поэтому приказ снимается из списка не тогда, когда его исполнил
/// первый, а когда мимо него прошли все — и когда сверху не осталось никого, кто до него
/// ещё дойдёт.
/// </summary>
public sealed class OrderList : IOrderFollower
{
    /// <summary>
    /// Предел длины цепочки при обходах. Защита от вырожденных случаев: кольца запрещены
    /// при пристёгивании, поэтому рабочим ограничением это число не является. Объявлено
    /// здесь одно на всю систему приказов — курсор считает по нему же.
    /// </summary>
    public const int ChainLimit = 64;

    private readonly List<Order> _items = new();

    /// <summary>Кто работает по ветке: курсоры исполнителей и ветки, ведущие сюда.</summary>
    private readonly List<IOrderFollower> _followers = new();

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
    /// Есть ли кому работать по ветке. Считается по всем подписчикам, а поскольку ветка,
    /// ведущая сюда, тоже подписчик, счёт получается по всей достижимости: пристёгнутая
    /// ветка живёт, пока живы исполнители тех очередей, что в неё ведут, даже если своих
    /// курсоров у неё пока нет ни одного.
    ///
    /// Условие живости для индекса, поэтому оно только отвечает и ничего не трогает:
    /// побочных действий предикат иметь не вправе.
    /// </summary>
    public bool Live
    {
        get
        {
            foreach (var follower in _followers)
                if (follower.Live)
                    return true;

            return false;
        }
    }

    /// <summary>
    /// Ответ ветки в роли подписчика: ждут ли здесь приказ ведомой ветки. Приказ этот лежит
    /// не у нас, а ниже по цепочке, и спрашивают о нём ровно затем, чтобы решить, можно ли
    /// его убрать. Пока здесь остаётся хоть один живой, убирать нельзя: перейдя, он встанет
    /// в начало ведомой ветки и до приказа дойдёт.
    /// </summary>
    public bool Awaits(Order order) => Live;

    /// <summary>
    /// Все ли участники — из числа перечисленных. Спрашивается по подписчикам, а значит
    /// и вверх по ссылкам: у общей ветки собственных курсоров может не быть вовсе — в неё
    /// приходят из пристёгнутых очередей, — и судить по пустому составу значило бы счесть
    /// своей ту ветку, куда придут посторонние.
    /// </summary>
    public bool Within(List<IOrderable> allowed)
    {
        foreach (var follower in _followers)
            if (!follower.Within(allowed))
                return false;

        return true;
    }

    /// <summary>
    /// Ветка, ведущая в другую, участников в состав её приказов не даёт: ждут друг друга
    /// только те, кто уже пришёл (см. <see cref="OrderQueue"/>). Войдут они в состав сами,
    /// когда подпишутся, — то есть когда действительно перейдут.
    /// </summary>
    public void Enlist(Order order)
    {
    }

    public void Dismiss(Order order)
    {
    }

    /// <summary>Ветка принадлежит одному этому подписчику и никуда не ведёт.</summary>
    public bool Only(IOrderFollower follower)
    {
        Prune();
        return _next == null && _followers.Count == 1 && _followers[0] == follower;
    }

    /// <summary>
    /// Пристегнуть ветку продолжением: поставить ссылку вперёд и подписаться на ту ветку,
    /// в которую она ведёт. Ставится только в хвост и только вперёд — ссылка на ветку,
    /// из которой эта достижима, замкнула бы цепочку в кольцо.
    /// </summary>
    public bool LinkNext(OrderList next)
    {
        if (next == null || next == this || _next != null || next.Reaches(this))
            return false;

        _next = next;
        next.Subscribe(this);
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
    /// Подписать. Подписка и есть присутствие в ветке, поэтому подписавшийся сразу входит
    /// в состав всех её приказов — и только её: то, что стоит ниже по цепочке, достанется
    /// ему тогда, когда он туда перейдёт.
    /// </summary>
    public void Subscribe(IOrderFollower follower)
    {
        if (follower == null || _followers.Contains(follower))
            return;

        _followers.Add(follower);

        foreach (var order in _items)
            follower.Enlist(order);
    }

    public void Unsubscribe(IOrderFollower follower)
    {
        if (follower == null || !_followers.Remove(follower))
            return;

        foreach (var order in _items)
            follower.Dismiss(order);
    }

    /// <summary>Дописать приказ в хвост ветки — всем, кто по ней сейчас работает.</summary>
    public void Add(Order order)
    {
        if (order == null)
            return;

        _items.Add(order);

        foreach (var follower in _followers)
            follower.Enlist(order);
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
    /// личные, и вынимать приказ из-под ещё работающего нельзя. Ветка, ведущая сюда, отвечает
    /// «ждут» на любой приказ, пока жива, — иначе пришедший позже нашёл бы список пустым.
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

    /// <summary>Смотрит ли на приказ хоть кто-то из подписчиков — или ещё дойдёт до него.</summary>
    private bool Awaited(Order order)
    {
        foreach (var follower in _followers)
            if (follower.Awaits(order))
                return true;

        return false;
    }

    private void Retire(Order order)
    {
        foreach (var follower in _followers)
            follower.Dismiss(order);
    }

    /// <summary>
    /// Убрать подписчиков, которых больше нет: погибшего исполнителя и опустевшую ветку,
    /// все участники которой уже перешли сюда. Обычно курсор отписывается сам, но сущность
    /// может уйти из мира и помимо этого пути.
    /// </summary>
    private void Prune()
    {
        for (int i = _followers.Count - 1; i >= 0; i--)
            if (!_followers[i].Live)
                _followers.RemoveAt(i);
    }
}

/// <summary>
/// Тот, кто работает по ветке приказов: курсор исполнителя (<see cref="OrderQueue"/>) либо
/// другая ветка, пристёгнутая к ней (<see cref="OrderList"/>).
///
/// ЗАЧЕМ ОДИН ПРИЗНАК НА ДВОИХ. У ветки было два раздельных списка — исполнители и ветки,
/// ведущие в неё, — и каждый вопрос приходилось задавать обоим: живость, состав, ожидание,
/// уборка, принадлежность выделению. Пять обходов, и все выписаны по два раза, хотя различие
/// между списками мнимое: ветка отвечает за своих участников ровно так же, как курсор
/// отвечает за себя. Курсор отвечает за одного, ветка — обходом собственных подписчиков,
/// то есть вверх по цепочке.
///
/// ПОДПИСЧИК — КУРСОР, А НЕ САМА СУЩНОСТЬ. Все три вопроса суть свойства указателя, а юнит
/// был бы в них лишь посредником, через которого ветка ходила бы к его очереди. Поэтому
/// <see cref="OrderList"/> о юнитах не знает вовсе.
/// </summary>
public interface IOrderFollower
{
    /// <summary>Есть ли ещё кому работать по этой подписке.</summary>
    bool Live { get; }

    /// <summary>Смотрит ли кто-то на этот приказ прямо сейчас или ещё дойдёт до него.</summary>
    bool Awaits(Order order);

    /// <summary>Все ли участники подписки — из числа перечисленных.</summary>
    bool Within(List<IOrderable> allowed);

    /// <summary>Войти в состав приказа ветки, на которую подписан, либо выйти из него.</summary>
    void Enlist(Order order);

    void Dismiss(Order order);
}
