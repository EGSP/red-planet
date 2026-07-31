using Godot;

/// <summary>
/// Готовая постройка: занимает клетки по матрице своего справочника.
///
/// Ось «вперёд» у здания есть, но ноду оно не крутит: визуал привязан к клеткам сетки,
/// а поворот ноды сетку не поворачивает. Поэтому направление живёт отдельным числом
/// из справочника и рисуется маркером — задел под турели и ворота.
/// </summary>
public partial class Building : Node2D, IFacing, IDamageable, IEconomyActor, IVision, IRepairable,
    IOrderable
{
    public int Id { get; private set; }
    public BuildableDef Def { get; private set; }
    public Vector2I Cell { get; private set; }

    public Health Health { get; private set; }

    public OrderQueue Orders { get; }

    public Building() => Orders = new OrderQueue(this);

    public int EntityId => Id;

    public string DefId => Def?.Id ?? "";

    public string DisplayName => Def?.DisplayName ?? "постройка";

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
    /// Ось «вперёд». У обычной постройки она из справочника и не меняется;
    /// турель переопределяет её на поворот собственной ноды — башня крутится.
    /// </summary>
    public virtual float Facing => Def != null ? Mathf.DegToRad(Def.FacingDegrees) : 0f;

    /// <summary>Радиус попадания — по описанной окружности: по краю стены снаряд тоже попадает.</summary>
    public float HitRadius => Def != null
        ? Mathf.Max(Def.Width, Def.Height) * Const.Unit * 0.5f
        : Const.Unit * 0.5f;

    public float VisionRadius => Def?.VisionRadiusPx ?? 0f;

    /// <summary>Курс ремонта берётся прямо из справочника: прочность, делённая на цену.</summary>
    public float HealthPerMetal => Def?.HealthPerMetal ?? 0f;

    public virtual void Init(int id, BuildableDef def, Vector2I cell)
    {
        Id = id;
        Def = def;
        Cell = cell;
        Position = Const.AreaCenter(cell, def.Size);
        Health = new Health(def.MaxHealth);

        QueueRedraw();
    }

    /// <summary>
    /// Генератор заявляет свою мощность. Производство не зависит от производительности базы:
    /// электростанция даёт энергию даже тогда, когда всё остальное еле шевелится.
    /// </summary>
    public virtual void Declare(EconomyLedger ledger)
    {
        if (Def != null)
            ledger.AddIncome(ResourceKind.Energy, Def.EnergyProduction);
    }

    public virtual void Run(double dt, EconomyRates rates)
    {
        if (Def == null || Def.EnergyProduction <= 0f)
            return;

        GameManager.I.Events.Append(new ResourceGained
        {
            Kind = ResourceKind.Energy,
            Amount = Def.EnergyProduction * (float)dt,
        });
    }

    /// <summary>Постройку снесли: освободить клетки и выйти из реестра.</summary>
    public virtual void OnDestroyed()
    {
        var gm = GameManager.I;

        if (Def != null)
            gm.Grid.Free(Cell, Def);

        gm.Entities.Remove(Id);

        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем.
        // Из разрезов индекса нода выпадает сама, как только помечена на удаление
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        if (Def == null)
            return;

        var size = new Vector2(Def.Size.X, Def.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        VisionGizmo.Draw(this, VisionRadius, Def.Color);

        DrawRect(rect, Def.Color);
        DrawRect(rect, new Color(0f, 0f, 0f, 0.35f), false, 2f);

        // Ось «вперёд» — короткая насечка от центра к краю
        var forward = Heading.Forward(Facing);
        float span = Mathf.Min(size.X, size.Y);
        DrawLine(forward * span * 0.2f, forward * span * 0.45f, new Color(1f, 1f, 1f, 0.5f), 3f);

        DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X + 4f, rect.Position.Y + 16f),
            Def.DisplayName, HorizontalAlignment.Left, -1, 12, Colors.Black);

        HealthBar.Draw(this, Health, size.X * 0.9f, rect.Position.Y - 8f);
    }
}
