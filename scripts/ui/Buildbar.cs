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
/// Что откуда берётся: набор панелей — от выделенных строителей, раскладка —
/// от BuildbarLayout, содержимое ячеек — из справочника построек.
/// </summary>
public partial class Buildbar : CanvasLayer
{
    /// <summary>Размер ячейки. Один на все кнопки — иначе сетка поплывёт.</summary>
    private static readonly Vector2 CellSize = new(118, 30);

    private static readonly Color TitleColor = new(0.45f, 0.85f, 0.95f);

    private Control _frame;
    private HBoxContainer _sections;

    /// <summary>
    /// Отпечаток выделения: идентификаторы панелей выделенных строителей. Сетку
    /// пересобираем только когда он сменился — иначе кнопки перестраивались бы каждый
    /// кадр, теряя наведение и нажатие.
    /// </summary>
    private string _key = "";

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
        margin.AddThemeConstantOverride("margin_bottom", 18);
        column.AddChild(margin);

        _sections = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _sections.AddThemeConstantOverride("separation", 10);
        margin.AddChild(_sections);
    }

    public override void _Process(double delta)
    {
        var bars = BarsOf(GameManager.I?.Command);
        string key = string.Join('|', bars.Select(bar => bar.Id).OrderBy(id => id));

        if (key == _key)
            return;

        _key = key;
        Rebuild(bars);
    }

    /// <summary>
    /// Панели выделенных строителей. Тот, кто строить не умеет, панели не имеет вовсе,
    /// поэтому отдельной проверки «есть ли среди выделенных строитель» не нужно:
    /// пустой список и есть ответ.
    /// </summary>
    private static List<BuildbarDef> BarsOf(CommandSystem command)
    {
        var bars = new List<BuildbarDef>();

        if (command == null)
            return bars;

        foreach (var actor in command.Selected)
        {
            if (actor is not Unit { Def: { CanBuild: true } def })
                continue;

            var bar = Content.Catalog.Buildbar(def.Buildbar);

            if (bar != null && !bars.Contains(bar))
                bars.Add(bar);
        }

        return bars;
    }

    private void Rebuild(List<BuildbarDef> bars)
    {
        foreach (var child in _sections.GetChildren())
            child.QueueFree();

        if (bars.Count == 0)
        {
            _frame.Visible = false;
            return;
        }

        _frame.Visible = true;

        foreach (var section in BuildbarLayout.Merge(bars).Sections)
            _sections.AddChild(BuildSection(section));
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

    private static Control BuildRow(BuildbarLayout.Row row)
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

            var def = Content.Catalog.Buildable(buildableId);

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

    private static Button BuildButton(BuildableDef def)
    {
        // Энергоцены у постройки нет: энергию тратит инструмент строителя, а не здание
        var button = new Button
        {
            Text = def.DisplayName,
            CustomMinimumSize = CellSize,
            ClipText = true,
            TooltipText = $"{def.DisplayName}\n{def.CostMetal:0} метала",
        };

        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => GameManager.I?.Command?.BeginBuild(def);

        return button;
    }
}
