using Godot;

/// <summary>
/// Полоса боевых групп в самом низу экрана: цифра слота и число юнитов в нём.
///
/// Показываются все десять слотов, включая пустые. Раскладка групп — то, что игрок держит
/// в мышечной памяти, и она должна оставаться на одном месте: если пустые слоты прятать,
/// занятые будут ездить по полосе при каждом назначении, и цифра перестанет совпадать
/// с положением на экране.
///
/// Три состояния, различаемые с одного взгляда: выбранная группа подсвечена, занятые
/// показаны обычным цветом, пустые притушены.
/// </summary>
public partial class ControlGroupBar : CanvasLayer
{
    /// <summary>Выбранная: подсветка и рамка.</summary>
    private static readonly Color Selected = new(0.55f, 0.95f, 1f);

    /// <summary>Занятая, но не выбранная.</summary>
    private static readonly Color Filled = new(0.85f, 0.88f, 0.95f);

    /// <summary>Пустая: тот же цвет, но приглушённый — слот виден, а внимания не просит.</summary>
    private static readonly Color Empty = new(0.85f, 0.88f, 0.95f, 0.28f);

    private static readonly Vector2 CellSize = new(40, 26);

    private readonly Label[] _digits = new Label[ControlGroups.Count];
    private readonly Label[] _sizes = new Label[ControlGroups.Count];
    private readonly PanelContainer[] _cells = new PanelContainer[ControlGroups.Count];

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_bottom", 4);
        column.AddChild(margin);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 4);
        margin.AddChild(row);

        for (int slot = 0; slot < ControlGroups.Count; slot++)
            row.AddChild(BuildCell(slot));
    }

    private Control BuildCell(int slot)
    {
        var cell = new PanelContainer { CustomMinimumSize = CellSize };
        _cells[slot] = cell;

        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 4);
        cell.AddChild(line);

        var digit = new Label { Text = ControlGroups.Label(slot) };
        digit.AddThemeFontSizeOverride("font_size", 13);
        line.AddChild(digit);
        _digits[slot] = digit;

        var size = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        size.AddThemeFontSizeOverride("font_size", 12);
        line.AddChild(size);
        _sizes[slot] = size;

        return cell;
    }

    public override void _Process(double delta)
    {
        var groups = GameManager.I?.Command?.Groups;

        if (groups == null)
            return;

        for (int slot = 0; slot < ControlGroups.Count; slot++)
        {
            int size = groups.Size(slot);
            bool current = groups.Current == slot;

            var color = size == 0 ? Empty : current ? Selected : Filled;

            _digits[slot].AddThemeColorOverride("font_color", color);
            _sizes[slot].AddThemeColorOverride("font_color", color);
            _sizes[slot].Text = size > 0 ? size.ToString() : "";

            // Подсветку даём всей ячейке, а не только тексту: цифра мелкая, и по одному
            // цвету шрифта выбранный слот на краю экрана не выхватить
            _cells[slot].Modulate = current
                ? new Color(1.35f, 1.35f, 1.35f)
                : Colors.White;
        }
    }
}
