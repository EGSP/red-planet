using Godot;

/// <summary>
/// Синтезатор метала: жжёт энергию и даёт метал. Раньше это был завод, перегонявший
/// руду в метал, но ресурс остался один, и конвертировать стало нечего.
///
/// Смысл постройки в потоковой экономике — сброс излишков: когда генераторов больше,
/// чем потребителей, лишняя энергия иначе просто сгорает на потолке хранилища.
/// Курс намеренно невыгодный, синтезатор не должен заменять месторождения.
///
/// Расход энергии заявляет и списывает <see cref="Building"/> по Conversion;
/// здесь только выход метала и индикатор работы.
/// </summary>
public partial class Factory : Building
{
    /// <summary>Сколько метала выдаёт в секунду при полной производительности.</summary>
    private float MetalOutput => Definition?.Conversion?.MetalOutput ?? 0f;

    public bool Working { get; private set; }

    public override void _Process(double delta) => QueueRedraw();

    public override void Declare(EconomyLedger ledger)
    {
        base.Declare(ledger);

        ledger.AddIncome(ResourceKind.Metal, MetalOutput);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        base.Run(dt, rates);

        // Синтезатору важна только энергия: метал он производит, и его нехватка
        // работу не ограничивает. Расход уже списан в Building.
        Working = rates.Energy > 0.01f;
        if (!Working || MetalOutput <= 0f)
            return;

        GameManager.I.Events.Append(new ResourceGained
        {
            Kind = ResourceKind.Metal,
            Amount = MetalOutput * (float)dt * rates.Energy,
        });
    }

    public override void _Draw()
    {
        base._Draw();

        if (Definition == null)
            return;

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        var color = Working ? new Color(0.4f, 1f, 0.5f) : new Color(0.5f, 0.5f, 0.5f);
        DrawCircle(new Vector2(rect.End.X - 10f, rect.Position.Y + 10f), 5f, color);
    }
}
