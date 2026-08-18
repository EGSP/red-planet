using System.Collections.Generic;
using Godot;

/// <summary>
/// Смысловой раздел карты действий. Настройки горячих клавиш показывают действия
/// разделами, а не одним списком, поэтому раздел объявляется вместе с действием,
/// а не выводится задним числом из имени.
///
/// Порядок объявления и есть порядок показа: разделы идут от того, чем игрок занят
/// чаще всего, к тому, что нужно редко.
/// </summary>
public enum InputSection
{
    Units,
    Selection,
    Groups,
    Camera,
    View,
    Game,
    Debug,
}

/// <summary>
/// Когда действие вообще читается. По этому признаку решается, спорят ли два действия
/// за одну клавишу.
///
/// ОДНА КЛАВИША НА НЕСКОЛЬКО ДЕЙСТВИЙ — ЭТО НОРМА, а не оплошность настройки. Приказ атаки
/// читается только при непустом выделении, отбор боевых машин — только при пустом, поэтому
/// клавиша A служит обоим и ни одно нажатие не оказывается двусмысленным. Спорят лишь те,
/// кого одно и то же нажатие может застать разом.
/// </summary>
public enum InputScope
{
    /// <summary>Читается в любом положении: группы, камера, отладка, отмена.</summary>
    Always,

    /// <summary>Читается, когда выделение есть: виды приказа.</summary>
    WithSelection,

    /// <summary>Читается, когда выделения нет: отбор по признаку.</summary>
    WithoutSelection,

    /// <summary>
    /// Ведёт камеру в режиме <see cref="CameraPanMode.Keys"/> и только при отпущенном Alt.
    ///
    /// СПОРА С ПРОЧИМИ ДЕЙСТВИЯМИ У НЕГО НЕТ, И ЭТО ЗАМЫСЕЛ, А НЕ ПОСЛАБЛЕНИЕ. Клавиши
    /// панорамирования по умолчанию — W, A, S, D, и A занята приказом атаки. Камера
    /// в режиме клавиш забирает нажатие себе целиком: <c>CameraRig</c> помечает событие
    /// обработанным, и до приказа оно не доходит. Alt же камерой не читается вовсе, поэтому
    /// A с Alt означает атаку и в этом режиме. Отсюда следует, что одно нажатие никогда
    /// не застаёт оба действия разом, и отбирать клавишу друг у друга им незачем.
    ///
    /// Между собой действия панорамирования спорят как обычно: две стороны света на одной
    /// клавише означали бы, что одна из них не сработает никогда.
    /// </summary>
    CameraPan,
}

/// <summary>
/// Одно действие карты: имя, под которым его знает <see cref="InputMap"/>, раздел
/// настроек, название для игрока, привязка по умолчанию и положение, в котором действие
/// читается.
///
/// Умолчанием служит <see cref="InputBinding"/>, а не клавиша: часть действий назначена
/// на кнопку мыши (перетаскивание камеры), и клавиша обращается в привязку сама собой.
/// </summary>
public readonly record struct InputAction(
    string Name,
    InputSection Section,
    string Title,
    InputBinding Default,
    InputScope Scope = InputScope.Always);

/// <summary>
/// Карта действий игрока: единственное место, где записано, какая клавиша что означает.
///
/// ЗАЧЕМ ОНА ЕСТЬ. Прежде привязки существовали литералами вида <c>Key.C</c> в шести файлах,
/// и перечислить занятые клавиши, не прочитав проект целиком, было невозможно. Отсюда две
/// беды: раскладку нельзя было спланировать (добавляя приказ на клавишу, приходилось гадать,
/// не занята ли она), а переназначение клавиш игроком было неосуществимо в принципе —
/// переназначать было нечего.
///
/// ПОЧЕМУ СПРАВОЧНИК В КОДЕ, А НЕ СЕКЦИЯ <c>[input]</c> В project.godot. Секция проекта
/// хранит только привязки: ни раздела настроек, ни названия для игрока в ней держать негде,
/// а нужны и то и другое. Держать половину сведений в одном месте, половину в другом значило
/// бы, что забытая строка даёт работающее действие, невидимое в настройках. Поэтому
/// объявление здесь и полное, а <see cref="InputMap"/> используется по прямому назначению —
/// как механизм сопоставления событий, который наполняется при запуске.
///
/// КЛАВИША ЗАДАЁТСЯ ФИЗИЧЕСКАЯ, А НЕ ПО РАСКЛАДКЕ. Прежний разбор по
/// <see cref="InputEventKey.Keycode"/> означал, что на русской раскладке горячие клавиши
/// не работают вовсе: движок возвращает в этом поле букву раскладки, а не клавишу. Физический
/// код от раскладки не зависит, поэтому клавиша под пальцем остаётся той же самой.
/// </summary>
public static class InputActions
{
    // ── управление юнитами ─────────────────────────────────────────────────────

    public const string UnitAttack = "unit_attack";
    public const string UnitMove = "unit_move";
    public const string UnitRepair = "unit_repair";

    /// <summary>
    /// Патруль: точка обхода щелчком, область — растягиванием. Маршрут набирается
    /// цепочкой по Shift, как и всякий другой приказ.
    /// </summary>
    public const string UnitPatrol = "unit_patrol";

    /// <summary>
    /// Помощь строительству: режим, в котором указывается не постройка из панели, а уже
    /// размеченное — план или каркас, которому нужна работа.
    /// </summary>
    public const string UnitBuild = "unit_build";
    public const string UnitFollow = "unit_follow";
    public const string UnitDelete = "unit_delete";

    // ── выделение ──────────────────────────────────────────────────────────────

    /// <summary>Приставка действий боевых групп: за нею идёт подпись слота.</summary>
    private const string GroupPrefix = "select_group_";

    /// <summary>
    /// Отбор боевых машин и строителей на экране. Клавиши те же, что у атаки и
    /// сопровождения: при пустом выделении приказывать некому, и клавиша достаётся
    /// второму смыслу — см. <c>CommandSystem.Context</c>.
    /// </summary>
    public const string SelectArmy = "select_army";

    public const string SelectBuilders = "select_builders";

    // ── камера ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Переход на соседнюю ступень зума. Читается всегда: камера не спрашивает,
    /// выделен ли кто-нибудь, и оспорить эти клавиши может любое другое действие.
    /// </summary>
    public const string CameraZoomIn = "camera_zoom_in";

    public const string CameraZoomOut = "camera_zoom_out";

    /// <summary>
    /// Удержание, при котором движение мыши тянет камеру. Прежде это была средняя кнопка,
    /// записанная в <c>CameraRig</c> литералом; теперь она умолчание переназначаемого
    /// действия, поскольку игроки держат перетаскивание и на боковых кнопках мыши,
    /// и на клавише под левой рукой.
    ///
    /// Действие читается удержанием, а не нажатием: <c>CameraRig</c> включает
    /// перетаскивание по <c>IsPressed</c> события и выключает по отпусканию. Отсюда
    /// следует, что назначать сюда прокрутку колеса нельзя — см.
    /// <see cref="InputBinding.CanBind"/>.
    /// </summary>
    public const string CameraDrag = "camera_drag";

    /// <summary>
    /// Ход камеры клавишами. Читаются только в режиме <see cref="CameraPanMode.Keys"/>,
    /// поэтому умолчания W, A, S, D не отнимают клавиши у приказов, пока игрок этот режим
    /// не выбрал.
    ///
    /// Порядок в <see cref="CameraPanActions"/> — стороны света по часовой стрелке от
    /// верха: он же порядок строк в настройках, и совпадение избавляет от второго перечня.
    /// </summary>
    public const string CameraPanUp = "camera_pan_up";

    public const string CameraPanRight = "camera_pan_right";
    public const string CameraPanDown = "camera_pan_down";
    public const string CameraPanLeft = "camera_pan_left";

    /// <summary>
    /// Действия хода камеры и отвечающие им направления в экранных координатах. Один
    /// перечень служит и разбору нажатий, и сложению направления, поэтому добавление
    /// стороны света не требует править две согласованные таблицы.
    /// </summary>
    public static readonly (string Action, Vector2 Direction)[] CameraPanActions =
    {
        (CameraPanUp, Vector2.Up),
        (CameraPanRight, Vector2.Right),
        (CameraPanDown, Vector2.Down),
        (CameraPanLeft, Vector2.Left),
    };

    // ── отображение, игра, отладка ─────────────────────────────────────────────

    public const string ViewOrdersAll = "view_orders_all";
    public const string GameCancel = "game_cancel";
    public const string DebugToggle = "debug_panel";

    /// <summary>
    /// Панель песочницы: мгновенная расстановка любого вида из справочника. Открывается
    /// вместо панели отладки — обе занимают одно место экрана (см. <see cref="ToolPanel"/>).
    /// </summary>
    public const string SandboxToggle = "sandbox_panel";

    /// <summary>
    /// Лента частоты кадров. В набор взаимоисключающих панелей не входит и потому имеет
    /// собственную клавишу: провал частоты ищут при открытой панели отладки, а не вместо неё.
    /// </summary>
    public const string DebugFps = "debug_fps";

    public const string DebugRestart = "debug_restart";

    /// <summary>
    /// Клавиши боевых групп по порядку слотов. Ноль стоит в конце, а не в начале:
    /// на клавиатуре он справа от девятки, и порядок в полосе групп должен совпадать
    /// с порядком под пальцами.
    /// </summary>
    private static readonly Key[] GroupKeys =
    {
        Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5,
        Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0,
    };

    private static InputAction[] _all;

    /// <summary>
    /// Все действия карты. Порядок внутри раздела — порядок объявления, и настройки
    /// показывают их именно так.
    ///
    /// Виды приказа объявлены здесь целиком, хотя читателя пока получил только снос:
    /// карта описывает раскладку, а раскладку нужно видеть всю разом, иначе следующая
    /// клавиша снова выбиралась бы наугад.
    /// </summary>
    public static InputAction[] All => _all ??= Build();

    /// <summary>
    /// Набор собирается списком, а не массивом заданной длины. Длина, посчитанная вручную,
    /// однажды разошлась с числом строк: в наборе оставался пустой элемент с именем null,
    /// и экран настроек падал на нём при первом же открытии. Список исключает эту ошибку
    /// целиком, поскольку считать нечего.
    /// </summary>
    private static InputAction[] Build()
    {
        var list = new List<InputAction>
        {
            new(UnitAttack, InputSection.Units, "Атаковать", Key.A,
                InputScope.WithSelection),
            new(UnitMove, InputSection.Units, "Идти", Key.M,
                InputScope.WithSelection),
            new(UnitPatrol, InputSection.Units, "Патрулировать", Key.P,
                InputScope.WithSelection),
            new(UnitRepair, InputSection.Units, "Чинить", Key.R,
                InputScope.WithSelection),
            new(UnitBuild, InputSection.Units, "Помощь строительству", Key.B,
                InputScope.WithSelection),
            new(UnitFollow, InputSection.Units, "Следовать", Key.F,
                InputScope.WithSelection),
            new(UnitDelete, InputSection.Units, "Снос", Key.Delete,
                InputScope.WithSelection),

            new(SelectArmy, InputSection.Selection, "Боевые на экране", Key.A,
                InputScope.WithoutSelection),
            new(SelectBuilders, InputSection.Selection, "Строители на экране", Key.F,
                InputScope.WithoutSelection),
        };

        for (int slot = 0; slot < GroupKeys.Length; slot++)
            list.Add(new(GroupAction(slot), InputSection.Groups,
                $"Группа {ControlGroups.Label(slot)}", GroupKeys[slot]));

        list.Add(new(CameraZoomIn, InputSection.Camera, "Приблизить", Key.Z));
        list.Add(new(CameraZoomOut, InputSection.Camera, "Отдалить", Key.X));
        list.Add(new(CameraDrag, InputSection.Camera, "Перетаскивать камеру",
            InputBinding.Of(MouseButton.Middle)));

        list.Add(new(CameraPanUp, InputSection.Camera, "Вверх", Key.W, InputScope.CameraPan));
        list.Add(new(CameraPanLeft, InputSection.Camera, "Влево", Key.A, InputScope.CameraPan));
        list.Add(new(CameraPanDown, InputSection.Camera, "Вниз", Key.S, InputScope.CameraPan));
        list.Add(new(CameraPanRight, InputSection.Camera, "Вправо", Key.D, InputScope.CameraPan));

        list.Add(new(ViewOrdersAll, InputSection.View, "Очереди всех своих", Key.C));
        list.Add(new(GameCancel, InputSection.Game, "Отмена, меню паузы", Key.Escape));
        list.Add(new(SandboxToggle, InputSection.Debug, "Панель песочницы", Key.F2));
        list.Add(new(DebugToggle, InputSection.Debug, "Панель отладки", Key.F3));
        list.Add(new(DebugFps, InputSection.Debug, "Лента частоты кадров", Key.F4));
        list.Add(new(DebugRestart, InputSection.Debug, "Пересобрать сессию", Key.F5));

        return list.ToArray();
    }

    /// <summary>Имя действия для слота боевой группы.</summary>
    public static string GroupAction(int slot) => GroupPrefix + ControlGroups.Label(slot);

    /// <summary>
    /// Виды приказа, которые игрок задаёт клавишей, и действия, которыми они заданы.
    /// Единственное место этого соответствия: им пользуются и разбор нажатия,
    /// и панель приказов, где показана назначенная клавиша.
    ///
    /// Стройка входит сюда в единственном смысле — помощи уже размеченному: клавиша включает
    /// режим, в котором указывается план или каркас, а не выбирается постройка. Выбор самой
    /// постройки по-прежнему делается панелью и клавиши не имеет.
    ///
    /// Снос не входит потому, что цели не требует и выдаётся сразу.
    /// </summary>
    public static readonly (OrderKind Kind, string Action)[] OrderActions =
    {
        (OrderKind.Attack, UnitAttack),
        (OrderKind.Move, UnitMove),
        (OrderKind.Patrol, UnitPatrol),
        (OrderKind.Build, UnitBuild),
        (OrderKind.Repair, UnitRepair),
        (OrderKind.Follow, UnitFollow),
    };

    /// <summary>
    /// Действие, которым задаётся вид приказа, либо null. Снос стоит особняком: он выдаётся
    /// сразу и в перечень задаваемых видов не входит, но клавишу показать нужно и у него.
    /// </summary>
    public static string ActionOf(OrderKind kind)
    {
        if (kind == OrderKind.Delete)
            return UnitDelete;

        foreach (var (candidate, action) in OrderActions)
            if (candidate == kind)
                return action;

        return null;
    }

    /// <summary>
    /// Привязка, назначенная действию прямо сейчас, либо <see cref="InputBinding.None"/>.
    ///
    /// У клавиши берётся физический код, а не раскладочный: на клавиатуре выгравирована
    /// латиница, и показанное игроку обязано совпадать с тем, что у него под пальцами,
    /// а не с буквой включённой раскладки.
    /// </summary>
    public static InputBinding Bound(string action)
    {
        if (action == null || !InputMap.HasAction(action))
            return InputBinding.None;

        foreach (var bound in InputMap.ActionGetEvents(action))
        {
            var binding = InputBinding.Of(bound);

            if (!binding.IsEmpty)
                return binding;
        }

        return InputBinding.None;
    }

    /// <summary>Подпись назначенной привязки для показа игроку, либо пустая строка.</summary>
    public static string KeyLabel(string action) => Bound(action).Label();

    /// <summary>
    /// Занести действия в <see cref="InputMap"/> и применить поверх сохранённые
    /// переназначения. Зовёт <see cref="Root"/> при запуске, до создания меню и сессии.
    ///
    /// Уже объявленное действие не трогается: иначе переназначение, сделанное игроком,
    /// затиралось бы при каждом вызове.
    /// </summary>
    public static void Ensure()
    {
        foreach (var action in All)
        {
            if (InputMap.HasAction(action.Name))
                continue;

            InputMap.AddAction(action.Name);
            Bind(action.Name, action.Default);
        }

        Keybinds.Load();
    }

    /// <summary>
    /// Назначить действию единственную привязку. Прежние события снимаются: у действия
    /// в этой игре ровно одна привязка, и вторая означала бы, что показанное в настройках
    /// назначение — не вся правда.
    /// </summary>
    public static void Bind(string action, InputBinding binding)
    {
        if (!InputMap.HasAction(action))
            return;

        InputMap.ActionEraseEvents(action);

        // Действие без привязки — законное состояние: игрок мог отдать клавишу другому,
        // и лучше показать пустую строку в настройках, чем молча оставить двух владельцев
        if (binding.ToEvent() is { } bound)
            InputMap.ActionAddEvent(action, bound);
    }

    /// <summary>Описание действия по имени либо null.</summary>
    public static InputAction? Find(string name)
    {
        foreach (var action in All)
            if (action.Name == name)
                return action;

        return null;
    }

    /// <summary>
    /// Могут ли два действия оказаться под одним нажатием.
    ///
    /// Читаемое всегда спорит с чем угодно; приказ и отбор не спорят никогда, поскольку
    /// одно читается при непустом выделении, другое при пустом, и вместе их застать нельзя.
    /// Отсюда следует, что одна клавиша на приказ атаки и на отбор боевых машин — это
    /// не оплошность настройки, а замысел, и запрещать её нельзя.
    ///
    /// Ход камеры клавишами разобран отдельно и первым: он не спорит ни с чем, кроме
    /// самого себя, поскольку в своём режиме забирает нажатие целиком, а под Alt
    /// не читается вовсе (см. <see cref="InputScope.CameraPan"/>). Разбирать его общим
    /// правилом было нельзя: <see cref="InputScope.Always"/> спорит с чем угодно,
    /// и назначение хода на W отобрало бы клавишу у действия, которое ту же клавишу
    /// разделяет с камерой намеренно.
    /// </summary>
    public static bool Collide(InputScope a, InputScope b)
    {
        if (a == InputScope.CameraPan || b == InputScope.CameraPan)
            return a == b;

        return a == InputScope.Always || b == InputScope.Always || a == b;
    }

    /// <summary>Название раздела настроек.</summary>
    public static string Title(InputSection section) => section switch
    {
        InputSection.Units => "Приказы выделенным",
        InputSection.Selection => "Выделение с пустым отрядом",
        InputSection.Groups => "Боевые группы",
        InputSection.Camera => "Камера",
        InputSection.View => "Отображение",
        InputSection.Game => "Игра",
        InputSection.Debug => "Отладка",
        _ => section.ToString(),
    };

    /// <summary>
    /// Слот боевой группы, которому отвечает событие, либо -1.
    ///
    /// СОПОСТАВЛЕНИЕ НЕСТРОГОЕ НАМЕРЕННО: <c>Ctrl</c> с цифрой попадает в то же действие,
    /// что и цифра без него, а различает их проверка модификатора у самого события.
    /// Строгое сопоставление означало бы вдвое больше действий в карте и вдвое больше строк
    /// в настройках ради одного и того же выбора слота.
    /// </summary>
    public static int GroupSlot(InputEvent @event)
    {
        for (int slot = 0; slot < GroupKeys.Length; slot++)
            if (@event.IsActionPressed(GroupAction(slot)))
                return slot;

        return -1;
    }
}
