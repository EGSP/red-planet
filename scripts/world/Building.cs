using Godot;

/// <summary>
/// Готовая постройка: занимает клетки по матрице своего справочника.
///
/// Ось «вперёд» у здания есть, но ноду оно не крутит: визуал привязан к клеткам сетки,
/// а поворот ноды сетку не поворачивает. Поэтому направление живёт отдельным числом
/// из справочника и рисуется маркером — задел под турели и ворота.
/// </summary>
public partial class Building : Node2D, IFacing, IDamageable
{
    public int Id { get; private set; }
    public BuildableDef Def { get; private set; }
    public Vector2I Cell { get; private set; }

    public Health Health { get; private set; }

    public int EntityId => Id;

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

    public virtual void Init(int id, BuildableDef def, Vector2I cell)
    {
        Id = id;
        Def = def;
        Cell = cell;
        Position = Const.AreaCenter(cell, def.Size);
        Health = new Health(def.MaxHealth);
        AddToGroup("building");
        AddToGroup(Targeting.Group);
        QueueRedraw();
    }

    /// <summary>Постройку снесли: освободить клетки и выйти из реестра.</summary>
    public virtual void OnDestroyed()
    {
        var gm = GameManager.I;

        if (Def != null)
            gm.Grid.Free(Cell, Def);

        gm.Entities.Remove(Id);

        // Выводим из игры до удаления, чтобы по ноде не прошёл ещё один кадр систем
        RemoveFromGroup("building");
        RemoveFromGroup(Targeting.Group);
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
