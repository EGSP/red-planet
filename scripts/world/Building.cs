using Godot;

/// <summary>
/// Готовая постройка: занимает прямоугольник по форме своего справочника, повёрнутый
/// на угол, под которым её поставили.
///
/// УГОЛ ПРИНАДЛЕЖИТ ЭКЗЕМПЛЯРУ, А НЕ СПРАВОЧНИКУ. Раньше он брался из определения и был
/// у всех построек одного рода одинаков, потому что повернуть постройку было нечем.
/// Теперь его задаёт игрок при постановке, а справочник даёт лишь начальное значение
/// для тех, кто появляется в мире помимо стройки, — стартовой базы например.
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
    /// Занимаемое место. Считается от позиции, формы и угла корпуса, а НЕ от поворота ноды:
    /// у турели в Rotation живёт ось башни, и корпус от её вращения шевелиться не должен.
    /// </summary>
    public Obb Footprint => Definition == null
        ? new Obb(GlobalPosition, Vector2.Zero)
        : Placement.Footprint(Definition, GlobalPosition, BodyFacing);

    public Health Health { get; private set; }

    public OrderQueue Orders { get; }

    public Building() => Orders = new OrderQueue(this);

    public int EntityId => Id;

    public string DefinitionId => Definition?.Id ?? "";

    public string DisplayName => Definition?.DisplayName ?? "постройка";

    /// <summary>
    /// Обычной постройке приказать нечего: склад, стена и генератор стоят и работают сами.
    /// Пустой набор и есть способ это сказать — очередь у них общая со всеми, но пополнить
    /// её невозможно. Турель и башня-сборщик свой набор переопределяют.
    /// </summary>
    public virtual OrderSet AllowedOrders => OrderSet.None;

    public SelectionGroup SelectionGroup => SelectionGroup.Structures;

    public virtual void RunOrder(Order order, double dt) { }

    public virtual void OnIdle(double dt) { }

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

        QueueRedraw();
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

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        VisionGizmo.Draw(this, VisionRadius, Definition.Color);

        // Корпус повёрнут на угол постановки. Правка трансформа канвы, а не поворот ноды:
        // ноду держит за собой турель, у которой в Rotation ось башни
        DrawSetTransform(Vector2.Zero, BodyFacing, Vector2.One);

        DrawRect(rect, Definition.Color);
        DrawRect(rect, new Color(0f, 0f, 0f, 0.35f), false, 2f);

        // Ось «вперёд» — короткая насечка от центра к краю. Рисуется в координатах корпуса,
        // поэтому насечка вперёд и есть направление корпуса
        float span = Mathf.Min(size.X, size.Y);
        DrawLine(Vector2.Right * span * 0.2f, Vector2.Right * span * 0.45f,
            new Color(1f, 1f, 1f, 0.5f), 3f);

        // Подпись и полоса прочности читаются с экрана, а не с корпуса, поэтому поворот
        // на них не распространяется
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X + 4f, rect.Position.Y + 16f),
            Definition.DisplayName, HorizontalAlignment.Left, -1, 12, Colors.Black);

        HealthBar.Draw(this, Health, size.X * 0.9f, rect.Position.Y - 8f);
    }
}
