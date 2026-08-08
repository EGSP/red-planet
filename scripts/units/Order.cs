using System.Collections.Generic;
using Godot;

public enum OrderKind
{
    Move,
    Build,
    Attack,
    Repair,
    Follow,
    Delete,
}

/// <summary>
/// Приказ. Рабочие приказы держат ссылку на место работы, движение — точку,
/// а атака, ремонт и сопровождение — сущность.
///
/// Сущность хранится нодой, а не интерфейсом: живость проверяется у ноды,
/// а нужную грань (прочность, радиус) достаём приведением там, где она понадобилась.
///
/// Приказ — НЕ документ. Документ это свершившийся факт, его нельзя отменить; приказ —
/// намерение, и он выбрасывается из очереди, как только стал невыполнимым.
///
/// ОДИН ПРИКАЗ РАЗДАЁТСЯ НЕСКОЛЬКИМ. Отряд, получивший приказ одним щелчком, получает
/// не копии, а один и тот же объект: состояние исполнения записано здесь же и потому
/// у всех общее. Отсюда два следствия, ради которых это и сделано. Первое — подошедший
/// вторым видит, что работа уже начата, и включается в неё. Второе — приказ движения
/// умеет дожидаться отставших, потому что знает всех своих участников поимённо.
///
/// Гибель одного исполнителя приказа не рушит: он не принадлежит никому в отдельности,
/// а очередь у каждого своя. Завод же раздаёт новорождённым КОПИИ (см. Plant.CloneOrder) —
/// иначе юнит, выехавший из корпуса позже, заставил бы весь отряд ждать себя.
/// </summary>
public sealed class Order
{
    public OrderKind Kind;
    public Vector2 Pos;

    private IWorkSite _target;

    /// <summary>
    /// Место работы. Свойство, а не поле, потому что приказ подписывается на объявление
    /// цели о собственной смене: план становится каркасом, и цель переезжает на преемника
    /// (см. <see cref="OnSuperseded"/>).
    /// </summary>
    public IWorkSite Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value))
                return;

            if (_target != null)
                _target.Superseded -= OnSuperseded;

            _target = value;

            if (_target != null)
                _target.Superseded += OnSuperseded;
        }
    }

    /// <summary>Кого бьём, кого чиним или за кем идём — смотря какой приказ.</summary>
    public Node2D Entity;

    /// <summary>
    /// Приказ выдан не игроком, а системой распределения задач. Такой приказ не уводит
    /// исполнителя дальше его радиуса внимания: самостоятельно выбранная цель не повод
    /// бросать участок, тогда как прямое указание игрока — повод.
    /// </summary>
    public bool Leashed;

    /// <summary>Кому приказ роздан. Нужен приказу движения, чтобы дождаться отставших.</summary>
    private readonly HashSet<int> _party = new();

    /// <summary>Кто уже на месте.</summary>
    private readonly HashSet<int> _arrived = new();

    public static Order MoveTo(Vector2 pos) => new() { Kind = OrderKind.Move, Pos = pos };

    public static Order Work(OrderKind kind, IWorkSite target) => new()
    {
        Kind = kind,
        Target = target,
        Pos = target.GlobalPosition,
    };

    public static Order Attack(Node2D victim) => new()
    {
        Kind = OrderKind.Attack,
        Entity = victim,
        Pos = victim.GlobalPosition,
    };

    public static Order Repair(Node2D target) => new()
    {
        Kind = OrderKind.Repair,
        Entity = target,
        Pos = target.GlobalPosition,
    };

    /// <summary>
    /// Сопровождение: одиночный приказ живёт, пока цель жива; с хвостом очереди
    /// снимается при рандеву или простое ведущего (см. <see cref="Unit.RunFollow"/>).
    /// </summary>
    public static Order Follow(Node2D leader) => new()
    {
        Kind = OrderKind.Follow,
        Entity = leader,
        Pos = leader.GlobalPosition,
    };

    /// <summary>
    /// Снос: исполнитель уничтожает себя. Цели нет — приказ держит только вид.
    /// Урон уходит документом <c>DamageDealt</c>, гибель разбирает DamageSystem в React.
    /// </summary>
    public static Order Delete() => new() { Kind = OrderKind.Delete };

    /// <summary>
    /// Место работы сменилось преемником: план поставил на себе каркас и уходит из мира.
    ///
    /// ПРИКАЗ НЕ ЗАМЕНЯЕТСЯ И НЕ ДОПИСЫВАЕТСЯ — у него переезжает цель. План и каркас суть
    /// две стадии одной постройки, а не две разные задачи, поэтому в очереди им положен
    /// один шаг, и шаг этот живёт от разметки до готового строения. Приказ остаётся тем же
    /// объектом на том же месте во всех ветках, где он значится, поэтому ни искать эти
    /// ветки, ни вставлять в них что-либо не требуется.
    ///
    /// Состав и прибывшие сохраняются: исполнитель, стоящий у плана, стоит и у каркаса,
    /// и подходить ему заново некуда.
    /// </summary>
    private void OnSuperseded(IWorkSite successor)
    {
        if (successor == null)
            return;

        Target = successor;
        Pos = successor.GlobalPosition;
    }

    /// <summary>Приказ роздан ещё одному исполнителю. Зовёт очередь при постановке.</summary>
    public void Enlist(int actorId) => _party.Add(actorId);

    /// <summary>Исполнитель ушёл с приказа: снять его и с состава, и с прибывших.</summary>
    public void Dismiss(int actorId)
    {
        _party.Remove(actorId);
        _arrived.Remove(actorId);
    }

    /// <summary>Исполнитель на месте. Повторный вызов ничего не меняет.</summary>
    public void Arrive(int actorId) => _arrived.Add(actorId);

    /// <summary>
    /// Все ли участники дошли.
    ///
    /// ПОГИБШИЕ В СЧЁТ НЕ ИДУТ: ждать того, кого больше нет, значило бы, что отряд навсегда
    /// встаёт от одной потери. Живость спрашиваем у реестра сущностей — состав фиксировался
    /// идентификаторами, а не ссылками.
    ///
    /// НЕПОДВИЖНЫЕ ТОЖЕ НЕ ИДУТ В СЧЁТ. Очередь бывает общей у отряда и завода, которому
    /// та же точка служит местом сбора; дойти до неё завод не может никогда, и ждать его
    /// означало бы, что отряд не тронется с места вовсе.
    /// </summary>
    public bool PartyReady => Awaited == 0;

    /// <summary>Сколько участников ещё в пути — для подписи в очереди приказов.</summary>
    public int Awaited
    {
        get
        {
            var entities = GameManager.I?.Entities;

            if (entities == null)
                return 0;

            int count = 0;

            foreach (int id in _party)
                if (!_arrived.Contains(id) && entities.Get(id) is IMobile)
                    count++;

            return count;
        }
    }

    /// <summary>Взялся ли за приказ хоть кто-то. По этому признаку видно начатую работу.</summary>
    public bool Started => _arrived.Count > 0;

    /// <summary>Этот исполнитель уже на месте и стоит, дожидаясь отставших.</summary>
    public bool Waiting(int actorId) => _arrived.Contains(actorId) && !PartyReady;

    /// <summary>
    /// Куда приказ ведёт прямо сейчас. У движения это заданная точка, у остальных —
    /// текущее положение цели: она могла с тех пор уйти, и путь тянется за ней.
    /// </summary>
    public Vector2 Point
    {
        get
        {
            if (Target is Node2D site && Alive.Is(site))
                return site.GlobalPosition;

            if (Alive.Is(Entity))
                return Entity.GlobalPosition;

            return Pos;
        }
    }

    /// <summary>
    /// Сущность, стоящая в точке приказа, — если она там вообще есть. Отдаётся расчёту
    /// досягаемости (<see cref="Reach"/>), которому мало координат: дотягивается инструмент
    /// до края цели, а край задаёт её форма. У приказа движения сущности нет, и поправки
    /// на габарит там тоже нет — точка есть точка.
    /// </summary>
    public object Body
    {
        get
        {
            if (Target is Node2D site && Alive.Is(site))
                return site;

            return Alive.Is(Entity) ? Entity : null;
        }
    }

    /// <summary>
    /// Выполним ли приказ ещё. Проверка одна на всех исполнителей и живёт здесь, а не
    /// в сущности: невыполнимость — свойство самого приказа, а не того, кто его получил.
    /// Снимает приказы с головы очереди OrderSystem.
    /// </summary>
    public bool IsValid()
    {
        switch (Kind)
        {
            case OrderKind.Move:
            case OrderKind.Delete:
                return true;

            // Цель пала — приказ исчерпан, исполнитель возвращается к обычному поведению
            case OrderKind.Attack:
                return Targeting.IsValid(Entity);

            // Починили — работа закончена сама собой
            case OrderKind.Repair:
                return Targeting.IsValid(Entity)
                       && Entity is IRepairable { Health.Ratio: < 0.999f };

            case OrderKind.Follow:
                return Alive.Is(Entity) && !Entity.IsQueuedForDeletion();
        }

        // Каркас достроен, план израсходован или сорван, сама нода ушла из мира
        return Target is Node2D node && Alive.Is(node) && !node.IsQueuedForDeletion()
               && Target.NeedsWork;
    }

    /// <summary>Название для интерфейса.</summary>
    public static string Name(OrderKind kind) => kind switch
    {
        OrderKind.Move => "идти",
        OrderKind.Build => "строить",
        OrderKind.Attack => "атаковать",
        OrderKind.Repair => "чинить",
        OrderKind.Follow => "следовать",
        OrderKind.Delete => "снос",
        _ => kind.ToString(),
    };

    /// <summary>Смысл отрисовки приказа — один и тот же в очереди и в подсказках.</summary>
    public static VizKind Viz(OrderKind kind) => kind switch
    {
        OrderKind.Move => VizKind.OrderMove,
        OrderKind.Build => VizKind.OrderBuild,
        OrderKind.Attack => VizKind.OrderAttack,
        OrderKind.Repair => VizKind.OrderRepair,
        OrderKind.Follow => VizKind.OrderFollow,
        OrderKind.Delete => VizKind.OrderDelete,
        _ => VizKind.OrderMove,
    };

    /// <summary>Цвет линии приказа на карте — один и тот же в очереди и в подсказках.</summary>
    public static Color Tint(OrderKind kind) => DrawTheme.Hue(Viz(kind));
}
