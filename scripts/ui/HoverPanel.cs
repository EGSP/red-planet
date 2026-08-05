using Godot;

/// <summary>
/// Справка о сущности под курсором: название, прочность, потоки ресурсов и вклад в террор.
/// Состав строк намеренно скуден, и строки с нулевыми величинами не показываются —
/// см. <see cref="HoverFacts"/>.
///
/// МЕСТО В СТОПКЕ ЛЕВОГО НИЖНЕГО УГЛА. Панель выделения (<see cref="SelectionPanel"/>)
/// главнее: она стоит у самого низа, а справка встаёт ярусом выше и поднимается вместе
/// с ростом списка выделенного. Порядок такой потому, что выделение — состояние, которое
/// игрок задал сам и держит, а наведение живёт ровно до следующего движения мыши;
/// прыгающая при этом панель выделения сбивала бы прицел по своим же строкам.
///
/// СУЩНОСТЬ О ПАНЕЛИ НЕ ЗНАЕТ. Что показано, решает <see cref="HoverSystem"/>, а из чего
/// складывается подпись — <see cref="HoverFacts"/>; отсюда не зовётся ни один метод юнита
/// или постройки.
/// </summary>
public partial class HoverPanel : CanvasLayer
{
    /// <summary>Просвет между ярусами стопки, пиксели.</summary>
    private const int StackGap = 6;

    private static readonly Color LabelColor = new(0.62f, 0.68f, 0.74f);
    private static readonly Color ValueColor = new(0.88f, 0.92f, 0.96f);

    private Control _frame;
    private MarginContainer _margin;
    private Label _title;
    private VBoxContainer _rows;

    private SelectionPanel _selection;

    /// <summary>Нижний отступ, применённый в прошлый раз: лишний раз тему не трогаем.</summary>
    private int _appliedMargin = -1;

    /// <summary>
    /// Отпечаток показанного: сущность и её прочность. Пока он тот же, подписи
    /// не переписываются — иначе текст присваивался бы заново каждый кадр наведения.
    /// </summary>
    private string _key = "";

    public override void _Ready()
    {
        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _frame.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _margin.AddThemeConstantOverride("margin_left", 12);
        row.AddChild(_margin);

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _margin.AddChild(column);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(210, 0) };
        column.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        _title = new Label { Text = "" };
        _title.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        box.AddChild(_rows);
    }

    public override void _Process(double delta)
    {
        var hovered = GameManager.I?.System<HoverSystem>()?.Hovered;

        if (hovered == null || hovered is Node node && !Alive.Is(node))
        {
            _frame.Visible = false;
            _key = "";
            return;
        }

        _frame.Visible = true;
        Restack();

        string key = KeyOf(hovered);

        if (key == _key)
            return;

        _key = key;
        Rebuild(hovered);
    }

    /// <summary>
    /// Поднять справку над панелью выделения. Высота нижнего яруса спрашивается каждый кадр:
    /// состав выделения меняется без всякого предупреждения, а сама справка держится
    /// ровно столько, сколько курсор стоит на сущности.
    /// </summary>
    private void Restack()
    {
        _selection ??= this.Sibling<SelectionPanel>();

        float stack = _selection?.StackHeight ?? 0f;
        int bottom = SelectionPanel.BottomMargin + Mathf.CeilToInt(stack)
                     + (stack > 0f ? StackGap : 0);

        if (bottom == _appliedMargin)
            return;

        _appliedMargin = bottom;
        _margin.AddThemeConstantOverride("margin_bottom", bottom);
    }

    /// <summary>
    /// Отпечаток показанного. Изменчива у сущности одна прочность, а название и цвет
    /// берутся из справочника и меняться не могут.
    /// </summary>
    private static string KeyOf(IDamageable target)
    {
        int health = target.Health != null ? Mathf.RoundToInt(target.Health.Current) : 0;

        return $"{target.EntityId}#{health}";
    }

    private void Rebuild(IDamageable target)
    {
        _title.Text = HoverFacts.TitleOf(target);
        _title.AddThemeColorOverride("font_color", HoverFacts.ColorOf(target));

        foreach (var child in _rows.GetChildren())
            child.QueueFree();

        foreach (var fact in HoverFacts.Collect(target))
            _rows.AddChild(Row(fact));
    }

    private static HBoxContainer Row(HoverFacts.Fact fact)
    {
        var line = new HBoxContainer();

        var label = new Label
        {
            Text = fact.Label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", LabelColor);
        line.AddChild(label);

        var value = new Label
        {
            Text = fact.Value,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.AddThemeFontSizeOverride("font_size", 13);
        value.AddThemeColorOverride("font_color", ValueColor);
        line.AddChild(value);

        return line;
    }
}
