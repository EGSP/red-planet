using Godot;

/// <summary>
/// Полоса ресурсов — отдельное дерево интерфейса у верхнего края, как в PA.
///
/// Отделена от прочего интерфейса потому, что живёт по своим правилам: показывает состояние
/// базы целиком и потому не зависит ни от выделения, ни от того, что игрок сейчас делает.
/// Панели выделения, команд и строительства появляются и исчезают, а эта висит всегда.
/// </summary>
public partial class ResourceBar : CanvasLayer
{
    private static readonly Color MetalColor = new(0.85f, 0.88f, 0.95f);
    private static readonly Color EnergyColor = new(1f, 0.92f, 0.5f);

    private Gauge _metal;
    private Gauge _energy;
    private Label _efficiency;
    private PanelContainer _panel;
    private MarginContainer _margin;
    private int _top = HudLayout.TopMargin;

    /// <summary>Нижний край полосы: по нему выравнивается <see cref="PortalCountdownBar"/>.</summary>
    public float BottomEdge => HudLayout.BottomOf(_panel);

    /// <summary>Показ одного ресурса: запас из потолка, полоска и баланс прихода с расходом.</summary>
    private sealed class Gauge
    {
        public Label Amount;
        public ProgressBar Fill;
        public Label Flow;
    }

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        // Прижимаем к верху и центрируем средствами контейнеров: якоря пришлось бы
        // пересчитывать при каждой смене длины чисел
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Отступ сверху оставляет место под <see cref="EventBar"/>: самый верх занят
        // полосой событий, потому что срок волны читается вместе с запасом ресурсов.
        // Точная величина ставится в _Process по измеренному краю той полосы
        _margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _margin.AddThemeConstantOverride("margin_top", _top);
        column.AddChild(_margin);

        var center = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _margin.AddChild(center);

        _panel = new PanelContainer();
        center.AddChild(_panel);

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(HudLayout.CenterWidth, 0) };
        row.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(row);

        _metal = AddGauge(row, "МЕТАЛ", MetalColor);

        row.AddChild(new VSeparator());
        row.AddChild(BuildEfficiency());
        row.AddChild(new VSeparator());

        _energy = AddGauge(row, "ЭНЕРГИЯ", EnergyColor);
    }

    private Control BuildEfficiency()
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(120, 0) };

        var caption = new Label
        {
            Text = "ПРОИЗВОДИТЕЛЬНОСТЬ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        caption.AddThemeFontSizeOverride("font_size", 10);
        column.AddChild(caption);

        _efficiency = new Label
        {
            Text = "100%",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _efficiency.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(_efficiency);

        return column;
    }

    private static Gauge AddGauge(Node parent, string caption, Color color)
    {
        // Наименьшая ширина взята с запасом вниз, а излишек строки делится между обоими
        // ресурсами поровну: общая ширина блока задана снаружи, и подгонять под неё
        // числа отдельных столбцов пришлось бы при каждой правке раскладки
        var column = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(190, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 2);
        parent.AddChild(column);

        var head = new HBoxContainer();
        column.AddChild(head);

        var name = new Label { Text = caption };
        name.AddThemeFontSizeOverride("font_size", 10);
        name.AddThemeColorOverride("font_color", color);
        head.AddChild(name);

        var gauge = new Gauge();

        gauge.Amount = new Label
        {
            Text = "0/0",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        gauge.Amount.AddThemeFontSizeOverride("font_size", 14);
        gauge.Amount.AddThemeColorOverride("font_color", color);
        head.AddChild(gauge.Amount);

        gauge.Fill = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 8),
            MaxValue = 1.0,
        };
        column.AddChild(gauge.Fill);

        gauge.Flow = new Label { Text = "" };
        gauge.Flow.AddThemeFontSizeOverride("font_size", 11);
        column.AddChild(gauge.Flow);

        return gauge;
    }

    public override void _Process(double delta)
    {
        HudLayout.StackUnder(_margin, ref _top, this.Sibling<EventBar>()?.BottomEdge ?? 0f);

        var gm = GameManager.I;
        if (gm == null)
            return;

        var stockpile = gm.Stockpile;
        var economy = gm.Economy;

        Show(_metal, stockpile, ResourceKind.Metal, economy.MetalIncome, economy.MetalDemand);
        Show(_energy, stockpile, ResourceKind.Energy, economy.EnergyIncome, economy.EnergyDemand);

        // Просадку показываем цветом: при полном ходе число не должно мозолить глаз
        int percent = Mathf.RoundToInt(economy.Efficiency * 100f);
        _efficiency.Text = $"{percent}%";
        _efficiency.AddThemeColorOverride("font_color", percent >= 95
            ? new Color(0.6f, 0.85f, 0.6f)
            : percent >= 50
                ? new Color(0.95f, 0.8f, 0.4f)
                : new Color(1f, 0.5f, 0.45f));
    }

    /// <summary>
    /// Показываем именно СПРОС, а не то, сколько удалось влить. Ужатый расход всегда
    /// выглядит почти сведённым с доходом — по нему не понять, чего и насколько не хватает,
    /// а по спросу видно сразу: столько-то генераторов недостаёт.
    /// </summary>
    private static void Show(Gauge gauge, StockpileProjection stockpile, ResourceKind kind,
        float income, float demand)
    {
        float amount = stockpile.Get(kind);
        float capacity = stockpile.Capacity(kind);

        gauge.Amount.Text = $"{amount:0}/{capacity:0}";
        gauge.Fill.Value = capacity > 0f ? amount / capacity : 0f;

        float net = income - demand;
        string sign = net >= 0f ? "+" : "−";

        gauge.Flow.Text = $"+{income:0.#}  −{demand:0.#}   {sign}{Mathf.Abs(net):0.#}/с";
        gauge.Flow.AddThemeColorOverride("font_color", net >= 0f
            ? new Color(0.6f, 0.85f, 0.6f)
            : new Color(1f, 0.5f, 0.45f));
    }
}
