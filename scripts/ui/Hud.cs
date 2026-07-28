using Godot;

/// <summary>
/// Интерфейс: ресурсы общего хранилища и панель построек коммандера.
/// Кнопки только порождают намерение — постановку делает CommandSystem.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _metal;
    private Label _energy;
    private Label _efficiency;
    private Label _combat;
    private Label _status;
    private VBoxContainer _buttons;

    public override void _Ready()
    {
        var panel = new PanelContainer
        {
            Position = new Vector2(12, 12),
            CustomMinimumSize = new Vector2(240, 0),
        };
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        _metal = new Label { Text = "метал 0" };
        _metal.AddThemeFontSizeOverride("font_size", 17);
        _metal.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.95f));
        box.AddChild(_metal);

        _energy = new Label { Text = "энергия 0" };
        _energy.AddThemeFontSizeOverride("font_size", 17);
        _energy.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.5f));
        box.AddChild(_energy);

        _efficiency = new Label { Text = "производительность 100%" };
        _efficiency.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_efficiency);

        _combat = new Label { Text = "врагов 0" };
        _combat.AddThemeFontSizeOverride("font_size", 13);
        _combat.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.65f));
        box.AddChild(_combat);

        box.AddChild(new HSeparator());

        var title = new Label { Text = "Строительство" };
        title.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(title);

        _buttons = new VBoxContainer();
        box.AddChild(_buttons);

        foreach (var def in GameManager.I.Catalog.AvailableFor("commander"))
            AddBuildButton(def);

        box.AddChild(new HSeparator());

        _status = new Label { Text = "ПКМ — идти, копать или атаковать" };
        _status.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_status);
    }

    private void AddBuildButton(BuildableDef def)
    {
        // Энергоцены у постройки нет: энергию тратит инструмент строителя, а не здание
        var button = new Button { Text = $"{def.DisplayName} — {def.CostMetal:0} метала" };
        button.Pressed += () => GameManager.I.Command?.BeginBuild(def);
        _buttons.AddChild(button);
    }

    public override void _Process(double delta)
    {
        var gm = GameManager.I;
        var stockpile = gm.Stockpile;
        var economy = gm.Economy;

        _metal.Text = Line("метал", stockpile, ResourceKind.Metal,
            economy.MetalIncome, economy.MetalDemand);

        _energy.Text = Line("энергия", stockpile, ResourceKind.Energy,
            economy.EnergyIncome, economy.EnergyDemand);

        // Просадку показываем цветом: при полном ходе число не должно мозолить глаз
        int percent = Mathf.RoundToInt(economy.Efficiency * 100f);
        _efficiency.Text = $"производительность {percent}%";
        _efficiency.AddThemeColorOverride("font_color", percent >= 95
            ? new Color(0.6f, 0.85f, 0.6f)
            : percent >= 50
                ? new Color(0.95f, 0.8f, 0.4f)
                : new Color(1f, 0.5f, 0.45f));

        var combat = gm.Combat;
        float commanderDamage = gm.Commander?.Health.TotalTaken ?? 0f;

        _combat.Text = $"врагов на карте {combat.EnemiesAlive}   " +
                       $"уничтожено {combat.EnemiesDestroyed}   потеряно {combat.LossesTaken}\n" +
                       $"урон коммандеру {commanderDamage:0}";

        var pending = gm.Command?.Pending;
        _status.Text = pending != null
            ? $"ставим: {pending.DisplayName}\nЛКМ — поставить, ПКМ — отмена"
            : "ПКМ — идти, копать или атаковать врага\nWASD и колесо — камера";
    }

    /// <summary>
    /// Строка ресурса в духе PA: запас из потолка, а рядом приход и ЗАПРОШЕННЫЙ расход.
    ///
    /// Показываем именно спрос, а не то, сколько удалось влить. Ужатый расход всегда
    /// выглядит почти сведённым с доходом — по нему не понять, чего и насколько не хватает,
    /// а по спросу видно сразу: столько-то генераторов недостаёт.
    /// </summary>
    private static string Line(string name, StockpileProjection stockpile, ResourceKind kind,
        float income, float demand)
    {
        float net = income - demand;
        string sign = net >= 0f ? "+" : "−";

        return $"{name} {stockpile.Get(kind):0}/{stockpile.Capacity(kind):0}   " +
               $"{sign}{Mathf.Abs(net):0.#}/с   (+{income:0.#} −{demand:0.#})";
    }
}
