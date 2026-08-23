using Godot;

/// <summary>
/// Готовая постройка: занимает прямоугольник по форме своего справочника, повёрнутый
/// на угол, под которым её поставили.
///
/// УГОЛ ПРИНАДЛЕЖИТ ЭКЗЕМПЛЯРУ, А НЕ СПРАВОЧНИКУ. Раньше он брался из определения и был
/// у всех построек одного рода одинаков, потому что повернуть постройку было нечем.
/// Теперь его задаёт игрок при постановке, а справочник даёт лишь начальное значение
/// для тех построек, которые появляются в мире помимо стройки.
///
/// Ноду постройка по-прежнему не крутит: в Rotation у турели живёт ось башни, и корпус
/// от её вращения шевелиться не должен. Поэтому угол корпуса — отдельное число,
/// а поворот при отрисовке применяется правкой трансформа канвы.
/// </summary>
public partial class Building : Node2D, IFacing, IDamageable, IEconomyActor, IVision, IRepairable,
    IOrderable, IObstacle
{
    public int Id { get; private set; }
    public UnitDefinition Definition { get; private set; }

    /// <summary>Угол корпуса, под которым постройку поставили. Ось занимаемого места.</summary>
    public float BodyFacing { get; private set; }

    /// <summary>
    /// Инструменты постройки с их углами и перезарядками. Есть у любой постройки, а не
    /// только у турели: манипулятор сборщика сидит на такой же поворотной опоре, что
    /// и орудие, и описывается теми же числами.
    /// </summary>
    public AimRig Aim { get; } = new();

    /// <summary>
    /// Занимаемое место. Считается от позиции, формы и угла корпуса, а НЕ от поворота ноды:
    /// у турели в Rotation живёт ось башни, и корпус от её вращения шевелиться не должен.
    /// </summary>
    public Obb Footprint => Definition == null
        ? new Obb(GlobalPosition, Vector2.Zero)
        : Placement.Footprint(Definition, GlobalPosition, BodyFacing);

    public Health Health { get; private set; }

    /// <summary>
    /// Экземпляр сцены изображения, если вид ею снабжён. Постройка ноду не крутит, поэтому
    /// угол корпуса модели выставляется вручную — см. <see cref="SyncModel"/>.
    /// </summary>
    protected UnitModel Model { get; private set; }

    /// <summary>
    /// Слой пометок поверх модели. Есть только у постройки с моделью: без неё корпус
    /// рисуется в общем ряду команд, и пометкам достаточно идти после него.
    /// </summary>
    private ModelLayer _marks;

    public OrderQueue Orders { get; }

    public Building() => Orders = new OrderQueue(this);

    public int EntityId => Id;

    public string DefinitionId => Definition?.Id ?? "";

    public string DisplayName => Definition?.DisplayName ?? "постройка";

    /// <summary>
    /// Приказы из определения: пересечение <c>[orders]</c> и исполнимых по классу.
    /// Обычной постройке в файле обычно разрешён только снос; турель и завод расширяют список.
    /// </summary>
    public virtual OrderSet AllowedOrders => Definition?.AcceptedOrders ?? OrderSet.None;

    /// <summary>Исполнимо, но не объявлено — панель помечает звёздочкой.</summary>
    public virtual OrderSet SoftOrders => Definition?.SoftOrders ?? OrderSet.None;

    public SelectionGroup SelectionGroup => SelectionGroup.Structures;

    public virtual void RunOrder(Order order, double dt)
    {
        if (order.Kind == OrderKind.Delete)
            Demolish();
    }

    public virtual void OnIdle(double dt) { }

    /// <summary>
    /// Снос через тот же канал, что и гибель от урона: документ DamageDealt разбирает
    /// DamageSystem в React, и проекции видят EntityDestroyed. Прямой QueueFree из
    /// Simulate оставил бы журнал без следа гибели.
    /// </summary>
    protected void Demolish()
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

    public Faction Faction => Faction.Player;

    /// <summary>
    /// Ось «вперёд». У обычной постройки она совпадает с углом корпуса и не меняется;
    /// турель переопределяет её на поворот собственной ноды — башня крутится.
    /// </summary>
    public virtual float Facing => BodyFacing;

    /// <summary>Радиус попадания — по описанной окружности: по краю стены снаряд тоже попадает.</summary>
    public float HitRadius => Definition != null
        ? Mathf.Max(Definition.Width, Definition.Height) * Const.Unit * 0.5f
        : Const.Unit * 0.5f;

    public float VisionRadius => Definition?.VisionRadiusPx ?? 0f;

    /// <summary>Курс ремонта берётся прямо из справочника: прочность, делённая на цену.</summary>
    public float HealthPerMetal => Definition?.HealthPerMetal ?? 0f;

    public virtual void Init(int id, UnitDefinition def, Vector2 center, float facing)
    {
        Id = id;
        Definition = def;
        Position = center;
        BodyFacing = facing;
        Health = new Health(def.MaxHealth);

        AttachModel();

        // Стенд собирается после модели: части изображения связываются с инструментами
        // по идентификатору, и без поднятой сцены связывать было бы не с чем
        Aim.Bind(def, Model);
        Aim.Snap();
        Aim.Apply();

        QueueRedraw();
        SetProcess(true);
    }

    /// <summary>
    /// Поднять сцену изображения, если вид ею снабжён. Постройки принадлежат стороне игрока,
    /// поэтому окраска назначается сразу и в дальнейшем не пересматривается.
    /// </summary>
    private void AttachModel()
    {
        Model = ModelLibrary.Instantiate(Definition?.Model);

        if (Model == null)
            return;

        AddChild(Model);
        Model.ApplyTeamColor(TeamPalette.Of(Faction));

        // Слой пометок добавляется ПОСЛЕ модели: полоса прочности, прогресс завода и
        // стрелки выездов обязаны лежать поверх корпуса, а собственные команды узла
        // выполняются до потомков — см. ModelLayer
        _marks = ModelLayer.Attach(this, PaintMarks, "Marks");

        SyncModel();
    }

    /// <summary>
    /// Пометки поверх корпуса: полоса прочности и всё, что добавляют наследники.
    /// Без модели рисуются самой постройкой в конце <see cref="_Draw"/>, с моделью —
    /// слоем <see cref="_marks"/>, который идёт следом за ней.
    ///
    /// Базис слоя не повёрнут на угол корпуса: пометки читаются с экрана, и в
    /// <see cref="_Draw"/> перед ними поворот тоже снимается.
    /// </summary>
    protected virtual void PaintMarks(CanvasItem canvas)
    {
        if (Definition == null)
            return;

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        HealthBar.Draw(canvas, Health, size.X * 0.9f, -size.Y * 0.5f - 8f);
    }

    /// <summary>
    /// Согласовать углы модели с постройкой. Корпус модели ставится на угол постановки
    /// за вычетом поворота ноды: у турели в <see cref="Node2D.Rotation"/> живёт ось башни,
    /// и корпус от её вращения шевелиться не должен. Инструменты, наоборот, получают
    /// разницу между осью наведения и углом корпуса.
    /// </summary>
    protected void SyncModel()
    {
        if (Model == null)
            return;

        Model.Rotation = BodyFacing - Rotation;

        // Каждая часть получает свой угол: у постройки с двумя стволами они наводятся
        // врозь, и один угол на все части такого не выразил бы
        Aim.Apply();
    }

    /// <summary>
    /// Что сделать после того, как стенд свёл требования инструментов, но до согласования
    /// углов модели. Обычной постройке делать нечего — основание неподвижно; турель
    /// переносит сюда ось башни в поворот ноды.
    /// </summary>
    protected virtual void AfterAim()
    {
    }

    /// <summary>
    /// Куда постройка тянется рабочей рукой в этом шаге. Пусто — работы нет, и рука
    /// возвращается к оси корпуса.
    ///
    /// ОБЪЯВЛЕНО ЗДЕСЬ, А НЕ У СБОРЩИКА, потому что наведение закрывает шаг вместе
    /// со всем остальным стендом, а закрывает его эта постройка. Ствол наводит система
    /// стрельбы, одна на подвижных и неподвижных, и потому турель крутит башню без единой
    /// строки собственного кода; у рабочей руки такой системы нет, и место, где её наводят,
    /// приходится назначать явно.
    /// </summary>
    protected virtual Vector2? WorkAim => null;

    /// <summary>
    /// Области инструментов включаются кадрами (Ctrl, покрытие турелей, вкладка giz),
    /// поэтому постройка обязана перерисовываться, иначе круг появится только при
    /// следующей смене состояния.
    /// </summary>
    public override void _Process(double delta)
    {
        Aim.AimWork(BodyFacing, GlobalPosition, WorkAim, delta);

        // Желаемый угол корпуса постройке безразличен: основание вкопано и развернуться
        // не может. Стенд всё равно закрывают каждый кадр — иначе инструменты не вернутся
        // в походное положение, а требования копились бы от кадра к кадру
        Aim.Advance(BodyFacing, delta);

        AfterAim();
        SyncModel();
        Model?.ApplyDamage(Health?.Ratio ?? 1f);
        QueueRedraw();
        _marks?.QueueRedraw();
    }

    /// <summary>
    /// Генератор заявляет выработку. Постройка с <see cref="ConversionDefinition"/> —
    /// расход энергии (портал) или, у синтезатора, ещё и выход метала в наследнике.
    /// Производство не зависит от производительности базы: электростанция даёт ресурс
    /// даже тогда, когда всё остальное еле шевелится.
    /// </summary>
    public virtual void Declare(EconomyLedger ledger)
    {
        if (Definition == null)
            return;

        ledger.AddIncome(ResourceKind.Energy, Definition.EnergyProduction);
        ledger.AddIncome(ResourceKind.Metal, Definition.MetalProduction);

        float drain = Definition.Conversion?.EnergyDrain ?? 0f;
        if (drain > 0f)
            ledger.Request(ResourceKind.Energy, drain);
    }

    public virtual void Run(double dt, EconomyRates rates)
    {
        if (Definition == null)
            return;

        var events = GameManager.I.Events;

        if (Definition.EnergyProduction > 0f)
        {
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Energy,
                Amount = Definition.EnergyProduction * (float)dt,
            });
        }

        if (Definition.MetalProduction > 0f)
        {
            events.Append(new ResourceGained
            {
                Kind = ResourceKind.Metal,
                Amount = Definition.MetalProduction * (float)dt,
            });
        }

        float drain = Definition.Conversion?.EnergyDrain ?? 0f;
        if (drain > 0f)
        {
            float spent = drain * (float)dt * rates.Energy;
            if (spent > 0f)
                events.Append(new ResourceSpent { Kind = ResourceKind.Energy, Amount = spent });
        }
    }

    /// <summary>Постройку снесли: вывести из игры. Место и EntityStore освобождает Spawner.</summary>
    public virtual void OnDestroyed()
    {
        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем.
        // Из разрезов индекса нода выпадает сама, как только помечена на удаление;
        // карту препятствий и реестр по id чистит подписка Spawner на выбытие из индекса.
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Definition == null)
            return;

        UnitGizmos.Draw(this, GizmoTools.From(Definition), Faction,
            selected: GizmoGate.IsSelected(this),
            armedStructure: Definition.Weapon != null);

        // Площадка и запасной корпус рисуются одной последовательностью на всю игру
        // и редактор содержимого — см. BuildingVisual. Угол постановки применяется правкой
        // трансформа канвы, а не поворотом ноды: ноду держит за собой турель, у которой
        // в Rotation ось башни. Трансформ там же и снимается
        BuildingVisual.Draw(this, Definition, Vector2.Zero, BodyFacing,
            showFootprint: false, modelAttached: Model != null);

        // С моделью пометки рисует слой, идущий после неё; его перерисовку ведёт
        // _Process, поскольку наследники переопределяют _Draw целиком
        if (_marks == null)
            PaintMarks(this);
    }
}
