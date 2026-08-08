using Godot;

/// <summary>
/// Юнит с очередью приказов. Движется к цели, а войдя в радиус инструмента —
/// подключается к узлу работы. Сам ресурсы не двигает: только сообщает свою мощность.
///
/// Он же цель для чужой стороны и он же носитель ствола, если ствол задан справочником.
/// Стрелять юнит начинает только без приказов: работа важнее, а огонь по площадям
/// вместо стройки — не то, чего ждёшь от отданного приказа.
///
/// Приказы юнит только ИСПОЛНЯЕТ. Кто их раздаёт — игрок через CommandSystem или мозговая
/// система — его не касается, а чего ему отдать нельзя, отсекает набор AllowedOrders.
///
/// ОДИН КЛАСС НА ОБЕ СТОРОНЫ. Отдельного класса Enemy больше нет: противник — такой же юнит,
/// и различие сторон выражается полем Faction, а не типом узла. Раньше два класса повторяли
/// друг за другом подход к цели, доворот, дистанцию остановки, гибель и отрисовку, причём
/// с расхождениями в мелочах, — а расходились они именно потому, что правку вносили в один
/// файл из двух. Из этого следует и правило для систем: сторону надо спрашивать у сущности,
/// а не выводить из того, в каком разрезе она нашлась.
/// </summary>
public partial class Unit : Node2D, IFacing, IDamageable, IArmed, IEconomyActor, IVision,
    IRepairable, IOrderable, IWorker, IMobile
{
    /// <summary>
    /// Определение. Ставит Spawner при создании: узел юнита собственной сцены не имеет,
    /// и связать его со справочником больше некому.
    /// </summary>
    public UnitDefinition Definition { get; set; }

    public int Id { get; set; }

    public OrderQueue Orders { get; }

    /// <summary>
    /// Намерение двигаться. Сам юнит себя не перемещает: он объявляет, куда хочет попасть,
    /// а путь, обход соседей и выталкивание из зданий делает <see cref="MovementSystem"/>.
    /// </summary>
    public Movement Movement { get; } = new();

    private WorkNode _attached;

    /// <summary>Сколько осталось до переигровки цели. Ведёт мозговая система, она же и решает.</summary>
    private float _retarget;

    /// <summary>
    /// Якорь внимания — см. <see cref="IWorker.Anchor"/>. Ставится при рождении и меняется
    /// только приказом игрока: занятие, выбранное самостоятельно, якоря не сдвигает,
    /// иначе отлучка за целью переносила бы участок вслед за юнитом.
    /// </summary>
    private Vector2 _anchor;

    /// <summary>
    /// Чужой приказ, в котором юнит помогает ведущему прямо сейчас. Своей очереди
    /// не принадлежит и в неё не попадает: помощь длится ровно пока идёт сопровождение.
    /// </summary>
    private Order _assisted;

    /// <summary>
    /// Направление инструмента в мировых радианах: ствол или манипулятор.
    /// Корпус при этом смотрит по <see cref="Node2D.Rotation"/> — туда, куда едет.
    /// </summary>
    private float _toolFacing;

    /// <summary>
    /// В этом физическом кадре инструмент уже доворачивали к цели. Без признака
    /// графический шаг снова тянул бы его к корпусу и гасил наведение.
    /// </summary>
    private bool _toolAimed;

    public Unit() => Orders = new OrderQueue(this);

    public Vector2 Anchor => _anchor;

    /// <summary>
    /// Юнита уже посылали. Признак нужен единственному правилу: приставленный к месту
    /// не бросает его ради коммандера, а тот, кому ничего не приказывали, идёт за ним,
    /// как и раньше. Без этого различия отряд, оставленный держать рубеж, уходил бы
    /// с него сам, едва закончив дело, — и якорь внимания не значил бы ничего.
    /// </summary>
    public bool Posted { get; private set; }

    /// <summary>
    /// Перенести якорь внимания. Зовёт тот, кто отдаёт приказ по воле игрока:
    /// <see cref="CommandSystem"/> и завод, раздающий новорождённым точку сбора.
    /// </summary>
    public void SetAnchor(Vector2 point)
    {
        _anchor = point;
        Posted = true;
    }

    /// <summary>
    /// Пора ли искать цель заново. Прежняя может быть и жива — просто рядом выросло что-то
    /// ближе, поэтому выбор переигрывается по таймеру, а не только по гибели цели.
    ///
    /// Свойство общее для обеих сторон: юнит игрока без работы выбирает цель по тем же
    /// правилам, что и юнит противника.
    /// </summary>
    public bool NeedsTarget => Orders.Idle || _retarget <= 0f;

    public void TickRetarget(double dt) => _retarget -= (float)dt;

    public void NoteTargeted() => _retarget = Const.RetargetDelay;

    public bool Idle => Orders.Idle;

    public Order Current => Orders.Current;

    public Health Health { get; protected set; }

    public WeaponState Gun { get; } = new();

    public int EntityId => Id;

    public string DefinitionId => Definition?.Id ?? "";

    public string DisplayName => Definition?.DisplayName ?? "юнит";

    /// <summary>
    /// Чья сторона. Ставит Spawner при создании, и после этого значение постоянно: разрезы
    /// индекса по стороне пересобираются раз в кадр и смены ключа посреди партии не терпят.
    ///
    /// Сторона не берётся из определения намеренно. Определение описывает, что сущность
    /// собой представляет, а кому она принадлежит — обстоятельство создания: один и тот же
    /// вид должна иметь возможность выставить любая сторона.
    /// </summary>
    public Faction Faction { get; set; } = Faction.Player;

    /// <summary>
    /// Откуда юнит взялся. Смысл имеет только у стороны противника: по этому признаку
    /// система давления отличает постоянный фон, который занимает место в бюджете,
    /// от волны, которая приходит поверх него. У стороны игрока значение не читается.
    /// </summary>
    public PressureOrigin Origin { get; set; } = PressureOrigin.Ambient;

    /// <summary>
    /// Приказы, которые очередь принимает. Состав задаёт секция <c>[orders]</c> определения
    /// в пересечении с тем, что юнит умеет по снабжению и классу.
    /// </summary>
    public virtual OrderSet AllowedOrders => Definition?.AcceptedOrders ?? OrderSet.None;

    /// <summary>
    /// Умеет, но в определении не разрешено — для панели со звёздочкой на время проверки
    /// полноты <c>[orders]</c>.
    /// </summary>
    public OrderSet SoftOrders => Definition?.SoftOrders ?? OrderSet.None;

    public SelectionGroup SelectionGroup => Definition?.SelectionGroup ?? SelectionGroup.Bots;

    /// <summary>
    /// Ось прицеливания: независимый инструмент, если умение это допускает,
    /// иначе направление корпуса.
    /// </summary>
    public float Facing => AimsIndependently ? _toolFacing : Rotation;

    /// <summary>Куда смотрит ствол или манипулятор прямо сейчас.</summary>
    public float ToolFacing => AimsIndependently ? _toolFacing : Rotation;

    /// <summary>
    /// Инструмент наводится отдельно от корпуса на ходу. Берётся из умения:
    /// ствол с <c>aim_while_moving</c> или рабочая рука с тем же признаком.
    /// </summary>
    private bool AimsIndependently =>
        Definition?.Weapon is { AimWhileMoving: true }
        || Definition?.BuildTool is { AimWhileMoving: true };

    public float HitRadius => Definition?.RadiusPx ?? Const.Unit * 0.35f;

    public float VisionRadius => Definition?.VisionRadiusPx ?? 0f;

    /// <summary>
    /// Курс ремонта. Раньше его приходилось складывать из двух определений — прочность
    /// брать из одного, цену из другого, — потому что юнит описывался двумя файлами.
    /// Теперь оба числа лежат рядом, и курс считает само определение.
    ///
    /// У коммандера секции сборки нет — значит нет и цены, а без цены нет курса,
    /// и чинить его нечем. Он и не нуждается: урон он копит, но не гибнет.
    /// </summary>
    public float HealthPerMetal => Definition?.HealthPerMetal ?? 0f;

    public WeaponDefinition Weapon => Definition?.Weapon;

    /// <summary>
    /// Занят делом: подключён к узлу работы или чинит. Такому не до стрельбы.
    /// </summary>
    private bool Busy => Alive.Is(_attached) || RepairTarget != null;

    /// <summary>
    /// Стреляем всегда, кроме как за работой. Прежде огонь вели только без приказов,
    /// и сопровождающий отряд молча шёл мимо противника, потому что приказ у него был.
    /// Сопровождение и движение стрельбе не мешают: прикрытие строителей — не отдельная
    /// задача, а то, что вооружённый делает попутно.
    ///
    /// Приказ атаки стреляет и на работе — но работать и атаковать одновременно нельзя,
    /// поэтому случай этот вырожденный и оставлен ради ясности правила.
    /// </summary>
    public virtual bool CanFire => Current?.Kind == OrderKind.Attack || !Busy;

    /// <summary>
    /// По приказу бьём именно назначенную жертву, даже если рядом мельтешит другая:
    /// приказ игрока не должен перебиваться выбором «кто ближе».
    /// </summary>
    public IDamageable FireTarget =>
        Current?.Kind == OrderKind.Attack ? Current.Entity as IDamageable : null;

    public override void _Ready()
    {
        Health ??= new Health(Definition?.MaxHealth ?? 80f);

        // Якорь ставится сразу: юнит, которому ещё ничего не приказывали, обязан
        // держаться места своего появления, а не расползаться за целями от него
        _anchor = GlobalPosition;
        _toolFacing = Rotation;

        QueueRedraw();
    }

    /// <summary>
    /// Выровнять инструмент по корпусу без доворота. Нужен после внешней установки
    /// <see cref="Node2D.Rotation"/> при рождении: иначе ствол остаётся на старом угле.
    /// </summary>
    public void SnapToolToBody()
    {
        _toolFacing = Rotation;
        _toolAimed = false;
    }

    /// <summary>
    /// Заявка в экономику. Чужая сторона своей экономики не ведёт, поэтому её юниты выходят
    /// сразу: ведомость в игре одна, и складывать в неё производство противника значило бы
    /// приписывать игроку чужой доход.
    /// </summary>
    public void Declare(EconomyLedger ledger)
    {
        if (Definition == null || Faction != Faction.Player)
            return;

        ledger.AddIncome(ResourceKind.Energy, Definition.EnergyProduction);
        ledger.AddIncome(ResourceKind.Metal, Definition.MetalProduction);

        // Ремонт — это и есть стройка тем же инструментом, поэтому и заявка та же:
        // метал по мощности руки плюс энергия на неё же
        if (RepairTarget != null && Definition.BuildTool is { } arm)
            Repair.Declare(ledger, arm.Power, arm.EnergyPerPower);
    }

    /// <summary>Своё производство идёт всегда, просадка производительности его не касается.</summary>
    public void Run(double dt, EconomyRates rates)
    {
        if (Definition == null || Faction != Faction.Player)
            return;

        var events = GameManager.I.Events;

        if (Definition.EnergyProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Energy,
                Amount = Definition.EnergyProduction * (float)dt,
            });

        if (Definition.MetalProduction > 0f)
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Metal,
                Amount = Definition.MetalProduction * (float)dt,
            });

        var target = RepairTarget;
        if (target != null && Definition.BuildTool is { } arm)
            Repair.Run(target, arm.Power, arm.EnergyPerPower, dt, rates);
    }

    public void AimAt(Vector2 point, double dt)
    {
        if (GlobalPosition.IsEqualApprox(point))
            return;

        float desired = Heading.AngleTo(GlobalPosition, point);
        float step = (Definition?.TurnSpeed ?? Mathf.Pi) * (float)dt;

        if (AimsIndependently)
        {
            _toolFacing = Heading.TurnToward(_toolFacing, desired, step);
            _toolAimed = true;
            return;
        }

        // Корпусный инструмент: на ходу корпус крутит движение, стоя — наведение
        if (Movement.Velocity.LengthSquared() > 0.0001f)
            return;

        Rotation = Heading.TurnToward(Rotation, desired, step);
        _toolFacing = Rotation;
        _toolAimed = true;
    }

    public override void _Process(double delta)
    {
        if (!_toolAimed)
            AlignTool(delta);

        _toolAimed = false;
        QueueRedraw();
    }

    /// <summary>
    /// Без цели инструмент возвращается к направлению корпуса. У корпусного оружия
    /// ось всегда совпадает с корпусом.
    /// </summary>
    private void AlignTool(double dt)
    {
        if (!AimsIndependently)
        {
            _toolFacing = Rotation;
            return;
        }

        float step = (Definition?.TurnSpeed ?? Mathf.Pi) * (float)dt;
        _toolFacing = Heading.TurnToward(_toolFacing, Rotation, step);
    }

    /// <summary>Приказов нет — отпускаем узел работы и стоим.</summary>
    public void OnIdle(double dt)
    {
        _assisted = null;
        Detach();
    }

    /// <summary>
    /// Отработать приказ за кадр. Снимать невыполнимое с головы очереди не наше дело —
    /// это уже сделала OrderSystem, сюда приказ приходит заведомо годным.
    /// </summary>
    public void RunOrder(Order order, double dt)
    {
        if (Definition == null)
            return;

        // Помощь ведущему живёт ровно один кадр и подтверждается заново: приказ
        // сопровождения мог смениться, а ведущий — закончить работу
        _assisted = null;

        switch (order.Kind)
        {
            case OrderKind.Move:
                RunMove(order, dt);
                return;

            case OrderKind.Attack:
                RunAttack(order, dt);
                return;

            case OrderKind.Repair:
                RunRepair(order, dt);
                return;

            case OrderKind.Follow:
                RunFollow(order, dt);
                return;

            case OrderKind.Delete:
                Demolish();
                return;

            default:
                RunWork(order, dt);
                return;
        }
    }

    /// <summary>
    /// Снос через тот же канал, что и гибель от урона: документ DamageDealt разбирает
    /// DamageSystem в React. Прямой QueueFree из Simulate оставил бы журнал без следа.
    /// </summary>
    private void Demolish()
    {
        if (Health == null || Health.IsDead || IsQueuedForDeletion())
            return;

        float amount = Mathf.Max(Health.Current, 1f);
        GameManager.I.Events.Append(new DamageDealt
        {
            TargetId = Id,
            SourceId = Id,
            Amount = amount,
            Pos = GlobalPosition,
        });
    }

    /// <summary>
    /// Движение с ожиданием отставших.
    ///
    /// ОТРЯД СОБИРАЕТСЯ, А НЕ РАСТЯГИВАЕТСЯ. Приказ движения, отданный нескольким, снимается
    /// только тогда, когда дошли все его участники: дошедший первым стоит и ждёт. Иначе
    /// быстрый юнит уходил бы к следующему приказу цепочки в одиночку, и отряд, посланный
    /// в две точки подряд, прибывал бы во вторую по частям.
    ///
    /// Ждать не приходится тому, кто получил приказ один: он сам себе весь состав. Копии
    /// приказа, которые завод раздаёт новорождённым, состав тоже не разделяют — см.
    /// <see cref="Order"/>.
    ///
    /// Приказ считается исполненным и тогда, когда ближе не пройти из-за своих: всему отряду
    /// в одну точку не поместиться, и ждать этого бессмысленно.
    /// </summary>
    private void RunMove(Order order, double dt)
    {
        Detach();

        float reach = Const.Unit * 0.2f;

        if (!Movement.Settled && GlobalPosition.DistanceTo(order.Pos) > reach)
        {
            Movement.Seek(order.Pos, reach);
            return;
        }

        order.Arrive(Id);

        // Стоим на месте: неподтверждённое намерение движения гаснет само,
        // и юнит удерживает позицию, пока подтягиваются остальные
        if (order.PartyReady)
            Orders.DropCurrent();
    }

    /// <summary>
    /// Стройка: дойти до места работы, а дойдя — поставить каркас, если это план,
    /// или подключиться к нему инструментом, если это уже каркас.
    ///
    /// Приказ объявляет намерение и проверяет, дошли ли. Сам ход и доворот корпуса
    /// на ходу делает система движения: обходя препятствие, юнит едет не туда, куда
    /// его послали, и разворот к цели выглядел бы движением боком вперёд.
    /// </summary>
    private void RunWork(Order order, double dt)
    {
        var target = order.Target.GlobalPosition;

        // Дальность принадлежит инструменту, а не юниту: раньше одно число служило всем
        // занятиям сразу, хотя тянутся они на разное
        var tool = Definition.BuildTool;
        float reach = tool?.RangePx ?? Const.Unit;

        // Дистанция меряется до КРАЯ места работы, а не до его середины: строитель,
        // вставший по диагонали от постройки, дотягивается до её угла, и отвергать его
        // на этом основании нельзя. Поправку на габарит цели держит Reach, а сюда она
        // приходит уже перенесённой на расстояние от центра — движение ведёт к центру
        float stop = Reach.StopDistance(GlobalPosition, order.Body, reach);

        if (GlobalPosition.DistanceTo(target) > stop)
        {
            Detach();
            Movement.Seek(target, stop);
            return;
        }

        // Дошли: разворачиваемся к тому, с чем работаем
        AimAt(target, dt);
        order.Arrive(Id);

        if (tool == null)
        {
            Orders.DropCurrent();
            return;
        }

        // План неосязаем и мощности не принимает: его нужно превратить в каркас.
        //
        // Раннего выхода здесь нет намеренно: воплотившись, план объявляет о смене, и цель
        // приказа к следующей строке — уже каркас, к которому можно подключиться тем же
        // кадром. Если же место оказалось занято и план тихо отменился, целью остался он сам,
        // и подключаться не к чему — проверка ниже отработает вхолостую
        if (order.Target is BuildPlan plan)
        {
            Detach();
            plan.Realize();
        }

        if (order.Target is WorkNode node && _attached != node)
        {
            Detach();

            // Узлу сообщаем и мощность, и прожорливость: энергию тратит инструмент,
            // а сколько именно — записано в нём же
            node.AttachWorker(Id, tool.Power, tool.EnergyDrain);
            _attached = node;
        }
    }

    /// <summary>
    /// Приказ атаковать: подойти на дальность своего ствола и держаться. Сам выстрел —
    /// дело WeaponSystem, сюда попадает только подход.
    ///
    /// В пределах дальности инструмент крутит система стрельбы; корпус на ходу
    /// по-прежнему доворачивает система движения. Своего доворота здесь нет.
    /// </summary>
    private void RunAttack(Order order, double dt)
    {
        Detach();

        var victim = order.Entity;
        var target = victim as IDamageable;
        var to = victim.GlobalPosition;

        // Поводок: цель, которую юнит выбрал себе сам, не уводит его дальше внимания
        // от якоря. Прямое указание игрока поводка не имеет и ведёт куда угодно
        if (order.Leashed && _anchor.DistanceTo(to) > Definition.AttentionRadiusPx)
        {
            Orders.DropCurrent();
            return;
        }

        // Подходим до дистанции, заведомо лежащей ВНУТРИ огневой границы, а не до самой
        // границы. Остановка по признаку «уже достаю» оставляла юнита ровно на краю,
        // откуда любое смещение цели или толчок соседа выводили его из радиуса.
        // Безоружный подходит на длину инструмента: приказ хотя бы не зависает
        float stop = Weapon != null
            ? Targeting.ApproachDistance(Weapon, GlobalPosition, target,
                Definition.ApproachHoldFraction)
            : Reach.StopDistance(GlobalPosition, victim, Definition.WorkRangePx);

        if (GlobalPosition.DistanceTo(to) > stop)
            Movement.Seek(to, stop);
    }

    /// <summary>
    /// Ремонт: подойти на длину инструмента и стоять. Само восстановление прочности идёт
    /// в Run — ремонт стоит ресурсов и потому проходит через экономику, как и стройка.
    /// </summary>
    private void RunRepair(Order order, double dt)
    {
        Detach();

        var to = order.Entity.GlobalPosition;
        float reach = Definition.BuildTool?.RangePx ?? Definition.WorkRangePx;
        float stop = Reach.StopDistance(GlobalPosition, order.Body, reach);

        if (GlobalPosition.DistanceTo(to) > stop)
            Movement.Seek(to, stop);
        else
            AimAt(to, dt);
    }

    /// <summary>
    /// Сопровождение: держаться рядом, но не наступать на пятки.
    ///
    /// СОПРОВОЖДЕНИЕ ВКЛЮЧАЕТ ПОМОЩЬ. Строитель, приставленный к строителю, берётся
    /// за то же дело, что и ведущий, — в том числе когда ведущий сам сопровождает
    /// другого строителя: помощь идёт по цепочке Follow вглубь.
    ///
    /// ОЧЕРЕДЬ ПОСЛЕ FOLLOW. Пока помощь или прикрытие заняты делом, сопровождение
    /// держится. Иначе, если за Follow уже стоит следующий приказ, сопровождение
    /// снимается, как только исполнитель дошёл до ведущего или ведущий без дела:
    /// иначе Shift-цепочка после Follow никогда бы не началась.
    /// </summary>
    private void RunFollow(Order order, double dt)
    {
        if (Assist(order, dt) || Guard(order))
            return;

        Detach();

        if (Orders.HasMore && FollowDone(order))
        {
            Orders.DropCurrent();
            return;
        }

        var to = order.Entity.GlobalPosition;
        float range = FollowRange;

        if (GlobalPosition.DistanceTo(to) > range)
            Movement.Seek(to, range);
    }

    /// <summary>
    /// Дистанция сопровождения: 2.5 собственных размера. Размер — диаметр корпуса
    /// (два радиуса), поэтому стоп = <c>5 · HitRadius</c>.
    /// </summary>
    private float FollowRange => HitRadius * 5f;

    /// <summary>
    /// Сопровождение исчерпано для очереди с хвостом: либо уже рядом с ведущим,
    /// либо ведущему нечего делать и ждать больше нечего.
    /// </summary>
    private bool FollowDone(Order order)
    {
        if (!Alive.Is(order.Entity))
            return true;

        if (GlobalPosition.DistanceTo(order.Entity.GlobalPosition) <= FollowRange)
            return true;

        return order.Entity is IOrderable { Orders.Idle: true };
    }

    /// <summary>
    /// Прикрытие ведущего: подойти к противнику на дистанцию огня и держаться, пока тот
    /// не уйдёт. Приказ сопровождения при этом не тратится и не отменяется, поэтому
    /// сопровождающий возвращается к ведущему сам, как только стрелять станет не в кого.
    ///
    /// ЗОНА ИНТЕРЕСА ОТСЧИТЫВАЕТСЯ ОТ ВЕДУЩЕГО, А НЕ ОТ СЕБЯ. Отсчёт от себя означал бы, что
    /// каждый шаг за целью расширяет область поиска, и охранение утягивалось бы за одиночкой
    /// через всю карту — ровно то, ради чего заведён якорь внимания.
    ///
    /// Стрельбу отсюда никто не ведёт: она разрешена самим фактом сопровождения
    /// (см. <see cref="CanFire"/>), а стреляет <see cref="WeaponSystem"/>.
    /// </summary>
    private bool Guard(Order order)
    {
        if (Weapon == null || !Alive.Is(order.Entity))
            return false;

        var center = order.Entity.GlobalPosition;

        if (Targeting.Nearest(center, Faction.Opposite(), Definition.AttentionRadiusPx)
            is not Node2D victim)
            return false;

        Detach();

        float stop = Targeting.ApproachDistance(Weapon, GlobalPosition, victim as IDamageable,
            Definition.ApproachHoldFraction);

        if (GlobalPosition.DistanceTo(victim.GlobalPosition) > stop)
            Movement.Seek(victim.GlobalPosition, stop);

        return true;
    }

    /// <summary>
    /// Помощь ведущему: делать то же, что делает он, пока это в пределах внимания.
    ///
    /// ЧУЖОЙ ПРИКАЗ НЕ ПОПАДАЕТ В СВОЮ ОЧЕРЕДЬ. Помощь — не приказ, а поведение внутри
    /// сопровождения: ведущий бросит работу, и помощник тут же вернётся к нему, ничего
    /// не отменяя. Попади приказ в очередь — его пришлось бы оттуда вынимать, а до тех пор
    /// помощник считался бы занятым и сопровождение бы потерял.
    ///
    /// ЦЕПОЧКА СОПРОВОЖДЕНИЯ. Ведущий, который сам идёт по <see cref="OrderKind.Follow"/>,
    /// считается занятым тем же делом, что и его ведущий: иначе помощник смотрел бы
    /// только на приказ сопровождения и к стройке не подключался. Искать «исходного»
    /// строителя снаружи не нужно — разбор идёт по ссылкам Follow вглубь.
    ///
    /// Предел внимания отсчитывается от непосредственного ведущего: помощник ходит за
    /// ним, а не за его работой, и уходить от него дальше, чем видит, не должен.
    /// </summary>
    private bool Assist(Order order, double dt)
    {
        if (Definition.BuildTool == null || order.Entity is not IOrderable leader)
            return false;

        var work = AssistedWork(leader);

        if (work == null || !Orders.Allows(work.Kind))
            return false;

        var point = work.Point;

        if (order.Entity.GlobalPosition.DistanceTo(point) > Definition.AttentionRadiusPx)
            return false;

        _assisted = work;

        // До края того, над чем работает ведущий, а не до середины: помощник, оказавшийся
        // с дальней стороны каркаса, дотягивается до ближней к нему стены
        float stop = Reach.StopDistance(GlobalPosition, work.Body, Definition.BuildTool.RangePx);

        if (GlobalPosition.DistanceTo(point) > stop)
        {
            Detach();
            Movement.Seek(point, stop);
            return true;
        }

        AimAt(point, dt);

        // Ремонт идёт через RepairTarget, а каркас ставит сам ведущий: помощнику
        // остаётся подключиться к тому, что уже стоит
        if (work.Target is WorkNode node && _attached != node)
        {
            Detach();
            node.AttachWorker(Id, Definition.BuildTool.Power, Definition.BuildTool.EnergyDrain);
            _attached = node;
        }
        else if (work.Kind == OrderKind.Repair)
        {
            Detach();
        }

        return true;
    }

    /// <summary>
    /// Стройка или ремонт, которым занят ведущий, в том числе через цепочку Follow.
    /// Предел глубины отсекает циклы A→B→A без выделения списка на каждый кадр.
    /// </summary>
    private Order AssistedWork(IOrderable leader)
    {
        const int depthLimit = 16;

        for (int i = 0; i < depthLimit; i++)
        {
            var work = leader.Orders.Current;

            if (work == null)
                return null;

            if (work.Kind is OrderKind.Build or OrderKind.Repair)
                return work;

            if (work.Kind != OrderKind.Follow || work.Entity is not IOrderable next)
                return null;

            // Цикл, вернувшийся к помощнику, или сопровождение самого себя
            if (ReferenceEquals(next, this) || ReferenceEquals(next, leader))
                return null;

            leader = next;
        }

        return null;
    }

    /// <summary>
    /// Кого чиним прямо сейчас: свой приказ ремонта либо чужой, в котором помогаем.
    /// Цель обязана быть в пределах инструмента — иначе чинить нечего.
    /// </summary>
    private IRepairable RepairTarget => Repairing(Current) ?? Repairing(_assisted);

    private IRepairable Repairing(Order order)
    {
        if (order?.Kind != OrderKind.Repair || Definition == null || !Definition.CanRepair)
            return null;

        if (order.Entity is not IRepairable repairable || !Targeting.IsValid(order.Entity))
            return null;

        if (order.Entity is Unit && !Definition.CanRepairUnits)
            return null;

        // САМ СЕБЯ НЕ ЧИНИТ НИКТО. Инструмент чинит то, до чего дотягивается, а не своего
        // носителя, иначе любой ремонтник превращал бы метал в собственную прочность
        // сколько угодно долго. Правило живёт здесь, потому что здесь единственное место,
        // через которое проходят все пути: и свой приказ, и чужой, в котором помогают, —
        // а помощь ведущему, чинящему как раз этого помощника, ни одним отсевом
        // при выдаче приказа не ловится.
        //
        // Источники приказа отсеивают этот случай и сами (CommandSystem.IssueOrder,
        // Jobs.NearestDamaged): без этого приказ выдавался бы и висел в очереди, ничего
        // не делая, — а исполнитель считался бы занятым
        if (ReferenceEquals(order.Entity, this))
            return null;

        // Запас в пиксель: проверка обязана соглашаться там, где остановка уже разрешена,
        // а движение встаёт не ровно в заданной точке
        float reach = Definition.BuildTool.RangePx + 1f;
        return Reach.Within(GlobalPosition, order.Entity, reach) ? repairable : null;
    }

    private void Detach()
    {
        if (Alive.Is(_attached))
            _attached.DetachWorker(Id);

        _attached = null;
    }

    /// <summary>Узел работы уходит из игры — забываем его, не касаясь мёртвой ноды.</summary>
    public void OnTargetLost(WorkNode node)
    {
        if (_attached == node)
            _attached = null;

        Orders.DropAllFor(node);
    }

    public override void _ExitTree() => Detach();

    /// <summary>Прочность кончилась: отпустить узел работы. EntityStore снимает Spawner.</summary>
    public virtual void OnDestroyed()
    {
        Detach();
        Orders.Clear();

        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем.
        // Реестр по id чистит подписка Spawner на выбытие из индекса.
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Definition == null)
            return;

        float radius = Definition.RadiusPx;

        float toolLocal = ToolFacing - Rotation;

        UnitGizmos.Draw(this, GizmoTools.From(Definition), Faction,
            selected: GizmoGate.IsSelected(this),
            facingOffset: toolLocal);

        UnitSilhouette.Draw(this, Definition, radius, toolLocal);

        HealthBar.Draw(this, Health, radius * 2.4f, -radius - 10f, Rotation);

        // Луч к узлу работы — это «работа идёт», а не приказ: очередь рисует оверлей
        if (Alive.Is(_attached))
            ShapeDraw.Line(this, Vector2.Zero, ToLocal(_attached.GlobalPosition),
                DrawTheme.Line(VizKind.WorkBeamBuild));
    }
}
