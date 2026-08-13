using Godot;

/// <summary>
/// Адаптивная строка свойства для кодовых редакторов Godot.
///
/// В обычном режиме образует три логические колонки: название, редактор значения,
/// происхождение и действия. Для длинного значения редактор переносится под заголовок,
/// а название и действия остаются в первой строке.
/// </summary>
[Tool]
public partial class EditorPropertyRow : PanelContainer
{
    private readonly VBoxContainer _body;

    public EditorPropertyRow()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(1f, 1f, 1f, 0.025f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 5,
        };
        AddThemeStyleboxOverride("panel", style);

        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 3);
        AddChild(_body);
    }

    public void Configure(
        string title,
        string tooltip,
        Control editor,
        Control sourceAndActions,
        bool editorBelow)
    {
        if (editorBelow)
        {
            var header = new HBoxContainer();
            header.AddChild(BuildTitle(title, tooltip, expand: true));
            header.AddChild(sourceAndActions);
            _body.AddChild(header);

            editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _body.AddChild(editor);
            return;
        }

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 8);
        columns.AddChild(BuildTitle(title, tooltip, expand: false));

        editor.CustomMinimumSize = new Vector2(
            Mathf.Max(editor.CustomMinimumSize.X, 100f),
            editor.CustomMinimumSize.Y);
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(editor);
        columns.AddChild(sourceAndActions);
        _body.AddChild(columns);
    }

    private static Label BuildTitle(string text, string tooltip, bool expand) => new()
    {
        Text = text,
        TooltipText = tooltip,
        CustomMinimumSize = expand ? Vector2.Zero : new Vector2(112, 0),
        SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin,
        VerticalAlignment = VerticalAlignment.Center,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
    };
}
