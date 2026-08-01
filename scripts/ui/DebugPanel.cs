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
/// Панель показывается по F3 и на симуляцию не влияет: она читает состояние и правит
/// только <see cref="DebugFlags"/>.
/// </summary>
public partial class DebugPanel : CanvasLayer
{
    private static readonly Color Heading = new(0.65f, 0.8f, 1f);
    private static readonly Color Numbers = new(0.8f, 0.85f, 0.9f);

    /// <summary>Ширина панели. Сверху её ограничивает полоса ресурсов, снизу — ничего.</summary>
    private const int PanelWidth = 300;

    /// <summary>Во сколько знаков укладывается строка подсказки.</summary>
    private const int TooltipWidth = 64;

    private Control _frame;
    private Label _combat;
    private Label _navigation;
    private Label _paths;

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

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(column);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        scroll.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

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

        Section(box, "Счёт боя", "То же, что раньше висело в левом верхнем углу.");
        _combat = Readout(box, "Пришло, уничтожено, потеряно, и сколько накопил коммандер.");

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
