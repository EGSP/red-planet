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

    public override void _Ready()
    {
        AddToGroup("work");
        AddToGroup("ore");
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void Work(double dt)
    {
        if (TotalPower <= 0f || Amount <= 0f)
            return;

        float extracted = Mathf.Min(Amount, (float)(TotalPower * dt));
        Amount -= extracted;

        GameManager.I.Events.Append(new ResourceGained
        {
            Kind = ResourceKind.Ore,
            Amount = extracted,
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
        RemoveFromGroup("work");
        RemoveFromGroup("ore");
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    public override void _Draw()
    {
        float half = Const.Unit * 0.5f;
        float ratio = Mathf.Clamp(Amount / Const.OreDepositAmount, 0f, 1f);

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
