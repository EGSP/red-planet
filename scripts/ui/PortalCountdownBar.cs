using Godot;

/// <summary>
/// Полоса ожидания победы: появляется, когда в мире есть достроенный портал, и заполняется
/// за минуту удержания. Отдельный слой — как полоса ресурсов: показ не зависит от выделения.
/// </summary>
public partial class PortalCountdownBar : CanvasLayer
{
    private static readonly Color Accent = new(0.35f, 0.78f, 0.92f);
    private static readonly Color Dim = new(0.55f, 0.6f, 0.65f);

    private Control _panel;
    private ProgressBar _fill;
    private Label _caption;
    private Label _time;

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_top", 56);
        column.AddChild(margin);

        var center = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(center);

        _panel = new PanelContainer { Visible = false };
        center.AddChild(_panel);

        var rows = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        rows.AddThemeConstantOverride("separation", 4);
        _panel.AddChild(rows);

        var header = new HBoxContainer();
        rows.AddChild(header);

        _caption = new Label { Text = "АКТИВАЦИЯ ПОРТАЛА" };
        _caption.AddThemeFontSizeOverride("font_size", 12);
        _caption.AddThemeColorOverride("font_color", Accent);
        header.AddChild(_caption);

        _time = new Label
        {
            Text = "0:00",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _time.AddThemeFontSizeOverride("font_size", 12);
        _time.AddThemeColorOverride("font_color", Dim);
        header.AddChild(_time);

        _fill = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 14),
            MaxValue = 1.0,
            ShowPercentage = false,
        };
        rows.AddChild(_fill);
    }

    public override void _Process(double delta)
    {
        var outcome = GameManager.I?.System<VictorySystem>();
        var session = this.Ancestor<Session>();

        if (outcome == null || session == null || session.Outcome != SessionOutcome.None)
        {
            _panel.Visible = false;
            return;
        }

        bool show = outcome.PortalCount > 0;
        _panel.Visible = show;

        if (!show)
            return;

        _fill.Value = outcome.HoldRatio;

        float left = Mathf.Max(0f, outcome.VictoryHoldSeconds - outcome.HoldElapsed);
        int seconds = Mathf.CeilToInt(left);
        _time.Text = $"{seconds / 60}:{seconds % 60:00}";
    }
}
