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
/// <c>vis</c>, <c>gfx</c>, <c>giz</c>, <c>wav</c>, <c>dia</c>), чтобы растущие блоки
/// не сдвигали чужие секции.
///
/// Панель показывается по F3 и на симуляцию не влияет: она читает состояние, правит
/// <see cref="DebugFlags"/> и управляет записью срезов <see cref="PerformanceCapture"/>.
/// Показ и клавиша достались ей от <see cref="ToolPanel"/>: панель песочницы занимает то же
/// место экрана, и открытой из них бывает не более одной.
/// </summary>
public partial class DebugPanel : ToolPanel
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

    /// <summary>Цвета выделения последнего замера при превышении медианы.</summary>
    private const string WarnInk = "#ffd24a";

    private const string AlertInk = "#ff9a3c";

    private const string AlarmInk = "#ff5a4a";

    private Control[] _pages;
    private Label _combat;
    private Label _navigation;
    private Label _paths;
    private Label _waves;
    private Label _fog;
    private RichTextLabel _profile;
    private Label _traceStatus;
    private Button _traceStart;
    private Button _traceStop;
    private OptionButton _traceInterval;

    /// <summary>
    /// Поколение замеров, по которому уже собран текст. Сравнение с текущим избавляет
    /// от пересборки списка каждый кадр: интервал закрывается четыре раза в секунду.
    /// </summary>
    private int _profileShown = -1;

    protected override string ToggleAction => InputActions.DebugToggle;

    public override void _ExitTree()
    {
        PerformanceCapture.StopIfRecording();
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        if (!Shown)
            return;

        Refresh();
    }

    // ── разметка ──────────────────────────────────────────────────────────────────

    protected override void Build(Control frame)
    {
        // Лента скорости лежит в том же каркасе, но вне колонки вкладок: она принадлежит
        // не какой-то одной из них, а наблюдению за выделенным юнитом вообще, и место
        // у правого края экрана выбрано затем, чтобы не спорить с самой панелью
        frame.AddChild(new SpeedGraph());

        var row = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(row);
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

        _pages = new Control[8];

        for (int i = 0; i < _pages.Length; i++)
            _pages[i] = Page(stack);

        FillNav(_pages[0]);
        FillBoi(_pages[1]);
        FillVis(_pages[2]);
        FillGfx(_pages[3]);
        FillGiz(_pages[4]);
        FillWav(_pages[5]);
        FillDia(_pages[6]);
        FillPrf(_pages[7]);

        var group = new ButtonGroup();
        Tab(tabs, group, 0, "nav", "Навигация", IconNav());
        Tab(tabs, group, 1, "boi", "Локальный обход", IconBoi());
        Tab(tabs, group, 2, "vis", "Зрение", IconVis());
        Tab(tabs, group, 3, "gfx", "Графика", IconGfx());
        Tab(tabs, group, 4, "giz", "Области юнитов", IconGiz());
        Tab(tabs, group, 5, "wav", "Волны", IconWav());
        Tab(tabs, group, 6, "dia", "Диагностика", IconDia());
        Tab(tabs, group, 7, "prf", "Замеры систем", IconPrf());

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

        Check(box, "области тайлов",
            "Из чего собран верхний уровень поиска: каждый тайл 32×32 ячейки разбит " +
            "на области непрерывной проходимости, и каждая окрашена своим цветом. " +
            "Здание, поставленное поперёк тайла, делит его область надвое — по картинке " +
            "видно, где у поиска появляются новые переходы, а где он их потерял.",
            () => DebugFlags.NavRegions, on => DebugFlags.NavRegions = on);

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

        Check(box, "полоса макро-поиска",
            "Чем поиск ограничивал себя, когда считал путь показываемого юнита. Фиолетовые " +
            "кружки — области, внутри которых ему было позволено раскрывать ячейки; " +
            "ломаная между ними — цепочка от старта к цели по графу областей, служащая " +
            "оценкой расстояния. Полоса хранится у самого пути, поэтому показывается для " +
            "каждого выделенного, а не для последнего посчитанного запроса. Пусто означает, " +
            "что поиск шёл по всему растру: цель была видна напрямую либо полоса пути " +
            "не дала. Вместе с «раскрытыми узлами» видно и позволенное, и понадобившееся.",
            () => DebugFlags.PathMacro, on => DebugFlags.PathMacro = on);

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

        Section(box, "Пространственная сетка",
            "Служебная разбивка мира на клетки по 128 px. По ней отвечают на вопрос " +
            "«кто рядом»: локальный обход берёт отсюда соседей, а выбор цели обходит " +
            "клетки кольцами от ближней к дальним вместо перебора всех целей стороны. " +
            "К игровым правилам отношения не имеет.");

        Check(box, "клетки",
            "Показывать занятые клетки. Сплошное пятно вокруг скоплений — норма; " +
            "одиночные клетки вдалеке означают, что кто-то забрёл за пределы поля.",
            () => DebugFlags.SpatialBuckets, on => DebugFlags.SpatialBuckets = on);

        Check(box, "численность в клетке",
            "Число сущностей в углу клетки. Десятки в одной клетке означают, что размер " +
            "клетки для такого боя мелок и обход платит за длинные списки.",
            () => DebugFlags.SpatialCounts, on => DebugFlags.SpatialCounts = on);

        Check(box, "показывать цели, а не подвижных",
            "Раскладок две: подвижные (по ним идёт расталкивание) и уязвимые (по ним " +
            "идёт выбор цели). Постройка попадает во все клетки своего прямоугольника, " +
            "поэтому у целей занятых клеток больше.",
            () => DebugFlags.SpatialTargets, on => DebugFlags.SpatialTargets = on);

        Check(box, "сверять положения с узлами",
            "Положение сущности хранится в управляемой памяти, а в узел движка пишется " +
            "при каждом изменении. Сверка ищет расхождение между ними — оно означает, " +
            "что положение писали мимо Entity. Стоит обращения к движку на сущность " +
            "за кадр, поэтому включать её стоит только при разборе дрожания.",
            () => DebugFlags.EntityAudit, on => DebugFlags.EntityAudit = on);
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

        Slide(box, "скорость догона границы", 0f, 40f, 0.5f,
            "Насколько быстро показываемая граница догоняет собранную. Растр пересобирается " +
            "двадцать раз в секунду, и без сглаживания граница двигалась бы ступенями. " +
            "Десять означает, что за десятую долю секунды проходится около двух третей пути; " +
            "меньшие значения дают вязкое движение, ноль отменяет сглаживание вовсе.",
            () => Fog()?.CatchUp ?? 0f, value => Set(settings => settings.CatchUp = value));

        Slide(box, "пересборок в секунду", 1f, 60f, 1f,
            "Как часто собирается сам растр. Стоимость сборки пропорциональна числу " +
            "источников обзора, поэтому при большой армии это заметная величина; " +
            "плавность же движения границы задаётся не этим числом, а скоростью догона.",
            () => Fog()?.UpdateHz ?? 20f, value => Set(settings => settings.UpdateHz = value));

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

    /// <summary>
    /// Графика мира: подсистемы затенения спрайтов и вида стройки из
    /// <see cref="GraphicsSettings"/>, а следом тень препятствий, которая принадлежит
    /// местности. Вкладка заведена отдельно от зрения потому, что туман есть часть правил
    /// видимости, а всё перечисленное не влияет ни на что, кроме вида.
    ///
    /// Правки идут прямо в ресурсы подсистем, поэтому действуют на всё сразу и переживают
    /// закрытие панели; сохранение файла остаётся за инспектором.
    /// </summary>
    private void FillGfx(Node box)
    {
        FillShading(box);
        FillConstruction(box);
        FillObstacleShade(box);
    }

    /// <summary>Затенение частей модели: тени под корпусом и затемнение по кайме.</summary>
    private void FillShading(Node box)
    {
        Section(box, "Затенение",
            "Три запечённых слоя, выведенных из альфы спрайта части модели. Признаки гасят " +
            "слой у всех сущностей сразу; величины действуют на те узлы затенения, которые " +
            "не назначили собственных.");

        Check(box, "отброшенная тень",
            "Размытый силуэт, заметно отнесённый по направлению света и нарисованный " +
            "под корпусом. Отделяет сущность от поверхности сильнее контактной тени " +
            "и потому нужен там, где корпус поднят над грунтом.",
            () => Ao().CastEnabled, on => Ao().CastEnabled = on);

        Check(box, "контактная тень",
            "Узкая полоса под самым корпусом. Без неё изображение лежит с грунтом в одной " +
            "плоскости и читается как наклейка.",
            () => Ao().ContactEnabled, on => Ao().ContactEnabled = on);

        Check(box, "затемнение по кайме",
            "Полоса потемнения вдоль края внутри силуэта, нарисованная поверх корпуса. " +
            "Упрощённая имитация ambient occlusion: настоящее вычисление потребовало бы " +
            "данных о рельефе, которых у плоского изображения нет. Даёт объём самому корпусу.",
            () => Ao().RimEnabled, on => Ao().RimEnabled = on);

        Colour(box, "цвет затенения",
            "Общий цвет всех слоёв. Чистый чёрный на охристом грунте выглядит провалом, " +
            "поэтому по умолчанию взят слегка холодный тёмный тон.",
            () => Ao().Shade, value => Ao().Shade = value);

        Slide(box, "направление света", 0f, 360f, 1f,
            "Куда уходят тени, градусов от оси вправо по часовой стрелке. Направление " +
            "мировое: у повёрнутой сущности тень идёт в ту же сторону, что и у неповёрнутой.",
            () => Ao().LightAngleDegrees, value => Ao().LightAngleDegrees = value);

        Slide(box, "плотность отброшенной тени", 0f, 1f, 0.01f,
            "Непрозрачность тени, отброшенной корпусом на грунт.",
            () => Ao().CastOpacity, value => Ao().CastOpacity = value);

        Slide(box, "размытие отброшенной тени", 0f, 0.3f, 0.005f,
            "Радиус размытия, доля меньшей стороны текстуры.",
            () => Ao().CastBlur, value => Ao().CastBlur = value);

        Slide(box, "отход отброшенной тени", 0f, 0.5f, 0.005f,
            "Насколько тень отнесена от корпуса, доля меньшей стороны текстуры.",
            () => Ao().CastOffset, value => Ao().CastOffset = value);

        Slide(box, "плотность контактной тени", 0f, 1f, 0.01f,
            "Непрозрачность тени вплотную к корпусу.",
            () => Ao().ContactOpacity, value => Ao().ContactOpacity = value);

        Slide(box, "размытие контактной тени", 0f, 0.3f, 0.005f,
            "Радиус размытия, доля меньшей стороны текстуры. Доля, а не пиксели: рисунки " +
            "имеют разное разрешение, и постоянная в пикселях дала бы у крупного изображения " +
            "вдвое более узкую тень.",
            () => Ao().ContactBlur, value => Ao().ContactBlur = value);

        Slide(box, "отход контактной тени", 0f, 0.3f, 0.005f,
            "Насколько тень смещена от корпуса, доля меньшей стороны текстуры.",
            () => Ao().ContactOffset, value => Ao().ContactOffset = value);

        Slide(box, "плотность каймы", 0f, 1f, 0.01f,
            "Непрозрачность затемнения на самом краю силуэта.",
            () => Ao().RimOpacity, value => Ao().RimOpacity = value);

        Slide(box, "ширина каймы", 0f, 0.3f, 0.005f,
            "Ширина полосы затемнения внутрь от края, доля меньшей стороны текстуры.",
            () => Ao().RimBlur, value => Ao().RimBlur = value);
    }

    /// <summary>Вид каркаса строящейся постройки.</summary>
    private void FillConstruction(Node box)
    {
        Section(box, "Стройка",
            "Каркас показан спрайтом будущей постройки, разделённым по высоте уровнем " +
            "готовности: ниже уровня видна полупрозрачная проекция корпуса, выше — сетка " +
            "одного цвета, а по уровню идёт полоса фронта. Линии проходят через обе части. " +
            "Каркас, к которому никто не подключён, рисуется тем же шейдером неподвижно.");

        Check(box, "шейдер строительства",
            "Снятый признак возвращает прежний вид: спрайт, непрозрачность которого растёт " +
            "вместе с готовностью.",
            () => Site().Enabled, on => Site().Enabled = on);

        Colour(box, "цвет недостроенного",
            "Цвет части выше уровня готовности. Альфа задаёт её плотность.",
            () => Site().Wire, value => Site().Wire = value);

        Colour(box, "цвет фронта",
            "Цвет полосы на уровне готовности. Альфа задаёт, насколько полоса перекрывает " +
            "то, что под ней.",
            () => Site().Edge, value => Site().Edge = value);

        Slide(box, "плотность готового", 0f, 1f, 0.01f,
            "Непрозрачность уже проявленной части конструкции.",
            () => Site().BuiltOpacity, value => Site().BuiltOpacity = value);

        Slide(box, "ширина фронта", 0f, 0.3f, 0.005f,
            "Ширина полосы фронта, доля высоты спрайта.",
            () => Site().EdgeWidth, value => Site().EdgeWidth = value);

        Slide(box, "полос сетки", 0f, 64f, 1f,
            "Сколько полос укладывается по высоте спрайта.",
            () => Site().ScanCount, value => Site().ScanCount = value);

        Slide(box, "глубина сетки", 0f, 1f, 0.01f,
            "Сила линий на недостроенной и уже проявленной частях.",
            () => Site().ScanStrength, value => Site().ScanStrength = value);

        Slide(box, "скорость сетки", 0f, 20f, 0.1f,
            "Скорость бега полос. Действует только при активной работе: у брошенного " +
            "каркаса сетка стоит.",
            () => Site().ScanSpeed, value => Site().ScanSpeed = value);

        Slide(box, "размах волнения", 0f, 0.2f, 0.001f,
            "На сколько колеблется уровень готовности, доля высоты спрайта. Тоже только при " +
            "активной работе.",
            () => Site().WaveAmplitude, value => Site().WaveAmplitude = value);

        Slide(box, "частота волнения", 0f, 20f, 0.1f,
            "Сколько периодов колебания укладывается по ширине спрайта.",
            () => Site().WaveFrequency, value => Site().WaveFrequency = value);

        Slide(box, "скорость волнения", 0f, 20f, 0.1f,
            "Как быстро идёт колебание уровня.",
            () => Site().WaveSpeed, value => Site().WaveSpeed = value);

        Slide(box, "сглаживание работы", 0.01f, 2f, 0.01f,
            "За сколько секунд признак работы доходит от нуля до единицы и обратно. " +
            "Строитель подключается мгновенно, и без сглаживания движение обрывалось бы " +
            "рывком.",
            () => Site().ActivityFade, value => Site().ActivityFade = value);
    }

    /// <summary>Тень препятствий. Принадлежит местности, а не своду настроек графики.</summary>
    private void FillObstacleShade(Node box)
    {
        Section(box, "Тень препятствий",
            "Затенение вокруг всего, что попадает в растр навигации, — построек и каркасов. " +
            "Плотность выводится из chamfer-расстояния, то есть из той же величины, по " +
            "которой навигация судит о проходимости. Отсюда и чтение картинки: узкий проход " +
            "затенён по всей ширине, а у отдельно стоящего здания тень ограничена каймой.");

        Check(box, "показывать", "Рисовать ли тень препятствий.",
            () => Shade()?.Enabled ?? false, on => SetShade(settings => settings.Enabled = on));

        Check(box, "ступенчатая подача",
            "Плотность меняется ступенями по ячейкам растра вместо плавного спада. Ступень " +
            "проходит там же, где меняется решение навигации о проходимости, поэтому такая " +
            "подача честнее к устройству растра; плавная выглядит мягче. Признак общий для " +
            "всех местностей и лежит на отрисовщике, а не в настройках поверхности.",
            () => ShadeRenderer()?.Pixelated ?? false,
            on =>
            {
                var renderer = ShadeRenderer();

                if (renderer != null)
                    renderer.Pixelated = on;
            });

        Slide(box, "ширина полосы", 0f, 64f, 1f,
            "Насколько далеко тень уходит от края препятствия, пикселей. Меряется свободным " +
            "местом: на границе полосы вокруг точки помещается круг такого радиуса. Сверху " +
            "ограничена насыщением растра — при нынешних настройках навигации это " +
            $"{ShadowSettings.MaxWidthPx:0} px, и большее значение ничего не изменит.",
            () => Shade()?.WidthPx ?? 0f,
            value => SetShade(settings => settings.WidthPx = value));

        Slide(box, "угасание", 0f, 8f, 0.1f,
            "Насколько быстро плотность сходит на нет к границе полосы. Ноль оставляет " +
            "плотность целиком за цветовой шкалой, единица гасит её равномерно, большие " +
            "значения держат тень плотной почти до конца полосы и роняют у самого края.",
            () => Shade()?.Falloff ?? 1f,
            value => SetShade(settings => settings.Falloff = value));

        Colour(box, "цвет у постройки",
            "Крайняя левая точка цветовой шкалы: цвет и плотность вплотную к краю препятствия.",
            () => Ramp(false), value => SetRamp(false, value));

        Colour(box, "цвет на границе",
            "Крайняя правая точка шкалы. Промежуточные точки правятся в ресурсе тени " +
            "текущей местности (resources/surface/<имя>/shadows_*.tres): здесь их " +
            "редактировать нечем, а трогать вслепую нельзя.",
            () => Ramp(true), value => SetRamp(true, value));
    }

    private void FillGiz(Node box)
    {
        Section(box, "Области юнитов",
            "Круги зрения, атаки и рабочей руки. По умолчанию выключены. " +
            "Матрица ниже включает вид для всех / своих / врагов: «все» перекрывает " +
            "остальные два. Помимо матрицы круги появляются у выделенных при зажатом Ctrl " +
            "и радиусы атаки турелей — при постановке постройки со стволом.");

        FilterMatrix(box, "зрение",
            "Круг обзора сущности.",
            GizmoFlags.Vision);

        FilterMatrix(box, "атака",
            "Дальность ствола и рёбра конуса прицеливания.",
            GizmoFlags.Attack);

        FilterMatrix(box, "работа",
            "Радиус строительной руки / манипулятора.",
            GizmoFlags.Work);

        var hints = new Label
        {
            Text = "Ctrl + выделение — инструменты выбранных\n" +
                   "стройка турели — покрытие стоящих стволов",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hints.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(hints);
    }

    /// <summary>
    /// Три флажка одного вида: все / свои / враги. «Все» при включении делает остальные
    /// неважными, но не сбрасывает их — сняв «все», прежний выбор восстанавливается.
    /// </summary>
    private static void FilterMatrix(Node parent, string title, string tooltip, GizmoFilter filter)
    {
        if (parent.GetChildCount() > 0)
            parent.AddChild(new HSeparator());

        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 12);
        heading.AddThemeColorOverride("font_color", Heading);
        parent.AddChild(heading);
        Explain(heading, tooltip);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        Check(row, "все", "Показать у всех сторон; «свои» и «враги» при этом не читаются.",
            () => filter.All, on => filter.All = on);
        Check(row, "свои", "Сторона игрока.",
            () => filter.Ally, on => filter.Ally = on);
        Check(row, "враги", "Сторона противника.",
            () => filter.Enemy, on => filter.Enemy = on);
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
                   "Shift — дописать в очередь\n" +
                   "Выделено: A атака, M идти, R чинить, F следовать, Del снос\n" +
                   "В режиме приказа: ПКМ — цель, ЛКМ или Escape — отмена\n" +
                   "Пусто: A боевые на экране, F строители на экране\n" +
                   "Камера: край экрана, СКМ — перетаскивание, колесо — зум\n" +
                   "C — очереди всех своих\n" +
                   "F3 — эта панель, F2 — песочница",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hints.AddThemeFontSizeOverride("font_size", 11);
        box.AddChild(hints);
    }

    private void FillPrf(Node box)
    {
        Section(box, "Замеры систем",
            "Время шага каждой системы в порядке вызова: сначала физический цикл, затем " +
            "графический. Под названием системы стоят три последних значения, от старого " +
            "к новому, в миллисекундах.");

        Check(box, "вести замеры",
            "Измерять время шага. Пока признак снят, планировщик вызывает системы без " +
            "обёртки, а накопленные ряды сброшены: сравнивать текущее время с давно " +
            $"устаревшим было бы бессмысленно. Окно хранения {StepProfiler.WindowSeconds:0} с.",
            () => DebugFlags.Profile, on => DebugFlags.Profile = on);

        _profile = Report(box,
            "Каждое число — среднее время шага за четверть секунды: значение отдельного " +
            "кадра скачет от уборки мусора и планировщика операционной системы, и читать " +
            "его, обновляемое шестьдесят раз в секунду, невозможно. Последнее значение " +
            "выделяется цветом, когда превышает медиану окна: жёлтым от " +
            $"{Percent(StepProfiler.WarnRatio)}, оранжевым от {Percent(StepProfiler.AlertRatio)}, " +
            $"красным от {Percent(StepProfiler.AlarmRatio)}. У систем быстрее " +
            $"{StepProfiler.NoiseFloorMs:0.00} мс раскраска не ведётся: там относительный " +
            "разброс говорит только о погрешности измерения.");

        Section(box, "Срез производительности",
            "Sampling стеков CLR текущего процесса. После остановки рядом с .nettrace " +
            "пишется .speedscope.json — его открывают на speedscope.app. В Speedscope " +
            "нужен именно JSON, не .nettrace. Метки Physics/Process видны после " +
            "dotnet-trace convert --format Chromium на ui.perfetto.dev. В соседний " +
            ".game.jsonl с выбранным интервалом записываются число сущностей и показатели " +
            "систем. Оба файла создаются только после нажатия кнопки. Каталог " +
            $"{DotnetTraceCapture.RelativeDir}/; нужен tool: dotnet tool install -g dotnet-trace.");

        _traceStatus = Readout(box,
            "Ключ сессии, время, состояние обоих каналов и стоимость последнего игрового снимка.");

        _traceInterval = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _traceInterval.AddItem("игровой снимок каждые 0,25 с");
        _traceInterval.AddItem("игровой снимок каждые 0,5 с");
        _traceInterval.AddItem("игровой снимок каждую 1 с");
        _traceInterval.Select(0);
        _traceInterval.ItemSelected += OnTraceIntervalSelected;
        Explain(_traceInterval,
            "Интервал записи состояния мира и систем. Во время записи изменить его нельзя.");
        box.AddChild(_traceInterval);

        var row = new HBoxContainer();
        box.AddChild(row);

        _traceStart = new Button
        {
            Text = "начать срез",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _traceStart.AddThemeFontSizeOverride("font_size", 11);
        _traceStart.Pressed += OnTraceStart;
        Explain(_traceStart,
            "Подключить EventPipe к этому процессу и писать sampling в новый файл.");
        row.AddChild(_traceStart);

        _traceStop = new Button
        {
            Text = "остановить",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _traceStop.AddThemeFontSizeOverride("font_size", 11);
        _traceStop.Pressed += OnTraceStop;
        Explain(_traceStop,
            "Завершить текущую запись и сбросить файл на диск. Без активной записи " +
            "кнопка не действует.");
        row.AddChild(_traceStop);

        RefreshTrace();
    }

    private void OnTraceStart()
    {
        if (!PerformanceCapture.TryStart(out string error) && error != null)
            GD.PushWarning($"[DebugPanel] срез: {error}");

        RefreshTrace();
    }

    private void OnTraceStop()
    {
        if (!PerformanceCapture.RequestStop(out string error) && error != null)
            GD.PushWarning($"[DebugPanel] срез: {error}");

        RefreshTrace();
    }

    private static void OnTraceIntervalSelected(long index)
    {
        PerformanceCapture.IntervalSeconds = index switch
        {
            1 => 0.5,
            2 => 1.0,
            _ => 0.25,
        };
    }

    private void RefreshTrace()
    {
        if (_traceStatus == null)
            return;

        var elapsed = PerformanceCapture.Elapsed;
        string time = PerformanceCapture.FormatElapsed(elapsed);
        bool busy = PerformanceCapture.Busy;
        bool recording = PerformanceCapture.Recording;

        _traceStatus.Text =
            $"сессия {PerformanceCapture.Key}\n" +
            $"время {time}\n" +
            $"{PerformanceCapture.Status}\n" +
            $"игровых снимков {PerformanceCapture.Samples}, пропущено " +
            $"{PerformanceCapture.Dropped}, последний {PerformanceCapture.LastCaptureMs:0.00} мс";

        if (_traceStart != null)
            _traceStart.Disabled = busy;

        if (_traceStop != null)
            _traceStop.Disabled = !recording;

        if (_traceInterval != null)
            _traceInterval.Disabled = busy;
    }

    /// <summary>Порог превышения как проценты — так он и назван в подсказке.</summary>
    private static string Percent(double ratio) => $"{(ratio - 1.0) * 100.0:0}%";

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

    /// <summary>Круг с крестом — области инструментов юнита.</summary>
    private static Texture2D IconGiz() => Paint(image =>
    {
        int[] ring =
        {
            5, 2, 6, 2, 7, 2, 8, 2, 9, 2, 10, 2,
            4, 3, 11, 3,
            3, 4, 12, 4,
            2, 5, 13, 5,
            2, 6, 13, 6,
            2, 9, 13, 9,
            2, 10, 13, 10,
            3, 11, 12, 11,
            4, 12, 11, 12,
            5, 13, 6, 13, 7, 13, 8, 13, 9, 13, 10, 13,
        };

        for (int i = 0; i < ring.Length; i += 2)
            Dot(image, ring[i], ring[i + 1]);

        for (int i = 4; i <= 11; i++)
        {
            Dot(image, i, 7);
            Dot(image, i, 8);
            Dot(image, 7, i);
            Dot(image, 8, i);
        }
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

    /// <summary>Заполненный квадрат в рамке — постройка и её тень.</summary>
    private static Texture2D IconGfx() => Paint(image =>
    {
        for (int x = 6; x <= 9; x++)
            for (int y = 6; y <= 9; y++)
                Dot(image, x, y);

        for (int i = 3; i <= 12; i++)
        {
            Dot(image, i, 3);
            Dot(image, i, 12);
            Dot(image, 3, i);
            Dot(image, 12, i);
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

    /// <summary>Ломаная с выбросом — ряд замеров.</summary>
    private static Texture2D IconPrf() => Paint(image =>
    {
        int[] path =
        {
            2, 11, 3, 11, 4, 10, 5, 11, 6, 11, 7, 8, 8, 4, 9, 8, 10, 11, 11, 11, 12, 10, 13, 11,
        };

        for (int i = 0; i < path.Length; i += 2)
            Dot(image, path[i], path[i + 1]);

        for (int y = 5; y <= 7; y++)
            Dot(image, 8, y);

        for (int y = 9; y <= 10; y++)
        {
            Dot(image, 7, y);
            Dot(image, 9, y);
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
    /// Поле для текста с разметкой. Отличается от <see cref="Readout"/> тем, что part
    /// строки можно выделить цветом, а обычная надпись красится только целиком.
    /// Перенос выключен намеренно: список замеров выровнен по столбцам, и перенос
    /// длинного названия системы разбил бы выравнивание.
    /// </summary>
    private static RichTextLabel Report(Node parent, string tooltip)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(PanelWidth, 0),
        };

        label.AddThemeFontSizeOverride("normal_font_size", 11);
        label.AddThemeColorOverride("default_color", Numbers);
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
    /// Ползунок величины с показом текущего значения. Отличается от <see cref="Check"/> тем,
    /// что правит не признак, а число: ширина тени и сила угасания подбираются на глаз,
    /// и вводить их с клавиатуры значило бы перебирать значения вслепую.
    /// </summary>
    private static void Slide(Node parent, string title, float min, float max, float step,
        string tooltip, Func<float> read, Action<float> write)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var label = new Label { Text = title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", 11);
        row.AddChild(label);

        float current = read();

        var readout = new Label { Text = $"{current:0.##}" };
        readout.AddThemeFontSizeOverride("font_size", 11);
        readout.AddThemeColorOverride("font_color", Numbers);
        row.AddChild(readout);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = current,
            CustomMinimumSize = new Vector2(PanelWidth, 16),
        };

        slider.ValueChanged += value =>
        {
            write((float)value);
            readout.Text = $"{value:0.##}";
        };

        parent.AddChild(slider);

        Explain(label, tooltip);
        Explain(slider, tooltip);
    }

    /// <summary>
    /// Настройки тумана берутся у системы зрения, а не хранятся снимком: ресурс живёт
    /// столько же, сколько сессия, а панель переживает её пересборку.
    /// </summary>
    /// <summary>Настройки затенения. Правки идут прямо в ресурс подсистемы.</summary>
    private static ShadingSettings Ao() => GraphicsSettings.Shade;

    /// <summary>Настройки вида стройки.</summary>
    private static ConstructionSettings Site() => GraphicsSettings.Building;

    private static FogSettings Fog() => GameManager.I?.System<VisionSystem>()?.Settings;

    /// <summary>
    /// Отрисовщик тени на слое теней. Из него берутся и настройки вида текущей местности,
    /// и общий признак ступенчатой подачи.
    /// </summary>
    private static ShadowRenderer ShadeRenderer()
    {
        var layer = GameManager.I?.Playground?.Layer(WorldLayer.Shadows);

        if (layer == null)
            return null;

        foreach (var child in layer.GetChildren())
        {
            if (child is ShadowRenderer renderer)
                return renderer;
        }

        return null;
    }

    /// <summary>
    /// Настройки вида тени берутся у отрисовщика на слое теней — по тем же основаниям,
    /// по каким настройки тумана берутся у системы зрения.
    /// </summary>
    private static ShadowSettings Shade() => ShadeRenderer()?.Settings;

    /// <summary>Правка настроек тени, безопасная к отсутствию слоя в сцене.</summary>
    private static void SetShade(Action<ShadowSettings> change)
    {
        var settings = Shade();

        if (settings != null)
            change(settings);
    }

    /// <summary>
    /// Крайняя точка цветовой шкалы тени. Правятся только края: промежуточные точки задают
    /// форму спада, и менять их парой выборов цвета нельзя, не разрушив замысел шкалы.
    /// </summary>
    private static Color Ramp(bool outer)
    {
        var gradient = Shade()?.Tint;

        if (gradient == null || gradient.GetPointCount() == 0)
            return Colors.Transparent;

        return gradient.GetColor(outer ? gradient.GetPointCount() - 1 : 0);
    }

    private static void SetRamp(bool outer, Color color)
    {
        var gradient = Shade()?.Tint;

        if (gradient == null || gradient.GetPointCount() == 0)
            return;

        gradient.SetColor(outer ? gradient.GetPointCount() - 1 : 0, color);
    }

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
            $"поле {NavGrid.Width}×{NavGrid.Width} по {NavGrid.CellPx} px, зазор {Const.BuildMarginPx:0} px\n" +
            $"препятствий {gm.Obstacles.Count}   ревизия {gm.Nav.Revision} " +
            $"(снимок {gm.Nav.ActiveRevision}, ждут {gm.Nav.RequestedRevision})\n" +
            $"пересчёт {gm.Nav.LastBuildMs:0.00} мс, тайлов {gm.Nav.LastRebuiltTiles}" +
            (gm.Nav.BuildPending ? ", фон занят" : "");

        _waves.Text = Waves(gm.System<WaveSystem>());
        _fog.Text = Vision(gm.System<VisionSystem>());

        RefreshProfile(gm);
        RefreshTrace();

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

    /// <summary>Состояние поля видимости: размер, источники и стоимость сбора.</summary>
    private static string Vision(VisionSystem vision)
    {
        if (vision == null)
            return "система зрения не в сцене";

        string rate = vision.Settings.EveryFrame
            ? "каждый кадр"
            : $"{vision.Settings.UpdateHz:0} раз в секунду";

        return $"поле {vision.Width}×{vision.Width} по {vision.CellPx} px, на видеокарте\n" +
               $"источников {vision.Sources} в {vision.Batches} партиях   скрыто {vision.Hidden}\n" +
               $"сбор источников {vision.LastBuildMs:0.00} мс, {rate}";
    }

    /// <summary>
    /// Список замеров. Пересобирается не каждый кадр, а при закрытии очередного интервала:
    /// сборка текста на несколько десятков систем сама стоит времени, и вести её кадр
    /// за кадром значило бы вносить в измеряемый кадр ту нагрузку, которую мы измеряем.
    /// </summary>
    private void RefreshProfile(GameManager gm)
    {
        var profiler = gm.Scheduler.Profiler;

        if (!DebugFlags.Profile)
        {
            if (_profileShown == -1)
                return;

            _profile.Text = "замеры выключены";
            _profileShown = -1;
            return;
        }

        if (profiler.Generation == _profileShown)
            return;

        _profileShown = profiler.Generation;
        _profile.Text = Profile(profiler);
    }

    private static string Profile(StepProfiler profiler)
    {
        var text = new System.Text.StringBuilder();

        double process = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;

        text.Append($"кадр {Engine.GetFramesPerSecond():0} к/с\n" +
                    $"движок: process {process:0.00} мс, physics {physics:0.00} мс");

        var tracks = profiler.Tracks;

        if (tracks.Count == 0)
        {
            text.Append("\n\nзамеров ещё нет");
            return text.ToString();
        }

        double inPhysics = 0f;
        double inProcess = 0f;

        foreach (var track in tracks)
        {
            double last = track.Value(0);

            if (double.IsNaN(last))
                continue;

            if (track.Cycle == UpdateCycle.PhysicsProcess)
                inPhysics += last;
            else
                inProcess += last;
        }

        // Итог по циклу — сумма последних значений: сравнение её со временем движка выше
        // показывает, сколько кадра приходится на системы, а сколько на всё остальное
        text.Append($"\nсистемы: физика {inPhysics:0.000} мс, графика {inProcess:0.000} мс");

        UpdateCycle? shown = null;

        foreach (var track in tracks)
        {
            if (shown != track.Cycle)
            {
                shown = track.Cycle;
                string title = shown == UpdateCycle.PhysicsProcess
                    ? "физический цикл"
                    : "графический цикл";

                text.Append($"\n\n[color=#{Heading.ToHtml(false)}]{title}[/color]");
            }

            text.Append($"\n{track.Name}\n    {Samples(track)}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Три последних значения, от старого к новому. Цветом выделяется только последнее:
    /// вопрос стоит «выросла ли задержка сейчас», и подсветка предыдущих на него не отвечает.
    /// </summary>
    private static string Samples(StepProfiler.Track track)
    {
        var text = new System.Text.StringBuilder();
        double median = track.Median();

        for (int back = StepProfiler.Shown - 1; back >= 0; back--)
        {
            double value = track.Value(back);

            if (double.IsNaN(value))
                continue;

            if (text.Length > 0)
                text.Append("   ");

            string ink = back == 0 ? Alarm(value, median) : null;

            text.Append(ink == null ? $"{value:0.000}" : $"[color={ink}]{value:0.000}[/color]");
        }

        return text.Length == 0 ? "—" : text.ToString();
    }

    /// <summary>
    /// Цвет последнего значения по превышению медианы окна. Пустой ответ означает,
    /// что выделять нечего: либо окно ещё не набрано, либо система слишком быстра,
    /// чтобы относительный разброс что-то значил.
    /// </summary>
    private static string Alarm(double value, double median)
    {
        if (double.IsNaN(median) || median < StepProfiler.NoiseFloorMs)
            return null;

        double ratio = value / median;

        if (ratio >= StepProfiler.AlarmRatio)
            return AlarmInk;

        if (ratio >= StepProfiler.AlertRatio)
            return AlertInk;

        return ratio >= StepProfiler.WarnRatio ? WarnInk : null;
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
        float tolerance = NavGrid.CellPx * 2f;

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
