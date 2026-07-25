using Godot;

/// <summary>
/// Интерфейс: ресурсы общего хранилища и панель построек коммандера.
/// Кнопки только порождают намерение — постановку делает CommandSystem.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _resources;
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

        _resources = new Label { Text = "руда 0   метал 0" };
        _resources.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(_resources);

        box.AddChild(new HSeparator());

        var title = new Label { Text = "Строительство" };
        title.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(title);

        _buttons = new VBoxContainer();
        box.AddChild(_buttons);

        foreach (var def in GameManager.I.Catalog.AvailableFor("commander"))
            AddBuildButton(def);

        box.AddChild(new HSeparator());

        _status = new Label { Text = "ПКМ — идти или копать" };
        _status.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_status);
    }

    private void AddBuildButton(BuildableDef def)
    {
        string cost = def.CostOre > 0f
            ? $"{def.CostOre:0} руды"
            : $"{def.CostMetal:0} метала";

        var button = new Button { Text = $"{def.DisplayName} — {cost}" };
        button.Pressed += () => GameManager.I.Command?.BeginBuild(def);
        _buttons.AddChild(button);
    }

    public override void _Process(double delta)
    {
        var gm = GameManager.I;
        var stockpile = gm.Stockpile;

        _resources.Text = $"руда {stockpile.Get(ResourceKind.Ore):0}   " +
                          $"метал {stockpile.Get(ResourceKind.Metal):0}";

        var pending = gm.Command?.Pending;
        _status.Text = pending != null
            ? $"ставим: {pending.DisplayName}\nЛКМ — поставить, ПКМ — отмена"
            : "ПКМ — идти или копать\nWASD и колесо — камера";
    }
}
