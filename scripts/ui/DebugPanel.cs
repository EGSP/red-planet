using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Панель отладки у левого края: признаки отрисовки и числа, по которым принимаются решения.
///
/// ЗАЧЕМ ОНА ЕСТЬ. Настройка локального обхода ведётся подбором коэффициентов, а разбор
/// странного пути — сопоставлением растра, клиренса и ломаной. И то и другое без
/// визуализации превращается в угадывание. Поэтому панель делалась вместе с растром,
/// а не после него.
///
/// ЧТО СЮДА ПЕРЕЕХАЛО. Счёт боя и подсказки управления раньше висели отдельной панелью
/// в левом верхнем углу и занимали место постоянно, хотя нужны редко. За HUD осталась
/// только кнопка паузы, и та ушла в правый нижний угол.
///
/// Содержимое разложено по вертикальным вкладкам-доменам (<c>nav</c>, <c>boi</c>,
/// <c>vis</c>, <c>wav</c>, <c>dia</c>), чтобы растущие блоки не сдвигали чужие секции.
///
/// Панель показывается по F3 и на симуляцию не влияет: она читает состояние и правит
/// только <see cref="DebugFlags"/>.
/// </summary>
public partial class DebugPanel : CanvasLayer
{
    private static readonly Color Heading = new(0.65f, 0.8f, 1f);
    private static readonly Color Numbers = new(0.8f, 0.85f, 0.9f);
    private static readonly Color IconInk = new(0.75f, 0.88f, 1f);

    /// <summary>Ширина страницы содержимого. Колонка вкладок добавляется отдельно.</summary>
    private const int PanelWidth = 300;

    /// <summary>Ширина колонки с кодами вкладок.</summary>
    private const int TabWidth = 52;

    /// <summary>Во сколько знаков укладывается строка подсказки.</summary>
    private const int TooltipWidth = 64;

    private Control _frame;
    private Control[] _pages;
    private Label _combat;
    private Label _navigation;
    private Label _paths;
    private Label _waves;
    private Label _fog;

    public override void _Ready()
    {
        Build();
        _frame.Visible = false;
    }

    /// <summary>
    /// F3 ловим до систем: панель обязана открываться и на паузе, когда ветка систем
    /// обработку не получает вовсе.
    /// </summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
            return;

        _frame.Visible = !_frame.Visible;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (!_frame.Visible)
            return;

        Refresh();
    }

    // ── разметка ──────────────────────────────────────────────────────────────────

    private void Build()
    {
        _frame = new UiFrame();
        AddChild(_frame);

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _frame.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Прижимаем к верху: нижний левый угол занят панелью выделения, и накрывать её
        // отладкой значило бы прятать то, ради чего отладку и включили
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 56);
        margin.AddThemeConstantOverride("margin_bottom", 140);
        row.AddChild(margin);

        var shell = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        shell.AddThemeConstantOverride("separation", 4);
        margin.AddChild(shell);

        var tabs = new VBoxContainer { CustomMinimumSize = new Vector2(TabWidth, 0) };
        tabs.AddThemeConstantOverride("separation", 2);
        shell.AddChild(tabs);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        shell.AddChild(scroll);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        scroll.AddChild(panel);

        // Скрытые страницы в VBox не участвуют в расчёте высоты, поэтому ScrollContainer
        // подстраивается под видимую вкладку, а не под сумму всех.
        var stack = new VBoxContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        panel.AddChild(stack);

        _pages = new Control[5];
        _pages[0] = Page(stack);
        _pages[1] = Page(stack);
        _pages[2] = Page(stack);
        _pages[3] = Page(stack);
        _pages[4] = Page(stack);

        FillNav(_pages[0]);
        FillBoi(_pages[1]);
        FillVis(_pages[2]);
        FillWav(_pages[3]);
        FillDia(_pages[4]);

        var group = new ButtonGroup();
        Tab(tabs, group, 0, "nav", "Навигация", IconNav());
        Tab(tabs, group, 1, "boi", "Локальный обход", IconBoi());
        Tab(tabs, group, 2, "vis", "Зрение", IconVis());
        Tab(tabs, group, 3, "wav", "Волны", IconWav());
        Tab(tabs, group, 4, "dia", "Диагностика", IconDia());

        ShowPage(0);
    }

    private static VBoxContainer Page(Node stack)
    {
        var box = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(PanelWidth, 0),
        };
        box.AddThemeConstantOverride("separation", 4);
        stack.AddChild(box);
        return box;
    }

    private void Tab(Node parent, ButtonGroup group, int index, string code, string title,
        Texture2D icon)
    {
        var button = new Button
        {
            Text = code,
            Icon = icon,
            ToggleMode = true,
            ButtonGroup = group,
            ButtonPressed = index == 0,
            CustomMinimumSize = new Vector2(TabWidth, 40),
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Top,
            ExpandIcon = false,
        };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.TooltipText = title;
        button.Toggled += on =>
        {
            if (on)
                ShowPage(index);
        };
        parent.AddChild(button);
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visible = i == index;
    }

    private void FillNav(Node box)
    {
        Section(box, "Навигация",
            "Мир разбит на растр из ячеек по 16 px. По нему ищется путь и решается, " +
            "где юнит помещается. Растр выводится из прямоугольников зданий и " +
            "пересобирается сам, когда те меняются.");

        Check(box, "непроходимость",
            "Красным — ячейки, задетые прямоугольником здания. Растеризация грубая: " +
            "ячейка, задетая хотя бы краем, закрашивается целиком. Проверять здесь стоит, " +
            "что растр совпадает с габаритом здания и исчезает вместе с ним.",
            () => DebugFlags.NavBlocked, on => DebugFlags.NavBlocked = on);

        Check(box, "клиренс",
            "Расстояние от ячейки до ближайшего препятствия. Именно оно решает, " +
            "пройдёт ли юнит: коридор годится, если клиренс не меньше радиуса юнита. " +
            "Оранжевым — ячейки, где типовой юнит не помещается; синим — где есть место, " +
            "и чем ярче, тем просторнее. Здесь видно, почему юнит не пошёл в щель.",
            () => DebugFlags.NavClearance, on => DebugFlags.NavClearance = on);

        Check(box, "связные области",
            "Куски карты, между которыми есть путь, окрашены в один цвет. Разные цвета " +
            "означают, что пути между ними нет вовсе, и такой приказ отклоняется до " +
            "запуска поиска. Включайте, когда подозреваете, что застройка замуровала область.",
            () => DebugFlags.NavComponents, on => DebugFlags.NavComponents = on);

        Check(box, "габариты и зазоры",
            "Синим — прямоугольник, который здание занимает на самом деле. Жёлтым — " +
            "обязательный зазор вокруг него: поставить второе здание так, чтобы оно " +
            "залезло в жёлтую рамку соседа, нельзя. Зазор не гарантирует прохода — " +
            "он лишь не даёт зданиям слипнуться в растре.",
            () => DebugFlags.Footprints, on => DebugFlags.Footprints = on);

        Section(box, "Пути",
            "Путь — ломаная, посчитанная A* по растру и сглаженная протягиванием прямой. " +
            "Юнит идёт от точки к точке, а не по прямой к цели.");

        Check(box, "пути выделенных",
            "Ломаная выделенных юнитов. Голубым — предстоящая часть, серым — пройденная, " +
            "красным — путь, который не удалось найти. Кружок на конце — цель. " +
            "Если цель была внутри здания, кружок стоит у его края: туда юнит и идёт.",
            () => DebugFlags.Paths, on => DebugFlags.Paths = on);

        Check(box, "пути всех",
            "То же самое для всех подвижных сразу, включая врагов. Нужно, когда странно " +
            "ведёт себя не выделенный юнит, а посторонний: выделять его в этот момент " +
            "значит спугнуть ситуацию.",
            () => DebugFlags.PathsAll, on => DebugFlags.PathsAll = on);

        Check(box, "раскрытые узлы A*",
            "Ячейки, которые перебрал последний поиск. Показывает, куда потрачен бюджет " +
            "узлов: широкое пятно означает, что поиск блуждал, узкая полоса — что шёл " +
            "прямо. Заполнение стоит памяти, поэтому ведётся только при включённом признаке.",
            () => DebugFlags.PathsExpanded, on => DebugFlags.PathsExpanded = on);

        Section(box, "Растр", "Состояние навигационной карты.");
        _navigation = Readout(box,
            "«Препятствий» — сколько зданий и каркасов занимают место. «Ревизия» растёт " +
            "при каждой пересборке растра: если она растёт без остановки, значит что-то " +
            "меняет карту каждый кадр. «Пересборка» — во что эта работа обходится.");

        Section(box, "Поиск пути", "Нагрузка на систему путей за прошедший кадр.");
        _paths = Readout(box,
            "«Готовых» — сколько запросов обслужено кешем без счёта. «В очереди» — сколько " +
            "поисков отложено на следующий кадр из-за бюджета; устойчиво ненулевая очередь " +
            "означает, что бюджета не хватает. «Узлов» — насколько тяжёлым был поиск. " +
            "«Разных целей» — по этому числу вместе с числом движущихся решается, " +
            "пора ли вводить общее векторное поле вместо отдельных путей.");
    }

    private void FillBoi(Node box)
    {
        Section(box, "Локальный обход",
            "Поверх пути работают силы boids: они разводят юнитов между собой и обводят " +
            "вокруг тех, кто стоит на дороге. За обход препятствий отвечает не этот слой, " +
            "а путь.");

        Check(box, "векторы сил",
            "Стрелки от юнита, длина по величине силы. Зелёная — стремление к следующей " +
            "точке пути, оранжевая — обход соседа стороной, синяя — выравнивание скорости " +
            "с группой, белая — итоговая скорость. Если белая расходится с зелёной, " +
            "юнита уводит именно локальный слой.",
            () => DebugFlags.BoidForces, on => DebugFlags.BoidForces = on);

        Check(box, "радиусы",
            "Жёлтая окружность — физический корпус: внутрь него не пускают ни здания, " +
            "ни другие юниты. Голубая — радиус чутья: только соседей внутри него юнит " +
            "учитывает при обходе.",
            () => DebugFlags.BoidRadii, on => DebugFlags.BoidRadii = on);

        Check(box, "соседи",
            "Линии к тем, кого выделенный юнит сейчас видит. Помогает понять, почему " +
            "он свернул: обход считается только по этим соседям.",
            () => DebugFlags.BoidNeighbours, on => DebugFlags.BoidNeighbours = on);

        Check(box, "ячейки поиска соседей",
            "Служебная разбивка мира на ячейки по 128 px, по которой юниты ищут соседей. " +
            "Нужна, чтобы не сравнивать каждого с каждым. К игровым правилам отношения " +
            "не имеет: показывает только непустые ячейки и служит проверкой, " +
            "что раскладка не разъехалась с настоящими положениями.",
            () => DebugFlags.BoidCells, on => DebugFlags.BoidCells = on);
    }

    private void FillVis(Node box)
    {
        Section(box, "Туман войны",
            "Поля зрения стороны игрока сведены в растр по ячейкам в 16 px, и по нему " +
            "рисуются заливка закрытой части карты и обводка по общей границе видимого. " +
            "На симуляцию это не влияет: цели выбираются и приказы отдаются по всей карте.");

        Check(box, "заливка", "Закрашивать непросматриваемую часть карты.",
            () => Fog()?.Fog ?? false, on => Set(settings => settings.Fog = on));

        Check(box, "обводка",
            "Линия по общей границе поля зрения. Толщина задана в пикселях экрана, " +
            "поэтому при отдалении камеры линия не утолщается.",
            () => Fog()?.Outline ?? false, on => Set(settings => settings.Outline = on));

        Check(box, "скрывать противника",
            "Юниты и снаряды противника вне поля зрения не рисуются. Стрелять по ним " +
            "и выделять их по-прежнему можно: скрытие касается только отрисовки.",
            () => Fog()?.HideEnemies ?? false, on => Set(settings => settings.HideEnemies = on));

        Colour(box, "цвет заливки", "Цвет и плотность закрытой части карты.",
            () => Fog()?.FogColor ?? Colors.Black, value => Set(settings => settings.FogColor = value));

        Colour(box, "цвет обводки", "Цвет линии границы.",
            () => Fog()?.OutlineColor ?? Colors.Yellow,
            value => Set(settings => settings.OutlineColor = value));

        _fog = Readout(box,
            "«Источников» — сколько зрячих сущностей участвовало в последней пересборке. " +
            "«Скрыто» — сколько сущностей противника сейчас не рисуется. " +
            "«Пересборка» — во что обходится растр: она идёт по таймеру, а не каждый кадр.");
    }

    private void FillWav(Node box)
    {
        Section(box, "Волны", "Что подсистема волн отобрала и что из этого вышло на карту.");
        _waves = Readout(box,
            "Первая строка — сколько осталось до ближайшей волны. Дальше история партии, " +
            "от новых к старым: время, волна, показатель террора на миг отбора, " +
            "бюджет и потраченная его часть, состав по видам, направление первого очага " +
            "и назначенный отдых. Расхождение бюджета с потраченным означает, что остаток " +
            "было некому занять: самый дешёвый допустимый вид оказался дороже него. " +
            "«+N рядов» — состав не уместился в заданную глубину формы.");
    }

    private void FillDia(Node box)
    {
        Section(box, "Счёт боя", "То же, что раньше висело в левом верхнем углу.");
        _combat = Readout(box, "Пришло, уничтожено, потеряно, и сколько накопил коммандер.");

        Section(box, "Управление", null);

        var hints = new Label
        {
            Text = "ЛКМ — выделить или рамка, ПКМ — приказ по цели\n" +
                   "Shift — дописать в очередь, WASD и колесо — камера\n" +
                   "C — очереди всех своих, F3 — эта панель",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hints.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(hints);
    }

    // ── иконки вкладок ────────────────────────────────────────────────────────────

    /// <summary>Сетка 3×3 — навигационный растр.</summary>
    private static Texture2D IconNav() => Paint(image =>
    {
        for (int i = 3; i <= 12; i++)
        {
            Dot(image, i, 3);
            Dot(image, i, 7);
            Dot(image, i, 12);
            Dot(image, 3, i);
            Dot(image, 7, i);
            Dot(image, 12, i);
        }
    });

    /// <summary>Стрелка вправо-вверх — локальная сила обхода.</summary>
    private static Texture2D IconBoi() => Paint(image =>
    {
        for (int i = 3; i <= 11; i++)
            Dot(image, i, 12 - (i - 3) / 2);

        Dot(image, 11, 3);
        Dot(image, 12, 3);
        Dot(image, 12, 4);
        Dot(image, 10, 3);
        Dot(image, 11, 4);
        Dot(image, 12, 5);
    });

    /// <summary>Круг с точкой — поле зрения.</summary>
    private static Texture2D IconVis() => Paint(image =>
    {
        int[] ring =
        {
            5, 2, 6, 2, 7, 2, 8, 2, 9, 2, 10, 2,
            4, 3, 11, 3,
            3, 4, 12, 4,
            2, 5, 13, 5,
            2, 6, 13, 6,
            2, 7, 13, 7,
            2, 8, 13, 8,
            2, 9, 13, 9,
            2, 10, 13, 10,
            3, 11, 12, 11,
            4, 12, 11, 12,
            5, 13, 6, 13, 7, 13, 8, 13, 9, 13, 10, 13,
        };

        for (int i = 0; i < ring.Length; i += 2)
            Dot(image, ring[i], ring[i + 1]);

        Dot(image, 7, 7);
        Dot(image, 8, 7);
        Dot(image, 7, 8);
        Dot(image, 8, 8);
    });

    /// <summary>Зигзаг — волна.</summary>
    private static Texture2D IconWav() => Paint(image =>
    {
        int[] path =
        {
            2, 10, 3, 9, 4, 8, 5, 7, 6, 6, 7, 5, 8, 6, 9, 7, 10, 8, 11, 7, 12, 6, 13, 5,
        };

        for (int i = 0; i < path.Length; i += 2)
        {
            Dot(image, path[i], path[i + 1]);
            Dot(image, path[i], path[i + 1] + 1);
        }
    });

    /// <summary>Три столбика разной высоты — счётчики диагностики.</summary>
    private static Texture2D IconDia() => Paint(image =>
    {
        for (int y = 9; y <= 13; y++)
        {
            Dot(image, 3, y);
            Dot(image, 4, y);
        }

        for (int y = 5; y <= 13; y++)
        {
            Dot(image, 7, y);
            Dot(image, 8, y);
        }

        for (int y = 3; y <= 13; y++)
        {
            Dot(image, 11, y);
            Dot(image, 12, y);
        }
    });

    private static ImageTexture Paint(Action<Image> draw)
    {
        var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);
        draw(image);
        return ImageTexture.CreateFromImage(image);
    }

    private static void Dot(Image image, int x, int y) => image.SetPixel(x, y, IconInk);

    private static void Section(Node parent, string title, string tooltip)
    {
        if (parent.GetChildCount() > 0)
            parent.AddChild(new HSeparator());

        var label = new Label { Text = title };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", Heading);
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
    /// Флажок над полем набора признаков. Чтение идёт через функцию, а не разовым снимком:
    /// признаки статические и переживают пересборку панели вместе с сессией.
    /// </summary>
    private static void Check(Node parent, string title, string tooltip,
        Func<bool> read, Action<bool> write)
    {
        var box = new CheckBox { Text = title, ButtonPressed = read() };
        box.AddThemeFontSizeOverride("font_size", 11);
        box.Toggled += on => write(on);
        parent.AddChild(box);

        Explain(box, tooltip);
    }

    /// <summary>Выбор цвета. Альфа правится вместе с цветом: ею задана плотность заливки.</summary>
    private static void Colour(Node parent, string title, string tooltip,
        Func<Color> read, Action<Color> write)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var label = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(label);

        var picker = new ColorPickerButton
        {
            Color = read(),
            EditAlpha = true,
            CustomMinimumSize = new Vector2(64, 20),
        };

        picker.ColorChanged += value => write(value);
        row.AddChild(picker);

        Explain(label, tooltip);
    }

    /// <summary>
    /// Настройки тумана берутся у системы зрения, а не хранятся снимком: ресурс живёт
    /// столько же, сколько сессия, а панель переживает её пересборку.
    /// </summary>
    private static FogSettings Fog() => GameManager.I?.System<VisionSystem>()?.Settings;

    /// <summary>Правка настроек тумана, безопасная к отсутствию системы в сцене.</summary>
    private static void Set(Action<FogSettings> change)
    {
        var settings = Fog();

        if (settings != null)
            change(settings);
    }

    /// <summary>
    /// Пояснение при наведении: что это за механизм, как он работает в игре и что должно
    /// быть видно на экране.
    ///
    /// Надписи по умолчанию мышь не ловят, поэтому подсказка на них не всплывала бы;
    /// приходится включать перехват явно. Панель от этого не начинает воровать щелчки:
    /// они и так не доходят до мира, за это отвечает сам контейнер.
    /// </summary>
    private static void Explain(Control control, string tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        control.TooltipText = Wrap(tooltip, TooltipWidth);

        if (control.MouseFilter == Control.MouseFilterEnum.Ignore)
            control.MouseFilter = Control.MouseFilterEnum.Stop;
    }

    /// <summary>
    /// Разложить текст по строкам вручную. Стандартная подсказка движка переносов не делает
    /// и вытягивается в одну строку через весь экран, а объяснение на три предложения
    /// в такой строке нечитаемо.
    /// </summary>
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

    // ── числа ─────────────────────────────────────────────────────────────────────

    private void Refresh()
    {
        var gm = GameManager.I;

        if (gm == null)
            return;

        var combat = gm.Combat;
        float damage = gm.Commander?.Health.TotalTaken ?? 0f;

        _combat.Text = $"на карте {combat.EnemiesAlive}   уничтожено {combat.EnemiesDestroyed}\n" +
                       $"потеряно {combat.LossesTaken}   урон коммандеру {damage:0}";

        _navigation.Text =
            $"поле {NavGrid.Width}×{NavGrid.Width} по {NavGrid.Cell} px, зазор {Const.BuildMarginPx:0} px\n" +
            $"препятствий {gm.Obstacles.Count}   ревизия {gm.Nav.Revision}\n" +
            $"последняя пересборка {gm.Nav.LastBuildMs:0.00} мс";

        _waves.Text = Waves(gm.System<WaveSystem>());
        _fog.Text = Vision(gm.System<VisionSystem>());

        var pathfinding = gm.System<PathfindingSystem>();
        var movement = gm.System<MovementSystem>();

        if (pathfinding == null)
        {
            _paths.Text = "система поиска пути не в сцене";
            return;
        }

        _paths.Text =
            $"запросов {pathfinding.Requests}, из них готовых {pathfinding.Hits}\n" +
            $"в очереди {pathfinding.Pending}   в кеше {pathfinding.Cached}\n" +
            $"узлов: последний {pathfinding.LastExpanded}, худший {pathfinding.WorstExpanded}\n" +
            $"движется {movement?.Tracked ?? 0}, разных целей {Destinations(gm, pathfinding)}";
    }

    /// <summary>
    /// Обратный отсчёт и история волн, от новых к старым.
    ///
    /// Порядок обратный порядку событий намеренно: панель узкая, длинную историю в ней
    /// приходится прокручивать, а нужна прежде всего последняя волна — та, чьи следствия
    /// сейчас на экране.
    /// </summary>
    private static string Waves(WaveSystem waves)
    {
        if (waves == null)
            return "подсистема волн не в сцене";

        var text = new System.Text.StringBuilder();
        text.Append($"до ближайшей {waves.TimeLeft:0} с");

        var history = waves.History;

        if (history.Count == 0)
        {
            text.Append("\nволн ещё не было");
            return text.ToString();
        }

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var record = history[i];

            text.Append($"\n\n{Clock(record.GameTime)} {record.WaveId}   террор {record.Terror:0.0}\n" +
                        $"бюджет {record.Budget:0.0}, потрачено {record.Spent:0.0}\n" +
                        $"{record.Composition}\n" +
                        $"угол {record.CenterAngleDegrees:0}°, очагов {record.Groups}, " +
                        $"отдых {record.ChillSeconds:0} с");

            if (record.ExtraRows > 0)
                text.Append($", +{record.ExtraRows} рядов");
        }

        return text.ToString();
    }

    /// <summary>Состояние растра видимости: размер, источники и стоимость пересборки.</summary>
    private static string Vision(VisionSystem vision)
    {
        if (vision == null)
            return "система зрения не в сцене";

        var field = vision.Field;

        string rate = vision.Settings.EveryFrame
            ? "каждый кадр"
            : $"{vision.Settings.UpdateHz:0} раз в секунду";

        return $"поле {field.Width}×{field.Width} по {field.Cell} px\n" +
               $"источников {field.Sources}   скрыто {vision.Hidden}\n" +
               $"последняя пересборка {field.LastBuildMs:0.00} мс, {rate}";
    }

    /// <summary>Время партии как «минуты:секунды»: по секундам от начала считать неудобно.</summary>
    private static string Clock(float seconds) =>
        $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";

    /// <summary>
    /// Сколько различных пунктов назначения обслуживается сейчас.
    ///
    /// Число не любопытства ради: отношение «движется к разным целям» и есть критерий,
    /// по которому решается, пора ли вводить flowfield. Порог назван в docs/pathfinding.md.
    /// </summary>
    private static int Destinations(GameManager gm, PathfindingSystem pathfinding)
    {
        var clusters = new List<Vector2>();
        float tolerance = NavGrid.Cell * 2f;

        foreach (var pair in pathfinding.Paths)
        {
            var goal = pair.Value.Goal;
            bool merged = false;

            foreach (var cluster in clusters)
            {
                if (cluster.DistanceTo(goal) > tolerance)
                    continue;

                merged = true;
                break;
            }

            if (!merged)
                clusters.Add(goal);
        }

        return clusters.Count;
    }
}
