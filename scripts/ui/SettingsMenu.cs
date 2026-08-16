using System.Collections.Generic;
using Godot;

/// <summary>
/// Экран настроек: разделы разнесены по вкладкам — «Управление» и «Графика».
///
/// ОТДЕЛЬНОЕ ДЕРЕВО ПОД КОРНЕМ ПРИЛОЖЕНИЯ, а не часть меню паузы. Настройки открываются
/// и из главного меню, где сессии не существует вовсе, и из паузы, где она стоит; общего
/// у этих двух случаев нет ничего, кроме самого экрана. Держать его внутри одной из веток
/// значило бы либо заводить второй такой же, либо тянуть настройки за сессией, которой
/// они не принадлежат.
///
/// ВКЛАДКИ, А НЕ ОДИН СПИСОК. Управление занимает несколько десятков строк и требует
/// прокрутки; настройки изображения к клавишам отношения не имеют и терялись бы в конце
/// этого списка. Разделение по вкладкам оставляет каждому разделу собственную высоту.
///
/// РАЗДЕЛЫ УПРАВЛЕНИЯ БЕРУТСЯ ИЗ КАРТЫ ДЕЙСТВИЙ, а не перечисляются здесь: действие,
/// забытое в этом файле, работало бы в игре, но не показывалось в настройках, и найти
/// такую пропажу можно было бы только случайно.
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
    private static readonly Color Hint = new(0.7f, 0.75f, 0.8f);

    /// <summary>Ширина колонки с названием действия. Клавиши должны стоять в один столбец.</summary>
    private const int TitleWidth = 280;

    /// <summary>
    /// Размер области вкладок. Высота ограничена, чтобы кнопки не выпихивались за экран,
    /// и согласована с <see cref="UiScale"/>: от неё зависит, какие ступени масштаба
    /// вообще доступны, поскольку этот экран — самое высокое из окон игры.
    /// </summary>
    private static readonly Vector2 PageSize = new(TitleWidth + 180, 320);

    /// <summary>Кнопки клавиш по имени действия — по ним обновляются подписи.</summary>
    private readonly Dictionary<string, Button> _keys = new();

    /// <summary>Действие, которому сейчас назначается клавиша, либо null.</summary>
    private string _capturing;

    private HSlider _scaleSlider;
    private Label _scaleValue;
    private OptionButton _panMode;

    /// <summary>
    /// Идёт перетаскивание ползунка масштаба. Пока оно идёт, масштаб не применяется:
    /// применение сдвигает сам ползунок под курсором, и захваченная мышью ручка начала бы
    /// убегать от указателя. Значение применяется по отпусканию, а до тех пор меняется
    /// только подпись.
    /// </summary>
    private bool _dragging;

    /// <summary>
    /// Кнопка мыши, отпускание которой предстоит проглотить. Назначение завершается
    /// нажатием, но отпускание приходит после него и досталось бы кнопке интерфейса,
    /// под которой стоит курсор, — то есть назначение началось бы заново.
    /// </summary>
    private MouseButton _swallow = MouseButton.None;

    /// <summary>
    /// Открыт ли экран настроек прямо сейчас. Признак нужен камере: в режиме клавиш она
    /// перехватывает нажатия до интерфейса, и, пока идёт назначение, ход камеры отнял бы
    /// у настроек ту самую клавишу, которую игрок назначает.
    ///
    /// Счётчик, а не булево поле: два экрана разом <see cref="Root.OpenSettings"/>
    /// не допускает, но закрытие одного не должно снимать признак, поставленный другим,
    /// если запрет однажды ослабнет.
    /// </summary>
    public static bool AnyOpen => _open > 0;

    private static int _open;

    public override void _Ready()
    {
        // Выше меню паузы, ниже панели отладки: настройки перекрывают игру, но не отладку
        Layer = 20;
        _open++;
        Build();
    }

    public override void _ExitTree() => _open--;

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
                Keybinds.Rebind(_capturing, InputBinding.Of(key.PhysicalKeycode));

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

    /// <summary>
    /// Назначение кнопки мыши. Разбирается в <see cref="Node._Input"/>, а не в
    /// <see cref="Node._UnhandledInput"/>, поскольку до последнего нажатие мыши не доходит
    /// вовсе: затемнение и кнопки настроек перехватывают его как обычный щелчок по
    /// интерфейсу. Ввод помечается обработанным, поэтому нажатие, ушедшее в назначение,
    /// не считается щелчком.
    ///
    /// Кнопка, назначить которую нельзя (<see cref="InputBinding.CanBind"/>), отменяет
    /// назначение вместо того, чтобы быть молча пропущенной: щелчок мимо — обычный способ
    /// передумать, и молчание выглядело бы так, будто настройки зависли в ожидании.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse)
            return;

        if (!mouse.Pressed)
        {
            if (mouse.ButtonIndex != _swallow)
                return;

            _swallow = MouseButton.None;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_capturing == null)
            return;

        if (InputBinding.CanBind(mouse.ButtonIndex))
            Keybinds.Rebind(_capturing, InputBinding.Of(mouse.ButtonIndex));

        StopCapture();

        // Колесо отпускания не имеет, и глотать у него нечего
        _swallow = mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
            ? MouseButton.None
            : mouse.ButtonIndex;

        GetViewport().SetInputAsHandled();
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
            Text = "Настройки",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 26);
        column.AddChild(title);

        var tabs = new TabContainer { CustomMinimumSize = PageSize };
        column.AddChild(tabs);

        var controls = BuildControlsPage();
        controls.Name = "Управление";
        tabs.AddChild(controls);

        var camera = BuildCameraPage();
        camera.Name = "Камера";
        tabs.AddChild(camera);

        var graphics = BuildGraphicsPage();
        graphics.Name = "Графика";
        tabs.AddChild(graphics);

        column.AddChild(new HSeparator());

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);

        AddButton(buttons, "Закрыть", Close);
    }

    // ── вкладка управления ─────────────────────────────────────────────────────

    private Control BuildControlsPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);

        page.AddChild(HintLabel(
            "Щёлкните по назначению и нажмите новую клавишу либо среднюю или боковую "
            + "кнопку мыши. Escape — отмена."));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        page.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        // Камера вынесена на свою вкладку целиком: там к её клавишам примыкает выбор
        // способа панорамирования, без которого половина этих клавиш не действует вовсе
        BuildSections(list, section => section != InputSection.Camera);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        page.AddChild(footer);

        AddButton(footer, "Клавиши по умолчанию", ResetKeys);

        return page;
    }

    /// <summary>
    /// Разделы идут в порядке объявления <see cref="InputSection"/>, а действия внутри —
    /// в порядке объявления карты. Пустой раздел пропускается: отладочные действия могут
    /// когда-нибудь скрыться от игрока целиком, и заголовок без строк выглядел бы ошибкой.
    ///
    /// Отбор разделов задаётся условием, а не перечнем: вкладок с клавишами две, и каждая
    /// показывает то, что не показывает другая, поэтому забытый раздел не исчезнет
    /// из настроек молча.
    /// </summary>
    private void BuildSections(Node parent, System.Func<InputSection, bool> allowed)
    {
        foreach (InputSection section in System.Enum.GetValues<InputSection>())
        {
            if (!allowed(section))
                continue;

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

    // ── вкладка камеры ─────────────────────────────────────────────────────────

    /// <summary>
    /// Камера: способ панорамирования и её собственные клавиши.
    ///
    /// СПОСОБ СТОИТ ПЕРВЫМ, ДО КЛАВИШ, потому что от него зависит, действуют ли клавиши
    /// хода вообще: в режиме края экрана они не читаются, и показывать их прежде выбора
    /// значило бы обещать действие, которого нет.
    /// </summary>
    private Control BuildCameraPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 8);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        page.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(list);

        var caption = new Label { Text = "ПАНОРАМИРОВАНИЕ" };
        caption.AddThemeFontSizeOverride("font_size", 13);
        caption.AddThemeColorOverride("font_color", Heading);
        list.AddChild(caption);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        list.AddChild(row);

        var label = new Label
        {
            Text = "Чем вести камеру",
            CustomMinimumSize = new Vector2(TitleWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(label);

        _panMode = new OptionButton { CustomMinimumSize = new Vector2(140, 30) };
        _panMode.AddThemeFontSizeOverride("font_size", 13);

        foreach (CameraPanMode mode in System.Enum.GetValues<CameraPanMode>())
            _panMode.AddItem(CameraControls.Label(mode), (int)mode);

        _panMode.Selected = _panMode.GetItemIndex((int)CameraControls.PanMode);
        _panMode.ItemSelected += OnPanModeSelected;
        row.AddChild(_panMode);

        list.AddChild(HintLabel(
            "Курсором у края экрана — камера идёт, когда указатель подведён к границе окна. "
            + "Клавишами — камера идёт по W, A, S, D, а край окна её не двигает. "
            + "В режиме клавиш нажатие достаётся камере целиком, поэтому A под ней "
            + "не отдаёт приказ атаки; зажатый Alt возвращает клавишам обычный смысл."));

        list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        list.AddChild(HintLabel("Щёлкните по назначению и нажмите новую клавишу либо "
            + "среднюю или боковую кнопку мыши. Escape — отмена."));

        BuildSections(list, section => section == InputSection.Camera);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        page.AddChild(footer);

        AddButton(footer, "Панорамирование по умолчанию", ResetPanMode);

        return page;
    }

    private void OnPanModeSelected(long index) =>
        CameraControls.SetPanMode((CameraPanMode)_panMode.GetItemId((int)index));

    private void ResetPanMode()
    {
        CameraControls.Reset();
        _panMode.Selected = _panMode.GetItemIndex((int)CameraControls.PanMode);
    }

    // ── вкладка графики ────────────────────────────────────────────────────────

    /// <summary>
    /// Масштаб интерфейса ползунком по ступеням <see cref="UiScale.Steps"/>. Ползунок ходит
    /// по НОМЕРАМ ступеней, а не по самим множителям: промежуточных значений не существует,
    /// и вещественный отрезок обещал бы игроку то, чего набор не даёт.
    ///
    /// Крупные ступени на малом окне недоступны — верхняя граница ползунка берётся у
    /// <see cref="UiScale.MaxFor"/>, поскольку при них разметка перестала бы помещаться.
    /// </summary>
    private Control BuildGraphicsPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);

        var caption = new Label { Text = "МАСШТАБ ИНТЕРФЕЙСА" };
        caption.AddThemeFontSizeOverride("font_size", 13);
        caption.AddThemeColorOverride("font_color", Heading);
        page.AddChild(caption);

        page.AddChild(HintLabel(
            "Ступени кратны 25 %: при них размеры панелей остаются целыми числами "
            + "пикселей. Шрифты растеризуются заново под выбранный масштаб, поэтому "
            + "текст не размывается."));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        page.AddChild(row);

        float allowed = UiScale.MaxFor(GetViewport().GetVisibleRect().Size);

        _scaleSlider = new HSlider
        {
            MinValue = 0,
            MaxValue = UiScale.IndexOf(allowed),
            Step = 1,
            TickCount = UiScale.IndexOf(allowed) + 1,
            TicksOnBorders = true,
            Value = UiScale.IndexOf(UiScale.Current),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(260, 0),
        };

        _scaleSlider.ValueChanged += OnScaleValueChanged;
        _scaleSlider.DragStarted += () => _dragging = true;
        _scaleSlider.DragEnded += OnScaleDragEnded;
        row.AddChild(_scaleSlider);

        _scaleValue = new Label
        {
            Text = UiScale.Label(UiScale.Current),
            CustomMinimumSize = new Vector2(64, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _scaleValue.AddThemeFontSizeOverride("font_size", 14);
        row.AddChild(_scaleValue);

        // Про недоступные ступени сказано прямо: молча укороченный ползунок выглядел бы
        // так, будто крупных значений в игре нет вовсе
        if (allowed < UiScale.Steps[^1])
            page.AddChild(HintLabel(
                $"В окне нынешнего размера доступно до {UiScale.Label(allowed)}: "
                + "при большем значении интерфейс не помещается. Разверните окно "
                + "на весь экран, чтобы открылись остальные ступени."));

        // Распорка прижимает кнопку сброса к низу вкладки, как и на вкладке управления
        page.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        page.AddChild(footer);

        AddButton(footer, "Масштаб по умолчанию", ResetScale);

        return page;
    }

    private void OnScaleValueChanged(double value)
    {
        float step = UiScale.Steps[(int)value];
        _scaleValue.Text = UiScale.Label(step);

        if (!_dragging)
            UiScale.Set(step);
    }

    private void OnScaleDragEnded(bool changed)
    {
        _dragging = false;

        if (changed)
            UiScale.Set(UiScale.Steps[(int)_scaleSlider.Value]);
    }

    private void ResetScale()
    {
        UiScale.Reset();
        _scaleSlider.Value = UiScale.IndexOf(UiScale.Current);
        _scaleValue.Text = UiScale.Label(UiScale.Current);
    }

    // ── общее ──────────────────────────────────────────────────────────────────

    private static Label HintLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", Hint);

        return label;
    }

    private static void AddButton(Node parent, string text, System.Action pressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(200, 36),
        };

        button.AddThemeFontSizeOverride("font_size", 15);
        button.Pressed += () => pressed();

        parent.AddChild(button);
    }

    // ── назначение клавиш ──────────────────────────────────────────────────────

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

    private void ResetKeys()
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
