using System.Collections.Generic;
using Godot;

/// <summary>
/// Панель выделения у левого края: сколько выделено и кого именно.
///
/// Сущности сводятся по имени, а не перечисляются поштучно: двадцать копателей — это
/// строка «Копатель ×20», а не двадцать одинаковых строк. Игроку важен состав отряда,
/// а не поимённый список.
/// </summary>
public partial class SelectionPanel : CanvasLayer
{
    private static readonly Color CountColor = new(0.65f, 0.95f, 0.75f);

    private Control _frame;
    private Label _total;
    private VBoxContainer _kinds;

    /// <summary>Отпечаток состава: пересобираем строки только когда он сменился.</summary>
    private string _key = "";

    public override void _Ready()
    {
        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        // Прижимаем к левому краю, по вертикали — к низу: сверху полоса ресурсов,
        // а внизу слева панель выделения соседствует со строительной, как в PA
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _frame.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        row.AddChild(margin);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(210, 0) };
        column.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        _total = new Label { Text = "" };
        _total.AddThemeFontSizeOverride("font_size", 15);
        _total.AddThemeColorOverride("font_color", CountColor);
        box.AddChild(_total);

        box.AddChild(new HSeparator());

        _kinds = new VBoxContainer();
        _kinds.AddThemeConstantOverride("separation", 2);
        box.AddChild(_kinds);
    }

    public override void _Process(double delta)
    {
        var selected = GameManager.I?.Command?.Selected;

        if (selected == null || selected.Count == 0)
        {
            _frame.Visible = false;
            _key = "";
            return;
        }

        var kinds = CountByName(selected);
        string key = string.Join('|', kinds.Keys) + $"#{selected.Count}";

        _frame.Visible = true;

        if (key == _key)
            return;

        _key = key;
        Rebuild(selected.Count, kinds);
    }

    /// <summary>Сколько кого выделено. Порядок появления сохраняем — он привычнее алфавита.</summary>
    private static Dictionary<string, int> CountByName(IReadOnlyList<IOrderable> selected)
    {
        var kinds = new Dictionary<string, int>();

        foreach (var actor in selected)
        {
            string name = actor.DisplayName;
            kinds.TryGetValue(name, out int count);
            kinds[name] = count + 1;
        }

        return kinds;
    }

    private void Rebuild(int total, Dictionary<string, int> kinds)
    {
        _total.Text = $"Выделено: {total}";

        foreach (var child in _kinds.GetChildren())
            child.QueueFree();

        foreach (var (name, count) in kinds)
        {
            var line = new HBoxContainer();

            var title = new Label
            {
                Text = name,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            title.AddThemeFontSizeOverride("font_size", 13);
            line.AddChild(title);

            var amount = new Label
            {
                Text = $"×{count}",
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            amount.AddThemeFontSizeOverride("font_size", 13);
            amount.AddThemeColorOverride("font_color", CountColor);
            line.AddChild(amount);

            _kinds.AddChild(line);
        }
    }
}
