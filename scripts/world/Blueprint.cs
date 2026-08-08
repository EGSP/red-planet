using System.Collections.Generic;
using Godot;

/// <summary>
/// Каркас будущей постройки. Появляется в мире сразу при постановке.
/// Сам тянет ресурсы из общего хранилища пропорционально суммарной мощности строителей.
/// Не хватает ресурсов — стройка замедляется, а не встаёт.
///
/// Каркас — тоже цель для врага, и прочности у него доля от готовой постройки
/// (Const.BlueprintHealthFactor): стройка под обстрелом должна быть рискованной затеей.
///
/// КАРКАС ПРИНИМАЕТ ПРИКАЗЫ И ЗАКАЗЫ НАРАВНЕ С ГОТОВОЙ СУЩНОСТЬЮ. Выделить его можно,
/// снести приказом Delete — тоже, а всё прочее, что примет достроенная сущность, каркас
/// хранит до готовности и передаёт ей (см. <see cref="Bequeath"/>). Стройка занимает время,
/// и требовать от игрока вернуться к постройке после её окончания значит терять это время:
/// точка сбора завода и состав его производства назначаются сразу.
///
/// САМ КАРКАС НЕ ИСПОЛНЯЕТ НИЧЕГО, КРОМЕ СНОСА. Он неподвижен, безоружен и ничего не
/// производит, поэтому приказ стоит на голове его очереди всё время стройки — и потому же
/// он виден игроку в очереди приказов сразу после назначения.
/// </summary>
public partial class Blueprint : WorkNode, IFacing, IDamageable, IVision, IObstacle,
    IOrderable, IProducer
{
    public UnitDefinition Definition { get; private set; }
    public float Progress { get; private set; }

    /// <summary>
    /// Заказанное производство: ключи справочника в порядке набора. У каркаса это
    /// намерение и ничего более — собирать юнитов ему нечем, и очередь просто ждёт
    /// достройки завода.
    /// </summary>
    private readonly List<string> _production = new();

    /// <summary>
    /// Срочные заказы, набранные до достройки. Отдельно от основной очереди по той же
    /// причине, что и у завода: они расходуются первыми и по кругу не идут.
    /// </summary>
    private readonly List<string> _rush = new();

    public OrderQueue Orders { get; }

    public Blueprint() => Orders = new OrderQueue(this);

    /// <summary>Угол, под которым каркас поставили. Достроенная сущность его наследует.</summary>
    public float BodyFacing { get; private set; }

    /// <summary>Место занято уже каркасом: перекрыть начатую стройку нельзя.</summary>
    public Obb Footprint => Definition == null
        ? new Obb(GlobalPosition, Vector2.Zero)
        : Placement.Footprint(Definition, GlobalPosition, BodyFacing);

    public Health Health { get; private set; }

    public override bool NeedsWork => Definition != null && Progress < Definition.TotalWork;

    public float Ratio => Definition == null || Definition.TotalWork <= 0f ? 0f : Progress / Definition.TotalWork;

    public int EntityId => Id;

    /// <summary>Каркас ёмкости хранилища ещё не даёт, поэтому в документ о гибели ключ не идёт.</summary>
    public string DefinitionId => "";

    /// <summary>
    /// Имя для интерфейса. Названо отлично от готовой постройки намеренно: по этой же строке
    /// работают сведение выделения в панели и выбор всех однотипных двойным щелчком, и путать
    /// начатую стройку с готовым строением там нельзя.
    /// </summary>
    public string DisplayName => Definition == null
        ? "каркас"
        : $"каркас: {Definition.DisplayName}";

    /// <summary>
    /// Что каркас принимает в очередь: всё, что примет достроенная сущность, плюс снос.
    /// Снос добавляется помимо определения, потому что относится он к самому каркасу,
    /// а не к тому, что из него получится: начатую стройку игрок вправе отменить всегда.
    /// </summary>
    public OrderSet AllowedOrders =>
        (Definition?.AcceptedOrders ?? OrderSet.None).With(OrderKind.Delete);

    /// <summary>Исполнимое, но не объявленное — панель помечает звёздочкой.</summary>
    public OrderSet SoftOrders =>
        (Definition?.SoftOrders ?? OrderSet.None).Except(AllowedOrders);

    /// <summary>Каркас неподвижен и рамкой выделяется вместе с постройками.</summary>
    public SelectionGroup SelectionGroup => SelectionGroup.Structures;

    /// <summary>
    /// Заказы принимает только каркас завода. У прочих каркасов очередь производства
    /// остаётся пустой: панель производства к ним не относится, хотя строительный
    /// раздел в определении бывает и у них.
    /// </summary>
    public bool CanProduce => Definition?.Plant != null;

    /// <summary>Режим производства, назначенный до постройки. Умолчание то же, что у завода.</summary>
    public bool Infinite { get; set; } = true;

    public int Revision { get; private set; }

    public Faction Faction => Faction.Player;

    public float Facing => BodyFacing;

    public float HitRadius => Definition != null
        ? Mathf.Max(Definition.Width, Definition.Height) * Const.Unit * 0.5f
        : Const.Unit * 0.5f;

    /// <summary>Каркас уже смотрит по сторонам — правда, вполглаза.</summary>
    public float VisionRadius => Definition != null ? Definition.VisionRadiusPx * 0.5f : 0f;

    public void Init(int id, UnitDefinition def, Vector2 center, float facing)
    {
        Id = id;
        Definition = def;
        Position = center;
        BodyFacing = facing;
        Health = new Health(def.FrameHealth * Const.BlueprintHealthFactor);
    }

    public override void _Ready() => Health ??= new Health(100f * Const.BlueprintHealthFactor);

    public override void _Process(double delta) => QueueRedraw();

    // ── приказы ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Каркас исполняет один приказ — снос; остальное он держит для достроенной сущности.
    /// Держать, а не отвергать, нужно потому, что назначенное каркасу относится к постройке,
    /// которая из него выйдет: неподвижному каркасу завода приказ «идти» бессмыслен, а точке
    /// сбора для его юнитов — нет, и это один и тот же приказ.
    /// </summary>
    public void RunOrder(Order order, double dt)
    {
        if (order.Kind == OrderKind.Delete)
            Demolish();
    }

    /// <summary>Приказов нет: каркасу и без них есть чем заняться — он строится.</summary>
    public void OnIdle(double dt) { }

    /// <summary>
    /// Снос каркаса по воле игрока — тем же каналом, что и гибель от обстрела: документ
    /// <c>DamageDealt</c> разбирает DamageSystem в фазе реакции, и проекции видят
    /// <c>EntityDestroyed</c>. Прямой QueueFree отсюда оставил бы журнал без следа.
    ///
    /// Вложенный в стройку метал не возвращается: снос есть потеря, а не отмена сделанного.
    /// Отменить без потерь можно план, пока за него не взялись, — см. <see cref="BuildPlan"/>.
    /// </summary>
    private void Demolish()
    {
        if (Health == null || Health.IsDead || IsQueuedForDeletion())
            return;

        GameManager.I.Events.Append(new DamageDealt
        {
            TargetId = Id,
            SourceId = Id,
            Amount = Mathf.Max(Health.Current, 1f),
            Pos = GlobalPosition,
        });
    }

    // ── очередь производства ───────────────────────────────────────────────────

    public int CountOf(string unitId) => Tally(_production, unitId) + Tally(_rush, unitId);

    public int RushCountOf(string unitId) => Tally(_rush, unitId);

    private static int Tally(List<string> list, string unitId)
    {
        int count = 0;

        foreach (string id in list)
            if (id == unitId)
                count++;

        return count;
    }

    /// <summary>
    /// Заказать выпуск. Ключ проверяется по каталогу так же, как у завода: неизвестное
    /// определение до достройки всё равно не превратилось бы в юнита.
    /// </summary>
    public void Enqueue(string unitId, int count = 1, bool rush = false)
    {
        if (!CanProduce || string.IsNullOrEmpty(unitId) || count <= 0)
            return;

        if (GameManager.I?.Catalog.Unit(unitId) == null)
            return;

        var list = rush ? _rush : _production;

        for (int i = 0; i < count; i++)
            list.Add(unitId);

        Revision++;
    }

    /// <summary>
    /// Снять с конца очереди до <paramref name="count"/> последних вхождений этого типа.
    /// Указателя и накопленного прогресса у каркаса нет — снимать проще, чем заводу.
    /// </summary>
    public void Dequeue(string unitId, int count = 1, bool rush = false)
    {
        if (string.IsNullOrEmpty(unitId) || count <= 0)
            return;

        var list = rush ? _rush : _production;

        for (int n = 0; n < count; n++)
        {
            int at = list.LastIndexOf(unitId);

            // Вхождения кончились раньше запрошенного числа — выходим из цикла, а не из
            // метода: снятое до этого мига уже изменило очередь, и ревизию надо поднять
            if (at < 0)
                break;

            list.RemoveAt(at);
        }

        Revision++;
    }

    /// <summary>
    /// Спрос стройки: мощность строителей — это метал в секунду, а энергия — сумма
    /// прожорливости их инструментов. У постройки своей энергоцены нет: одно и то же
    /// здание обойдётся дороже, если его варит коммандер, и дешевле, если фабрикатор.
    /// </summary>
    public override void Declare(EconomyLedger ledger)
    {
        if (Definition == null || TotalPower <= 0f || !NeedsWork)
            return;

        ledger.Request(ResourceKind.Metal, TotalPower);
        ledger.Request(ResourceKind.Energy, TotalEnergy);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        if (Definition == null || TotalPower <= 0f || !NeedsWork)
            return;

        var events = GameManager.I.Events;

        // Энергию стройка забирает по своей доле, а не по темпу работы: мощность оплачена
        // целиком, даже когда стройка еле ползёт из-за нехватки метала. Лишнее сгорает —
        // именно поэтому просадку по энергии лечат генераторами
        float energy = (float)(TotalEnergy * dt) * rates.Energy;
        if (energy > 0f)
            events.Append(new ResourceSpent { Kind = ResourceKind.Energy, Amount = energy });

        // А сама работа и метал идут по худшей из долей: цена постройки не меняется,
        // меняется только скорость
        float done = Mathf.Min((float)(TotalPower * dt) * rates.Work, Definition.TotalWork - Progress);
        if (done <= 0f)
            return;

        events.Append(new ResourceSpent { Kind = ResourceKind.Metal, Amount = done });

        Progress += done;

        if (Progress >= Definition.TotalWork - 0.001f)
            Complete();
    }

    private void Complete()
    {
        var gm = GameManager.I;
        var center = GlobalPosition;

        // Место держал каркас — отпускаем немедленно: готовая постройка займёт его
        // заново в этом же кадре, а юнит не занимает вовсе. Ждать конца кадра нельзя,
        // иначе SpawnBuilding увидел бы место ещё занятым. Отложенное снятие по подписке
        // Spawner идемпотентно: повторный вызов Remove ничего не делает.
        gm.Obstacles.Remove(this);

        // Постройка встаёт на освобождённое место заново, юнит уходит с него и ходит.
        // Род берём у класса, а не у формы: форма есть и у каркаса юнита
        // Угол наследуется каркасом: игрок развернул стройку, и готовая постройка обязана
        // встать так же. Юниту угол не передаётся — у подвижного он означает курс,
        // и первый же шаг его перепишет
        var heir = Definition.IsStructure
            ? (IOrderable)gm.Spawn.SpawnBuilding(Definition, center, BodyFacing)
            : gm.Spawn.SpawnUnit(Definition, center);

        gm.Events.Append(new ConstructionCompleted
        {
            EntityId = heir.EntityId,
            DefinitionId = Definition.Id,
            Pos = center,
        });

        Bequeath(heir);

        Retire();
    }

    /// <summary>
    /// Передать достроенной сущности всё, что было назначено каркасу: ветку приказов
    /// и набранную очередь производства.
    ///
    /// ВЕТКА ПЕРЕДАЁТСЯ ТА ЖЕ САМАЯ, А НЕ КОПИЯ. Приказ, отданный отряду вместе с каркасом,
    /// один на всех получателей (см. <see cref="Order"/>), и достроенная постройка обязана
    /// оказаться в том же приказе, а не в его двойнике. Подписка на ветку переезжает
    /// с курсора каркаса на курсор преемника: сперва подписывается преемник, и только потом
    /// каркас свою подписку снимает, — иначе ветка на миг осталась бы без исполнителей вовсе.
    ///
    /// Отбирать приказы по допустимости не нужно: набор каркаса и есть набор достроенной
    /// сущности, за вычетом сноса, который до этого мига уже был бы исполнен.
    /// </summary>
    private void Bequeath(IOrderable heir)
    {
        // Подписку каркаса снимает Retire — уже после того, как преемник подписался сам
        if (Orders.List is { } branch)
            heir.Orders.Adopt(branch);

        if (heir is not IProducer producer || !producer.CanProduce)
            return;

        producer.Infinite = Infinite;

        foreach (string unitId in _production)
            producer.Enqueue(unitId);

        // Срочные заказы переезжают срочными: иначе они растворились бы в основной
        // очереди и в режиме повтора пошли бы по кругу вопреки замыслу игрока
        foreach (string unitId in _rush)
            producer.Enqueue(unitId, rush: true);

        _production.Clear();
        _rush.Clear();
    }

    /// <summary>Каркас разбит: вывести из игры. Место и EntityStore освобождает Spawner.</summary>
    public void OnDestroyed() => Retire();

    /// <summary>Выводим узел из игры до удаления, чтобы по нему не прошёл ещё один кадр.</summary>
    private void Retire()
    {
        ReleaseWorkers();

        // Отпускаем ветку приказов: при достройке преемник к этому мигу уже подписан
        // на неё сам, а при сносе работать по ней от лица каркаса больше некому
        Orders.Clear();

        _production.Clear();
        _rush.Clear();
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Definition == null)
            return;

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        // У каркаса обзор урезан: полная зона появится у готовой постройки
        var tools = GizmoTools.From(Definition) with { VisionRadius = VisionRadius };
        UnitGizmos.Draw(this, tools, Faction, selected: GizmoGate.IsSelected(this));

        // Каркас повёрнут так же, как встанет постройка: место он занимает уже сейчас,
        // и показывать его иначе, чем оно занято, нельзя
        DrawSetTransform(Vector2.Zero, BodyFacing, Vector2.One);

        // Площадка принадлежит каркасу так же, как готовой постройке: исчезает вместе с ним
        BuildingSkirt.Draw(this, rect);

        ShapeDraw.Rect(this, rect, ShapeStyle.Solid(new Color(Definition.Color, 0.15f)));

        // Заполнение снизу вверх по прогрессу
        float filled = size.Y * Ratio;
        ShapeDraw.Rect(this, new Rect2(rect.Position.X, rect.End.Y - filled, size.X, filled),
            ShapeStyle.Solid(new Color(Definition.Color, 0.55f)));

        ShapeDraw.Rect(this, rect,
            ShapeStyle.Outline(new Color(1f, 1f, 1f, 0.7f), 2f, WidthMode.Screen));

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        var font = ThemeDB.FallbackFont;
        string label = $"{Definition.DisplayName} {Mathf.FloorToInt(Ratio * 100f)}%";
        DrawString(font, new Vector2(rect.Position.X, rect.Position.Y - 6f), label,
            HorizontalAlignment.Left, -1, 13, Colors.White);

        if (WorkerCount > 0)
            DrawString(font, new Vector2(rect.Position.X, rect.End.Y + 16f),
                $"строителей: {WorkerCount} ({TotalPower:0.#}/с)",
                HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.9f, 1f));

        HealthBar.Draw(this, Health, size.X * 0.9f, rect.Position.Y - 20f);
    }
}
