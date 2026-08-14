using Godot;

/// <summary>
/// Ограниченная рамкой область интерфейса с заголовком и полосой действий.
///
/// ЗАЧЕМ РАМКА. Каталог, поле, форма и таблицы стоят вплотную и различаются только
/// содержимым, отчего интерфейс читается как одна сплошная поверхность. Рамка и фон
/// делают границу области явной, а заголовок называет её.
///
/// ЗАЧЕМ ПОЛОСА ДЕЙСТВИЙ В ЗАГОЛОВКЕ. Кнопка, помещённая в заголовок области, действует
/// на её содержимое, и это видно по расположению. Общая полоса кнопок наверху окна
/// такого указания не даёт: по ней нельзя определить, к какой из областей относится
/// действие.
/// </summary>
[Tool]
public partial class EditorPanel : PanelContainer
{
    private readonly Label _title;
    private readonly HBoxContainer _header;
    private readonly HBoxContainer _actions;
    private readonly VBoxContainer _content;

    public EditorPanel() : this("")
    {
    }

    public EditorPanel(string title)
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var style = ContentEditorTheme.PanelStyle();
        AddThemeStyleboxOverride("panel", style);

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 5);
        AddChild(body);

        _header = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _header.AddThemeConstantOverride("separation", 6);
        body.AddChild(_header);

        _title = new Label
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Modulate = ContentEditorTheme.PanelTitle(),
        };
        _title.AddThemeFontSizeOverride("font_size", 12);
        _header.AddChild(_title);

        _actions = new HBoxContainer();
        _actions.AddThemeConstantOverride("separation", 4);
        _header.AddChild(_actions);

        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _content.AddThemeConstantOverride("separation", 4);
        body.AddChild(_content);

        _header.Visible = !string.IsNullOrEmpty(title);
    }

    /// <summary>Полоса действий заголовка: сюда добавляются кнопки этой области.</summary>
    public HBoxContainer Actions => _actions;

    public string Title
    {
        get => _title.Text;
        set
        {
            _title.Text = value ?? "";
            _header.Visible = _title.Text.Length > 0 || _actions.GetChildCount() > 0;
        }
    }

    public string TitleTooltip
    {
        get => _title.TooltipText;
        set => _title.TooltipText = value ?? "";
    }

    /// <summary>Цвет заголовка: им различаются области одного вида, например команды.</summary>
    public void SetTitleColor(Color color)
    {
        _title.Modulate = Colors.White;
        _title.AddThemeColorOverride("font_color", color);
    }

    public void AddContent(Control control)
    {
        _content.AddChild(control);
        _header.Visible = _title.Text.Length > 0 || _actions.GetChildCount() > 0;
    }

    public void ClearContent()
    {
        foreach (Node child in _content.GetChildren())
        {
            // RemoveChild исключает узел из раскладки немедленно; один QueueFree оставлял
            // прежнее и новое содержимое рядом до конца кадра.
            _content.RemoveChild(child);
            child.QueueFree();
        }
    }
}
