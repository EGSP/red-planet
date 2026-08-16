using Godot;

/// <summary>
/// Чем игрок ведёт камеру по карте. Два способа исключают друг друга: они спорят
/// за одно и то же — за то, что делает курсор у края экрана и что делают клавиши
/// под левой рукой.
/// </summary>
public enum CameraPanMode
{
    /// <summary>
    /// Курсор, подведённый к краю окна, толкает камеру. Клавиши при этом свободны
    /// и достаются приказам целиком.
    /// </summary>
    Border,

    /// <summary>
    /// Камеру ведут клавиши (по умолчанию W, A, S, D), а край окна её не двигает.
    /// Клавиша, зажатая вместе с Alt, камере не достаётся и означает то же, что означала
    /// бы без этого режима, — см. <see cref="InputScope.CameraPan"/>.
    /// </summary>
    Keys,
}

/// <summary>
/// Настройки управления камерой, выбранные игроком: пока — способ панорамирования.
///
/// ПОЧЕМУ НЕ В <see cref="CameraSettings"/>. Тот ресурс держит подбираемые числа замысла:
/// скорость, упоры зума, ширину приграничной полосы, — и правит его разработчик в редакторе.
/// Здесь же лежит выбор игрока, который обязан пережить обновление игры и не может быть
/// записан в ресурс проекта.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ ФАЙЛ, А НЕ СЕКЦИЯ В <c>user://input.cfg</c>. Сброс привязок
/// (<see cref="Keybinds.ResetAll"/>) стирает свой файл целиком, и способ панорамирования,
/// оказавшись в нём, сбрасывался бы вместе с клавишами, хотя кнопка сброса обещает только
/// клавиши. То же основание, по которому отдельно живёт <see cref="UiScale"/>.
/// </summary>
public static class CameraControls
{
    private const string Path = "user://camera.cfg";
    private const string Section = "camera";
    private const string ModeKey = "pan_mode";

    public const CameraPanMode DefaultMode = CameraPanMode.Border;

    /// <summary>Способ панорамирования, выбранный игроком.</summary>
    public static CameraPanMode PanMode { get; private set; } = DefaultMode;

    /// <summary>Камеру ведут клавиши, а край окна не действует.</summary>
    public static bool KeysMode => PanMode == CameraPanMode.Keys;

    /// <summary>
    /// Прочитать сохранённое. Зовётся из <see cref="Root"/> при запуске. Отсутствие файла —
    /// обычное дело при первом запуске и ошибкой не считается.
    /// </summary>
    public static void Load()
    {
        var file = new ConfigFile();

        if (file.Load(Path) != Error.Ok)
            return;

        int stored = (int)file.GetValue(Section, ModeKey, (int)DefaultMode);

        // Значение, которого в перечислении нет, могло остаться от другой версии игры:
        // способ панорамирования тогда берётся обычный, а не отсутствующий
        PanMode = System.Enum.IsDefined(typeof(CameraPanMode), stored)
            ? (CameraPanMode)stored
            : DefaultMode;
    }

    /// <summary>Задать способ панорамирования и записать выбор на диск.</summary>
    public static void SetPanMode(CameraPanMode mode)
    {
        if (mode == PanMode)
            return;

        PanMode = mode;
        Save();
    }

    public static void Reset() => SetPanMode(DefaultMode);

    /// <summary>Название способа для показа игроку.</summary>
    public static string Label(CameraPanMode mode) => mode switch
    {
        CameraPanMode.Keys => "Клавишами",
        _ => "Курсором у края экрана",
    };

    /// <summary>
    /// Записать отличие от умолчания. Умолчание файла не занимает — то же правило,
    /// что у привязок клавиш и у масштаба интерфейса: иначе смена умолчания в коде
    /// не дошла бы до тех, кто однажды открывал настройки.
    /// </summary>
    private static void Save()
    {
        if (PanMode == DefaultMode)
        {
            if (FileAccess.FileExists(Path))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));

            return;
        }

        var file = new ConfigFile();
        file.SetValue(Section, ModeKey, (int)PanMode);
        file.Save(Path);
    }
}
