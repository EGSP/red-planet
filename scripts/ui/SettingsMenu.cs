using System.Collections.Generic;
using Godot;

/// <summary>
/// Настройки горячих клавиш: разделы по смыслу, строка на действие, назначение нажатием.
///
/// ОТДЕЛЬНОЕ ДЕРЕВО ПОД КОРНЕМ ПРИЛОЖЕНИЯ, а не часть меню паузы. Настройки открываются
/// и из главного меню, где сессии не существует вовсе, и из паузы, где она стоит; общего
/// у этих двух случаев нет ничего, кроме самого экрана. Держать его внутри одной из веток
/// значило бы либо заводить второй такой же, либо тянуть настройки за сессией, которой
/// они не принадлежат.
///
/// РАЗДЕЛЫ БЕРУТСЯ ИЗ КАРТЫ ДЕЙСТВИЙ, а не перечисляются здесь: действие, забытое в этом
/// файле, работало бы в игре, но не показывалось в настройках, и найти такую пропажу
/// можно было бы только случайно.
///
/// ОДНА КЛАВИША ВСТРЕЧАЕТСЯ В РАЗНЫХ РАЗДЕЛАХ, и это не ошибка: A задаёт приказ атаки
/// выделенному отряду и она же отбирает боевые машины, когда отряда нет. Разделы для того
/// и разведены — каждый отвечает своему положению, и внутри одного раздела клавиша
/// повториться не может.
/// </summary>
public partial class SettingsMenu : CanvasLayer
{
    private static readonly Color Shade = new(0f, 0f, 0f, 0.75f);
    private static readonly Color Heading = new(0.45f, 0.85f, 0.95f);
    private static readonly Color Waiting = new(1f, 0.72f, 0.28f);

    /// <summary>Ширина колонки с названием действия. Клавиши должны стоять в один столбец.</summary>
    private const int TitleWidth = 280;

    /// <summary>Кнопки клавиш по имени действия — по ним обновляются подписи.</summary>
    private readonly Dictionary<string, Button> _keys = new();

    /// <summary>Действие, которому сейчас назначается клавиша, либо null.</summary>
    private string _capturing;

    public override void _Ready()
    {
        // Выше меню паузы, ниже панели отладки: настройки перекрывают игру, но не отладку
        Layer = 20;
        Build();
    }

    /// <summary>
    /// Клавиши ловим до всех остальных: пока идёт назначение, нажатие принадлежит настройкам
    /// и ничего в игре означать не должно. <see cref="Node._UnhandledKeyInput"/> приходит
    /// раньше <see cref="Node._UnhandledInput"/>, которым разбирают ввод меню паузы и системы,
    /// поэтому порядок обхода дерева здесь ни при чём.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (_capturing != null)
        {
            // Escape отменяет назначение, а не назначается: иначе первым же промахом
            // игрок потерял бы клавишу отмены и не смог бы закрыть настройки
            if (key.PhysicalKeycode != Key.Escape)
                Keybinds.Rebind(_capturing, key.PhysicalKeycode);

            StopCapture();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.PhysicalKeycode == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close() => QueueFree();

    // ── разметка ───────────────────────────────────────────────────────────────

    private void Build()
    {
        var frame = new UiFrame();
        AddChild(frame);

        // Затемнение перехватывает мышь: сквозь настройки не должно быть ни щелчка
        var shade = new ColorRect { Color = Shade, MouseFilter = Control.MouseFilterEnum.Stop };
        frame.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var center = new CenterContainer();
        frame.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        var title = new Label
        {
            Text = "Горячие клавиши",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 26);
        column.AddChild(title);

        var hint = new Label
        {
            Text = "Щёлкните по клавише и нажмите новую. Escape — отмена.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.8f));
        column.AddChild(hint);

        column.AddChild(new HSeparator());

        // Высота ограничена, чтобы длинный список разделов не выпихивал кнопки за экран
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(TitleWidth + 180, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        BuildSections(list);

        column.AddChild(new HSeparator());

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);

        AddButton(buttons, "По умолчанию", ResetAll);
        AddButton(buttons, "Закрыть", Close);
    }

    /// <summary>
    /// Разделы идут в порядке объявления <see cref="InputSection"/>, а действия внутри —
    /// в порядке объявления карты. Пустой раздел пропускается: отладочные действия могут
    /// когда-нибудь скрыться от игрока целиком, и заголовок без строк выглядел бы ошибкой.
    /// </summary>
    private void BuildSections(Node parent)
    {
        foreach (InputSection section in System.Enum.GetValues<InputSection>())
        {
            var rows = new List<InputAction>();

            foreach (var action in InputActions.All)
                if (action.Section == section)
                    rows.Add(action);

            if (rows.Count == 0)
                continue;

            var caption = new Label { Text = InputActions.Title(section).ToUpperInvariant() };
            caption.AddThemeFontSizeOverride("font_size", 13);
            caption.AddThemeColorOverride("font_color", Heading);
            parent.AddChild(caption);

            foreach (var action in rows)
                parent.AddChild(BuildRow(action));

            parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        }
    }

    private Control BuildRow(InputAction action)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);

        var title = new Label
        {
            Text = action.Title,
            CustomMinimumSize = new Vector2(TitleWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
        };
        title.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(title);

        var button = new Button
        {
            Text = InputActions.KeyLabel(action.Name),
            CustomMinimumSize = new Vector2(140, 30),
        };
        button.AddThemeFontSizeOverride("font_size", 13);

        string name = action.Name;
        button.Pressed += () => StartCapture(name);

        _keys[name] = button;
        row.AddChild(button);

        return row;
    }

    private static void AddButton(Node parent, string text, System.Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(160, 36),
        };

        button.AddThemeFontSizeOverride("font_size", 15);
        button.Pressed += () => pressed();

        parent.AddChild(button);
    }

    // ── назначение ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Начать назначение. Прежнее незаконченное отменяется: два ожидания разом означали бы,
    /// что следующее нажатие достанется неизвестно кому.
    /// </summary>
    private void StartCapture(string action)
    {
        StopCapture();

        _capturing = action;

        if (!_keys.TryGetValue(action, out var button))
            return;

        // Фокус снимаем, иначе Space и Enter достались бы самой кнопке как нажатие
        // и назначить их было бы нельзя
        button.ReleaseFocus();

        button.Text = "нажмите…";
        button.AddThemeColorOverride("font_color", Waiting);
    }

    private void StopCapture()
    {
        if (_capturing == null)
            return;

        string action = _capturing;
        _capturing = null;

        if (!_keys.TryGetValue(action, out var button))
            return;

        button.RemoveThemeColorOverride("font_color");
        RefreshLabels();
    }

    private void ResetAll()
    {
        StopCapture();
        Keybinds.ResetAll();
        RefreshLabels();
    }

    /// <summary>
    /// Обновить подписи всех кнопок, а не одной изменённой: назначение задевает и тех,
    /// кто делит клавишу с изменённым действием.
    /// </summary>
    private void RefreshLabels()
    {
        foreach (var (action, button) in _keys)
            button.Text = InputActions.KeyLabel(action);
    }
}
