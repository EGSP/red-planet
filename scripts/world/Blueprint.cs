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

    /// <summary>Каркас ёмкости хранилища ещё не даёт, поэтому в документ о гибели ключ не идёт.</summary>
    public string DefId => "";

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

        AddToGroup("blueprint");
        AddToGroup(Targeting.Group);
        AddToGroup(EconomySystem.Group);
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>
    /// Спрос стройки: мощность строителей — это метал в секунду, а энергия — сумма
    /// прожорливости их инструментов. У постройки своей энергоцены нет: одно и то же
    /// здание обойдётся дороже, если его варит коммандер, и дешевле, если фабрикатор.
    /// </summary>
    public override void Declare(EconomyLedger ledger)
    {
        if (Def == null || TotalPower <= 0f || !NeedsWork)
            return;

        ledger.Request(ResourceKind.Metal, TotalPower);
        ledger.Request(ResourceKind.Energy, TotalEnergy);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        if (Def == null || TotalPower <= 0f || !NeedsWork)
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
        float done = Mathf.Min((float)(TotalPower * dt) * rates.Work, Def.TotalWork - Progress);
        if (done <= 0f)
            return;

        events.Append(new ResourceSpent { Kind = ResourceKind.Metal, Amount = done });

        Progress += done;

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
        RemoveFromGroup("blueprint");
        RemoveFromGroup(Targeting.Group);
        RemoveFromGroup(EconomySystem.Group);
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
