using Godot;

/// <summary>
/// Месторождение руды. Механика та же, что у каркаса: копатели подключаются инструментом,
/// а источник сам отдаёт руду в общее хранилище по суммарной мощности копки.
/// Выработался — исчезает и освобождает слот.
/// </summary>
public partial class OreDeposit : WorkNode
{
    public Vector2I Cell { get; private set; }
    public float Amount { get; private set; } = Const.OreDepositAmount;

    public override bool NeedsWork => Amount > 0f;

    public void Init(int id, Vector2I cell)
    {
        Id = id;
        Cell = cell;
        Position = Const.CellCenter(cell);
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>
    /// Добыча — и доход, и расход разом: буры дают метал, но сами едят энергию.
    ///
    /// Доход заявляется по номиналу, хотя реально он тоже просядет вместе с производительностью.
    /// Так делает и PA: считать «сколько дохода будет при той производительности, которую
    /// мы как раз и вычисляем» — это уравнение с самим собой. Погрешность в пользу игрока
    /// и живёт один кадр.
    /// </summary>
    public override void Declare(EconomyLedger ledger)
    {
        if (TotalPower <= 0f || Amount <= 0f)
            return;

        ledger.AddIncome(ResourceKind.Metal, TotalPower);
        ledger.Request(ResourceKind.Energy, TotalEnergy);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        if (TotalPower <= 0f || Amount <= 0f)
            return;

        // Добыче нужна только энергия, метал она как раз производит — значит нехватка
        // метала буры не тормозит, а нехватка энергии тормозит
        float extracted = Mathf.Min(Amount, (float)(TotalPower * dt) * rates.Energy);
        if (extracted <= 0f)
            return;

        Amount -= extracted;

        var events = GameManager.I.Events;

        events.Append(new ResourceGained { Kind = ResourceKind.Metal, Amount = extracted });
        events.Append(new ResourceSpent
        {
            Kind = ResourceKind.Energy,
            Amount = (float)(TotalEnergy * dt) * rates.Energy,
        });

        if (Amount <= 0.001f)
            Deplete();
    }

    private void Deplete()
    {
        var gm = GameManager.I;
        gm.Grid.Free(Cell);

        gm.Events.Append(new OreDepleted { EntityId = Id, Cell = Cell });

        gm.Entities.Remove(Id);

        // Выводим узел из игры до удаления, чтобы по нему не прошёл ещё один кадр
        ReleaseWorkers();
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        float half = Const.Unit * 0.5f;
        float ratio = Mathf.Clamp(Amount / Const.OreDepositAmount, 0f, 1f);

        // Руда в земле — источник метала: отдельного ресурса «руда» в экономике нет
        var ore = new Color(0.85f, 0.45f, 0.2f);
        DrawRect(new Rect2(-half, -half, Const.Unit, Const.Unit), new Color(ore, 0.25f));
        DrawCircle(Vector2.Zero, half * (0.35f + 0.55f * ratio), ore);

        DrawString(ThemeDB.FallbackFont, new Vector2(-half, -half - 6f),
            $"{Mathf.CeilToInt(Amount)}", HorizontalAlignment.Left, -1, 12, ore);

        if (WorkerCount > 0)
            DrawString(ThemeDB.FallbackFont, new Vector2(-half, half + 14f),
                $"копают: {WorkerCount}", HorizontalAlignment.Left, -1, 11,
                new Color(1f, 0.85f, 0.6f));
    }
}
