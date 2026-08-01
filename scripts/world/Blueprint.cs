using Godot;

/// <summary>
/// Каркас будущей постройки. Появляется в мире сразу при постановке.
/// Сам тянет ресурсы из общего хранилища пропорционально суммарной мощности строителей.
/// Не хватает ресурсов — стройка замедляется, а не встаёт.
///
/// Каркас — тоже цель для врага, и прочности у него доля от готовой постройки
/// (Const.BlueprintHealthFactor): стройка под обстрелом должна быть рискованной затеей.
/// </summary>
public partial class Blueprint : WorkNode, IFacing, IDamageable, IVision, IObstacle
{
    public UnitDefinition Definition { get; private set; }
    public float Progress { get; private set; }

    /// <summary>Место занято уже каркасом: перекрыть начатую стройку нельзя.</summary>
    public Rect2 Footprint => Definition == null
        ? new Rect2(GlobalPosition, Vector2.Zero)
        : Placement.Footprint(Definition, GlobalPosition);

    public Health Health { get; private set; }

    public override bool NeedsWork => Definition != null && Progress < Definition.TotalWork;

    public float Ratio => Definition == null || Definition.TotalWork <= 0f ? 0f : Progress / Definition.TotalWork;

    public int EntityId => Id;

    /// <summary>Каркас ёмкости хранилища ещё не даёт, поэтому в документ о гибели ключ не идёт.</summary>
    public string DefinitionId => "";

    public Faction Faction => Faction.Player;

    public float Facing => Definition != null ? Mathf.DegToRad(Definition.FacingDegrees) : 0f;

    public float HitRadius => Definition != null
        ? Mathf.Max(Definition.Width, Definition.Height) * Const.Unit * 0.5f
        : Const.Unit * 0.5f;

    /// <summary>Каркас уже смотрит по сторонам — правда, вполглаза.</summary>
    public float VisionRadius => Definition != null ? Definition.VisionRadiusPx * 0.5f : 0f;

    public void Init(int id, UnitDefinition def, Vector2 center)
    {
        Id = id;
        Definition = def;
        Position = center;
        Health = new Health(def.FrameHealth * Const.BlueprintHealthFactor);
    }

    public override void _Ready() => Health ??= new Health(100f * Const.BlueprintHealthFactor);

    public override void _Process(double delta) => QueueRedraw();

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
        int spawnedId = Definition.IsStructure
            ? gm.Spawn.SpawnBuilding(Definition, center).Id
            : gm.Spawn.SpawnUnit(Definition, center).Id;

        gm.Events.Append(new ConstructionCompleted
        {
            EntityId = spawnedId,
            DefinitionId = Definition.Id,
            Pos = center,
        });

        Retire();
    }

    /// <summary>Каркас разбит: вывести из игры. Место и EntityStore освобождает Spawner.</summary>
    public void OnDestroyed() => Retire();

    /// <summary>Выводим узел из игры до удаления, чтобы по нему не прошёл ещё один кадр.</summary>
    private void Retire()
    {
        ReleaseWorkers();
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

        DrawRect(rect, new Color(Definition.Color, 0.15f));

        // Заполнение снизу вверх по прогрессу
        float filled = size.Y * Ratio;
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - filled, size.X, filled),
            new Color(Definition.Color, 0.55f));

        DrawRect(rect, new Color(1f, 1f, 1f, 0.7f), false, 2f);

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
