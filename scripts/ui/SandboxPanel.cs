using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Панель песочницы: перечень всего, что есть в справочнике, с мгновенной расстановкой
/// выбранного вида по миру.
///
/// ЗАЧЕМ ОНА ЕСТЬ. Проверить вид — силуэт, габарит, поведение в бою, плотность строя —
/// значит сперва его получить, а обычный путь к этому долог: накопить метал, поставить
/// завод, дождаться выпуска. Для противника такого пути нет вовсе: враги приходят волной,
/// и увидеть отдельный вид рядом со своим юнитом иначе невозможно. Панель этот путь
/// обходит и никакой другой роли не несёт — ни цены, ни стройки, ни приказа здесь нет.
///
/// ЧТО ДЕЛАЕТ ВЫБОР. Ровно то же, что выбор постройки в <see cref="Buildbar"/>: включается
/// режим постановки, под курсором идёт призрак, протаскивание задаёт угол и раскладку,
/// Shift держит серию, Escape или правая кнопка отменяют. Разница только в том, что
/// по отпусканию кнопки в мир входит готовая сущность, а не план застройки, —
/// см. <c>CommandSystem.BeginSandbox</c>.
///
/// РАЗДЕЛЫ РАЗВЕДЕНЫ ПО СТОРОНАМ. Союзные виды выставляются за игрока, вражеские —
/// за противника, и сторона выведена из раздела, а не выбирается отдельно: вид противника
/// за игрока не воюет, у него нет ни панели приказов, ни производства.
///
/// ПОСТРОЙКИ СТОЯТ В СОЮЗНОМ РАЗДЕЛЕ И ТОЛЬКО В НЁМ. Постройка в этой игре принадлежит
/// игроку всегда (<c>Building.Faction</c>), поэтому вражеский завод оказался бы своим,
/// и предлагать его во вражеском разделе значило бы обещать несуществующее.
///
/// Коммандер в перечень не входит: <see cref="Commander"/> при появлении записывает себя
/// в <c>GameManager.Commander</c>, и второй экземпляр сменил бы того, чья гибель означает
/// поражение. Ставить сущность, молча меняющую условие проигрыша, панель не должна.
///
/// Показывается по F2 вместо панели отладки — см. <see cref="ToolPanel"/>.
/// </summary>
public partial class SandboxPanel : ToolPanel
{
    private static readonly Color Heading = new(0.65f, 0.8f, 1f);
    private static readonly Color Numbers = new(0.8f, 0.85f, 0.9f);

    /// <summary>Цвет заголовка вражеского раздела: сторона обязана читаться до чтения подписи.</summary>
    private static readonly Color EnemyInk = new(1f, 0.62f, 0.5f);

    /// <summary>Ширина панели. Та же, что у панели отладки: они занимают одно место экрана.</summary>
    private const int PanelWidth = 300;

    /// <summary>
    /// Сколько ячеек стоит в ряду. Перечень видов длинен, и в один столбец раздел занимал
    /// экран целиком: чтобы дойти до вражеских видов, приходилось прокручивать союзные.
    /// Два столбца укорачивают раздел вдвое; на три названия уже не помещаются.
    /// </summary>
    private const int Columns = 2;

    /// <summary>Промежуток между столбцами и рядами сетки, пикселей.</summary>
    private const int CellGap = 4;

    /// <summary>Высота ячейки вида и сторона квадрата под изображение внутри неё.</summary>
    private const int RowHeight = 26;

    private const float IconSide = 18f;

    /// <summary>
    /// Ширина ячейки. Выводится из ширины панели, а не задаётся числом: иначе сетка
    /// разъезжалась бы с панелью при всякой правке её ширины.
    /// </summary>
    private const float CellWidth =
        (PanelWidth - 16 - CellGap * (Columns - 1)) / (float)Columns;

    private Label _chosen;

    protected override string ToggleAction => InputActions.SandboxToggle;

    public override void _Process(double delta)
    {
        if (!Shown || _chosen == null)
            return;

        var pending = GameManager.I?.Command?.Pending;

        _chosen.Text = pending == null
            ? "ничего не выбрано"
            : $"выбрано: {pending.DisplayName}";
    }

    /// <summary>
    /// Закрытие панели снимает выбранный вид. Иначе выбор пережил бы панель: щелчок по миру
    /// ставил бы юнита, а панели, объясняющей происходящее, на экране уже не было бы.
    /// Обычный выбор постройки при этом не трогается — его сделала не эта панель.
    /// </summary>
    protected override void OnHidden() => GameManager.I?.Command?.CancelSandbox();

    // ── разметка ──────────────────────────────────────────────────────────────────

    protected override void Build(Control frame)
    {
        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Отступы те же, что у панели отладки: нижний левый угол занят панелью выделения
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 56);
        margin.AddThemeConstantOverride("margin_bottom", 140);
        row.AddChild(margin);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        margin.AddChild(scroll);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        scroll.AddChild(panel);

        var stack = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        stack.AddThemeConstantOverride("separation", 4);
        panel.AddChild(stack);

        Section(stack, "Песочница", Heading,
            "Выбранный вид ставится в мир сразу и целиком: ни метала, ни каркаса, ни " +
            "строителя не требуется. Щелчок ставит один, протаскивание задаёт угол и " +
            "раскладку, Shift держит серию, Alt меняет раскладку, Escape или правая " +
            "кнопка снимают выбор.");

        _chosen = Readout(stack, "Что выбрано в панели прямо сейчас.");

        Sections(stack);
    }

    /// <summary>
    /// Разделы по сторонам. Внутри союзного виды ещё раз разложены по роду: подвижных
    /// и построек десятки, и сплошной перечень пришлось бы читать целиком ради одной ячейки.
    /// </summary>
    private void Sections(Node stack)
    {
        var units = Content.Catalog.Units
            .Where(definition => definition.Class != UnitClass.Commander)
            .OrderBy(definition => definition.DisplayName, System.StringComparer.CurrentCulture)
            .ToList();

        Section(stack, "Союзные", Heading,
            "Выставляются за сторону игрока. Постройки стоят здесь и только здесь: " +
            "постройка в этой игре принадлежит игроку всегда.");

        Group(stack, "боты и машины", Faction.Player,
            units.Where(definition =>
                definition.Class != UnitClass.Enemy && !definition.IsStructure));

        Group(stack, "постройки", Faction.Player,
            units.Where(definition => definition.IsStructure));

        Section(stack, "Вражеские", EnemyInk,
            "Выставляются за сторону противника: они сразу воюют против игрока и приказов " +
            "не принимают.");

        Group(stack, "виды противника", Faction.Hostile,
            units.Where(definition => definition.Class == UnitClass.Enemy));
    }

    private void Group(Node stack, string title, Faction faction,
        IEnumerable<UnitDefinition> definitions)
    {
        var list = definitions.ToList();

        if (list.Count == 0)
            return;

        var label = new Label { Text = title };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", Numbers);
        stack.AddChild(label);

        // Сетка, а не столбец: ячейки заполняются слева направо в порядке перечня,
        // поэтому алфавитный порядок читается построчно
        var grid = new GridContainer { Columns = Columns };
        grid.AddThemeConstantOverride("h_separation", CellGap);
        grid.AddThemeConstantOverride("v_separation", CellGap);
        stack.AddChild(grid);

        foreach (var definition in list)
            grid.AddChild(Entry(definition, faction));
    }

    /// <summary>
    /// Ячейка вида: изображение силуэта и название. Собрана отдельными узлами, а не текстом
    /// кнопки, — по той же причине, что и в <see cref="Buildbar"/>: собственный текст кнопки
    /// лёг бы под ними вторым слоем.
    /// </summary>
    private Button Entry(UnitDefinition definition, Faction faction)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(CellWidth, RowHeight),
            ClipText = true,
        };

        button.Pressed += () => GameManager.I?.Command?.BeginSandbox(definition, faction);

        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 4);
        button.AddChild(line);
        line.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        line.OffsetLeft = 3f;
        line.OffsetRight = -3f;

        line.AddChild(new UnitIcon
        {
            Definition = definition,
            CustomMinimumSize = new Vector2(IconSide, IconSide),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });

        var name = new Label
        {
            Text = definition.DisplayName,
            ClipText = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Кегль меньше, чем в строительной панели: в узкой ячейке двенадцатый обрезал бы
        // название на середине, а полное имя есть в подсказке, но читать его придётся
        // наведением — то есть по одному виду за раз
        name.AddThemeFontSizeOverride("font_size", 11);
        line.AddChild(name);

        button.TooltipText = Tip(definition, faction);
        return button;
    }

    /// <summary>
    /// Подсказка ячейки: чем вид является и по каким числам его узнают. Идентификатор
    /// показан затем, что правки ведутся в .toml, а найти файл по названию на русском
    /// нельзя — имена файлов латинские.
    /// </summary>
    private static string Tip(UnitDefinition definition, Faction faction)
    {
        string side = faction == Faction.Hostile ? "противник" : "игрок";
        string text = $"{definition.DisplayName} ({definition.Id})\n" +
                      $"сторона: {side}, класс: {definition.Class}\n" +
                      $"прочность {definition.MaxHealth:0}";

        if (definition.Occupies)
            text += $", габарит {definition.Width}×{definition.Height} клеток";

        if (definition.IsMobile)
            text += $"\nскорость {definition.Speed:0.##}, обзор {definition.VisionRange:0.#}";

        return text;
    }

    private static void Section(Node parent, string title, Color ink, string tooltip)
    {
        if (parent.GetChildCount() > 0)
            parent.AddChild(new HSeparator());

        var label = new Label { Text = title };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", ink);
        parent.AddChild(label);

        Explain(label, tooltip);
    }

    private static Label Readout(Node parent, string tooltip)
    {
        var label = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", Numbers);
        parent.AddChild(label);

        Explain(label, tooltip);
        return label;
    }

    /// <summary>
    /// Пояснение при наведении. Надписи по умолчанию мышь не ловят, поэтому перехват
    /// включается явно — то же правило, что в <see cref="DebugPanel"/>.
    /// </summary>
    private static void Explain(Control control, string tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        control.TooltipText = Wrap(tooltip, 64);

        if (control.MouseFilter == Control.MouseFilterEnum.Ignore)
            control.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    /// <summary>Разложить подсказку по строкам: встроенная подсказка движка переносов не делает.</summary>
    private static string Wrap(string text, int width)
    {
        var lines = new System.Text.StringBuilder(text.Length + 16);
        int since = 0;

        foreach (string word in text.Split(' '))
        {
            if (since > 0 && since + word.Length + 1 > width)
            {
                lines.Append('\n');
                since = 0;
            }
            else if (since > 0)
            {
                lines.Append(' ');
                since++;
            }

            lines.Append(word);
            since += word.Length;
        }

        return lines.ToString();
    }
}
