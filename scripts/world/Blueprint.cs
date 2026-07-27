using Godot;

/// <summary>
/// Каркас будущей постройки. Появляется в мире сразу при постановке.
/// Сам тянет ресурсы из общего хранилища пропорционально суммарной мощности строителей.
/// Не хватает ресурсов — стройка замедляется, а не встаёт.
///
/// Каркас — тоже цель для врага, и прочности у него доля от готовой постройки
/// (Const.BlueprintHealthFactor): стройка под обстрелом должна быть рискованной затеей.
/// </summary>
public partial class Blueprint : WorkNode, IFacing, IDamageable
{
    public BuildableDef Def { get; private set; }
    public Vector2I Cell { get; private set; }
    public float Progress { get; private set; }

    public Health Health { get; private set; }

    public override bool NeedsWork => Def != null && Progress < Def.TotalWork;

    public float Ratio => Def == null || Def.TotalWork <= 0f ? 0f : Progress / Def.TotalWork;

    public int EntityId => Id;

    public Faction Faction => Faction.Player;

    public float Facing => Def != null ? Mathf.DegToRad(Def.FacingDegrees) : 0f;

    public float HitRadius => Def != null
        ? Mathf.Max(Def.Width, Def.Height) * Const.Unit * 0.5f
        : Const.Unit * 0.5f;

    public void Init(int id, BuildableDef def, Vector2I cell)
    {
        Id = id;
        Def = def;
        Cell = cell;
        Position = Const.AreaCenter(cell, def.Size);
        Health = new Health(def.MaxHealth * Const.BlueprintHealthFactor);
    }

    public override void _Ready()
    {
        Health ??= new Health(100f * Const.BlueprintHealthFactor);

        AddToGroup("work");
        AddToGroup("blueprint");
        AddToGroup(Targeting.Group);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void Work(double dt)
    {
        if (Def == null || TotalPower <= 0f)
            return;

        float want = Mathf.Min((float)(TotalPower * dt), Def.TotalWork - Progress);
        if (want <= 0f)
            return;

        // Доля от полного объёма работ, которую пытаемся закрыть в этот тик
        float fraction = want / Def.TotalWork;
        float needOre = Def.CostOre * fraction;
        float needMetal = Def.CostMetal * fraction;

        var stockpile = GameManager.I.Stockpile;
        float scale = stockpile.AvailableFraction(needOre, needMetal);
        if (scale <= 0f)
            return;

        var events = GameManager.I.Events;

        if (needOre > 0f)
            events.Append(new ResourceSpent { Kind = ResourceKind.Ore, Amount = needOre * scale });

        if (needMetal > 0f)
            events.Append(new ResourceSpent { Kind = ResourceKind.Metal, Amount = needMetal * scale });

        Progress += want * scale;

        if (Progress >= Def.TotalWork - 0.001f)
            Complete();
    }

    private void Complete()
    {
        var gm = GameManager.I;

        // Клетки держал каркас — отпускаем: готовая постройка займёт их заново,
        // а юнит не занимает вовсе, он ходит.
        gm.Grid.Free(Cell, Def);

        int spawnedId = Def.IsUnit
            ? gm.Spawn.SpawnUnit(Def.Scene, Const.AreaCenter(Cell, Def.Size)).Id
            : gm.Spawn.SpawnBuilding(Def, Cell).Id;

        gm.Events.Append(new ConstructionCompleted
        {
            EntityId = spawnedId,
            DefId = Def.Id,
            Cell = Cell,
        });

        gm.Entities.Remove(Id);
        Retire();
    }

    /// <summary>Каркас разбит: клетки освобождаются, вложенные ресурсы пропадают.</summary>
    public void OnDestroyed()
    {
        var gm = GameManager.I;

        if (Def != null)
            gm.Grid.Free(Cell, Def);

        gm.Entities.Remove(Id);
        Retire();
    }

    /// <summary>Выводим узел из игры до удаления, чтобы по нему не прошёл ещё один кадр.</summary>
    private void Retire()
    {
        ReleaseWorkers();
        RemoveFromGroup("work");
        RemoveFromGroup("blueprint");
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

        DrawRect(rect, new Color(Def.Color, 0.15f));

        // Заполнение снизу вверх по прогрессу
        float filled = size.Y * Ratio;
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - filled, size.X, filled),
            new Color(Def.Color, 0.55f));

        DrawRect(rect, new Color(1f, 1f, 1f, 0.7f), false, 2f);

        var font = ThemeDB.FallbackFont;
        string label = $"{Def.DisplayName} {Mathf.FloorToInt(Ratio * 100f)}%";
        DrawString(font, new Vector2(rect.Position.X, rect.Position.Y - 6f), label,
            HorizontalAlignment.Left, -1, 13, Colors.White);

        if (WorkerCount > 0)
            DrawString(font, new Vector2(rect.Position.X, rect.End.Y + 16f),
                $"строителей: {WorkerCount} ({TotalPower:0.#}/с)",
                HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.9f, 1f));

        HealthBar.Draw(this, Health, size.X * 0.9f, rect.Position.Y - 20f);
    }
}
