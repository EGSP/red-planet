using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Строительная панель — отдельное дерево интерфейса внизу экрана, секции в ряд.
///
/// Отделена от основного HUD намеренно. HUD показывает состояние базы и живёт всё время,
/// пока идёт игра; панель же появляется и исчезает вслед за выделением и перестраивается
/// целиком. Держи их в одном дереве — и перестройка сетки дёргала бы разметку остальных
/// показаний, а место панели зависело бы от длины строки с ресурсами.
///
/// Что откуда берётся: набор панелей — от выделенных строителей и заводов, раскладка —
/// от BuildbarLayout, содержимое ячеек — из справочника.
/// </summary>
public partial class Buildbar : CanvasLayer
{
    /// <summary>Размер ячейки. Один на все кнопки — иначе сетка поплывёт.</summary>
    private static readonly Vector2 CellSize = new(118, 30);

    private static readonly Color TitleColor = new(0.45f, 0.85f, 0.95f);
    private static readonly Color TipColor = new(0.9f, 0.92f, 0.95f);

    private Control _frame;
    private HBoxContainer _sections;

    /// <summary>
    /// Подсказка панели: появляется сразу при наведении и сидит над кнопкой, а не
    /// у курсора. Встроенный TooltipText Godot для этого не подходит — у него задержка
    /// и привязка к мыши.
    /// </summary>
    private PanelContainer _tip;
    private Label _tipLabel;
    private Button _tipOwner;

    /// <summary>
    /// Отпечаток выделения и очередей заводов. Сетку пересобираем только когда он
    /// сменился — иначе кнопки перестраивались бы каждый кадр, теряя наведение.
    /// </summary>
    private string _key = "";

    /// <summary>Режим последней собранной панели: постановка или очередь завода.</summary>
    private BuildbarKind _kind = BuildbarKind.Placement;

    /// <summary>
    /// Заводы, чью очередь показывают счётчики (при kind = Plant). Каркас будущего завода
    /// стоит здесь наравне с готовым: заказ принимают оба (см. <see cref="IProducer"/>).
    /// </summary>
    private readonly List<IProducer> _plants = new();

    /// <summary>
    /// Кнопки с счётчиками очереди, собранные при постройке сетки. Держим список,
    /// чтобы обновление счётчиков не обходило дерево интерфейса заново.
    /// </summary>
    private readonly List<(Button Button, UnitDefinition Def)> _counted = new();

    /// <summary>Кнопка переключения режима производства. Есть только у plant-панели.</summary>
    private Button _modeButton;

    /// <summary>
    /// Сумма ревизий очередей на прошлом обновлении счётчиков. Пересчитываем подписи
    /// только когда сумма сменилась: перебирать состав очередей каждый кадр незачем.
    /// </summary>
    private int _queueRevision = -1;

    public override void _Ready()
    {
        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        // Прижимаем содержимое к низу и центрируем по горизонтали средствами самих
        // контейнеров: якоря пришлось бы пересчитывать при каждой смене состава секций
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        // Снизу оставляем место полосе боевых групп: она стоит у самого края
        margin.AddThemeConstantOverride("margin_bottom", 42);
        column.AddChild(margin);

        _sections = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _sections.AddThemeConstantOverride("separation", 10);
        margin.AddChild(_sections);

        BuildTip();
    }

    private void BuildTip()
    {
        _tip = new PanelContainer
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // Поверх кнопок панели: иначе подсказка уйдёт под соседнюю секцию
        _frame.AddChild(_tip);

        _tipLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _tipLabel.AddThemeFontSizeOverride("font_size", 13);
        _tipLabel.AddThemeColorOverride("font_color", TipColor);
        _tip.AddChild(_tipLabel);
    }

    public override void _Process(double delta)
    {
        var bars = BarsOf(GameManager.I?.Command, out var plants, out var kind);
        string key = Fingerprint(bars, plants, kind);

        if (key != _key)
        {
            _key = key;
            _kind = kind;
            _plants.Clear();
            _plants.AddRange(plants);
            _queueRevision = -1;
            HideTip();
            Rebuild(bars);
        }

        if (_kind == BuildbarKind.Plant)
            RefreshPlantCounts();

        if (_tip.Visible && _tipOwner != null && Alive.Is(_tipOwner))
            PlaceTip(_tipOwner);
    }

    /// <summary>
    /// Панели выделенных строителей и заводов. Приоритет у завода: если среди выделенных
    /// есть принимающий заказы, показывается только его plant-панель (очередь),
    /// а не постановка. Каркас завода в этом смысле от готового завода не отличается —
    /// очередь производства набирается ещё на стройке.
    /// </summary>
    private static List<BuildbarDefinition> BarsOf(CommandSystem command,
        out List<IProducer> plants, out BuildbarKind kind)
    {
        var bars = new List<BuildbarDefinition>();
        plants = new List<IProducer>();
        kind = BuildbarKind.Placement;

        if (command == null)
            return bars;

        foreach (var actor in command.Selected)
        {
            // Живость проверяем здесь: выделение чистится своей системой, а панель может
            // прийти раньше неё в том же кадре, и обращение к снесённому заводу упало бы
            if (actor is IProducer { CanProduce: true } plant && Alive.Is(plant as Node)
                                     && plant.Definition?.Buildbar is { } plantBarId)
            {
                plants.Add(plant);
                var bar = Content.Catalog.Buildbar(plantBarId);
                if (bar != null && !bars.Contains(bar))
                    bars.Add(bar);
            }
        }

        if (plants.Count > 0)
        {
            kind = BuildbarKind.Plant;
            return bars;
        }

        foreach (var actor in command.Selected)
        {
            if (actor is not Unit { Definition: { CanBuild: true } def })
                continue;

            var bar = Content.Catalog.Buildbar(def.Buildbar);

            if (bar != null && !bars.Contains(bar))
                bars.Add(bar);
        }

        return bars;
    }

    private static string Fingerprint(List<BuildbarDefinition> bars, List<IProducer> plants,
        BuildbarKind kind)
    {
        string ids = string.Join('|', bars.Select(bar => bar.Id).OrderBy(id => id));
        string plantIds = string.Join(',', plants.Select(plant => plant.Id));
        return $"{kind}:{ids}:{plantIds}";
    }

    private void Rebuild(List<BuildbarDefinition> bars)
    {
        foreach (var child in _sections.GetChildren())
            child.QueueFree();

        _counted.Clear();
        _modeButton = null;

        if (bars.Count == 0)
        {
            _frame.Visible = false;
            return;
        }

        _frame.Visible = true;

        foreach (var section in BuildbarLayout.Merge(bars).Sections)
            _sections.AddChild(BuildSection(section));

        // Режим производства принадлежит заводу, а не разделу справочника, поэтому кнопка
        // стоит отдельной секцией справа, а не внутри сетки юнитов
        if (_kind == BuildbarKind.Plant)
            _sections.AddChild(BuildModeSection());
    }

    /// <summary>Секция: сетка ячеек, под нею подпись — как в строительной панели PA.</summary>
    private Control BuildSection(BuildbarLayout.Section section)
    {
        // Секции разной высоты прижимаем к низу, чтобы подписи встали на одну линию
        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        frame.AddChild(column);

        var grid = new VBoxContainer();
        grid.AddThemeConstantOverride("separation", 4);
        column.AddChild(grid);

        // Снизу вверх в справочнике — сверху вниз на экране
        for (int y = section.Rows.Count - 1; y >= 0; y--)
            grid.AddChild(BuildRow(section.Rows[y]));

        var title = new Label
        {
            Text = section.Title.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 12);
        title.AddThemeColorOverride("font_color", TitleColor);
        column.AddChild(title);

        return frame;
    }

    /// <summary>
    /// Секция с единственной кнопкой — переключателем режима производства. Отдельная
    /// секция, а не ячейка сетки: ячейки заняты определениями из справочника, и вставить
    /// среди них кнопку, у которой нет определения, значило бы сломать раскладку.
    /// </summary>
    private Control BuildModeSection()
    {
        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        frame.AddChild(column);

        _modeButton = new Button
        {
            // Половина ячейки: кнопка служебная и не должна спорить с составом очереди
            CustomMinimumSize = new Vector2(CellSize.X * 0.6f, CellSize.Y),
            ClipText = true,
        };
        _modeButton.AddThemeFontSizeOverride("font_size", 12);
        _modeButton.Pressed += ToggleInfinite;
        column.AddChild(_modeButton);

        string tip = "Режим производства\nПовтор — очередь идёт по кругу\n" +
                     "Один проход — слот снимается после выпуска";
        _modeButton.MouseEntered += () => ShowTip(_modeButton, tip);
        _modeButton.MouseExited += () =>
        {
            if (_tipOwner == _modeButton)
                HideTip();
        };

        var title = new Label
        {
            Text = "РЕЖИМ",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 12);
        title.AddThemeColorOverride("font_color", TitleColor);
        column.AddChild(title);

        UpdateModeButton();
        return frame;
    }

    /// <summary>
    /// Переключить режим у всех выделенных заводов разом. Состояние берётся по «все
    /// повторяют»: пока хоть один идёт в один проход, нажатие включает повтор у всех,
    /// и разнобой при групповом выделении сходится за одно нажатие.
    /// </summary>
    private void ToggleInfinite()
    {
        bool infinite = _plants.Count > 0 && _plants.All(plant => plant.Infinite);

        foreach (var plant in _plants)
            plant.Infinite = !infinite;

        UpdateModeButton();
    }

    private void UpdateModeButton()
    {
        if (_modeButton == null)
            return;

        bool infinite = _plants.Count > 0 && _plants.All(plant => plant.Infinite);
        _modeButton.Text = infinite ? "↻ повтор" : "→ один";
    }

    private Control BuildRow(BuildbarLayout.Row row)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 4);

        foreach (var buildableId in row.Cells)
        {
            // Пустая ячейка держит место: без распорки соседи съехали бы влево
            // и позиции разошлись бы с другими панелями
            if (buildableId == null)
            {
                line.AddChild(new Control { CustomMinimumSize = CellSize });
                continue;
            }

            var def = Content.Catalog.Unit(buildableId);

            if (def == null)
            {
                GD.PushWarning($"[Buildbar] неизвестная постройка в панели: {buildableId}");
                line.AddChild(new Control { CustomMinimumSize = CellSize });
                continue;
            }

            line.AddChild(BuildButton(def));
        }

        return line;
    }

    private Button BuildButton(UnitDefinition def)
    {
        var button = new Button
        {
            Text = ButtonCaption(def),
            CustomMinimumSize = CellSize,
            ClipText = true,
        };

        button.AddThemeFontSizeOverride("font_size", 12);

        if (_kind == BuildbarKind.Plant)
        {
            // Не Pressed, а GuiInput: сигнал нажатия приходит только от левой кнопки,
            // а очередь правится обеими — левой в хвост, правой из хвоста
            button.GuiInput += @event => OnPlantButtonInput(button, def, @event);
            _counted.Add((button, def));
        }
        else
        {
            button.Pressed += () => GameManager.I?.Command?.BeginBuild(def);
        }

        string tip = TipText(def);
        button.MouseEntered += () => ShowTip(button, tip);
        button.MouseExited += () =>
        {
            if (_tipOwner == button)
                HideTip();
        };

        return button;
    }

    private void OnPlantButtonInput(Button button, UnitDefinition def, InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouse)
            return;

        int count = ModifierCount();

        if (mouse.ButtonIndex == MouseButton.Left)
        {
            foreach (var plant in _plants)
                plant.Enqueue(def.Id, count);

            button.AcceptEvent();
            RefreshPlantCounts();
            return;
        }

        if (mouse.ButtonIndex == MouseButton.Right)
        {
            foreach (var plant in _plants)
                plant.Dequeue(def.Id, count);

            button.AcceptEvent();
            RefreshPlantCounts();
        }
    }

    /// <summary>Shift ×5, Ctrl ×10, Shift+Ctrl ×50; без модификаторов — 1.</summary>
    private static int ModifierCount()
    {
        bool shift = Input.IsKeyPressed(Key.Shift);
        bool ctrl = Input.IsKeyPressed(Key.Ctrl);

        if (shift && ctrl)
            return 50;

        if (ctrl)
            return 10;

        if (shift)
            return 5;

        return 1;
    }

    /// <summary>
    /// Обновить счётчики на кнопках. Зовётся каждый кадр, пока показана plant-панель,
    /// поэтому работа делается только по смене суммарной ревизии очередей: подписи
    /// меняются от щелчков и выпусков, то есть несколько раз в секунду в лучшем случае.
    /// </summary>
    private void RefreshPlantCounts()
    {
        int revision = 0;

        foreach (var plant in _plants)
            revision += plant.Revision;

        if (revision == _queueRevision)
            return;

        _queueRevision = revision;

        foreach (var (button, def) in _counted)
            button.Text = ButtonCaption(def);

        UpdateModeButton();
    }

    private string ButtonCaption(UnitDefinition def)
    {
        if (_kind != BuildbarKind.Plant || _plants.Count == 0)
            return def.DisplayName;

        int total = 0;
        foreach (var plant in _plants)
            total += plant.CountOf(def.Id);

        return total > 0 ? $"{def.DisplayName} ×{total}" : def.DisplayName;
    }

    private string TipText(UnitDefinition def)
    {
        // Энергоцена инструмента строителя в подсказку не входит: она зависит от того,
        // кто строит. Постоянный расход готовой постройки (портал) показываем отдельно.
        string text = $"{def.DisplayName}\n{def.TotalWork:0} метала";

        float drain = def.Conversion?.EnergyDrain ?? 0f;
        if (drain > 0f)
            text += $"\n{drain:0.#} энергии/с";

        if (_kind == BuildbarKind.Plant)
            text += "\nЛКМ — в очередь, ПКМ — убрать\nShift ×5, Ctrl ×10, Shift+Ctrl ×50";

        return text;
    }

    private void ShowTip(Button owner, string text)
    {
        _tipOwner = owner;
        _tipLabel.Text = text;
        _tip.Visible = true;
        // Размер подписи ещё не пересчитан в этом кадре — откладываем постановку
        // на _Process, а пока хотя бы показываем панель
        PlaceTip(owner);
    }

    private void HideTip()
    {
        _tipOwner = null;
        _tip.Visible = false;
    }

    /// <summary>
    /// Ставит подсказку над верхней кромкой кнопки и выравнивает по её левому краю.
    /// Не следует за курсором: игрок читает подпись у ячейки, а не у мыши.
    /// </summary>
    private void PlaceTip(Button owner)
    {
        if (!Alive.Is(owner) || !_tip.Visible)
            return;

        _tip.ResetSize();
        var rect = owner.GetGlobalRect();
        var tipSize = _tip.GetCombinedMinimumSize();

        // Если минимум ещё нулевой — берём текущий размер после ResetSize
        if (tipSize == Vector2.Zero)
            tipSize = _tip.Size;

        float x = rect.Position.X;
        float y = rect.Position.Y - tipSize.Y - 6f;

        // Не уезжаем за левый/правый край окна
        var view = _frame.Size;
        x = Mathf.Clamp(x, 4f, Mathf.Max(4f, view.X - tipSize.X - 4f));
        y = Mathf.Max(4f, y);

        _tip.GlobalPosition = new Vector2(x, y);
    }
}
