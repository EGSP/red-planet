using Godot;

/// <summary>
/// Настройка графики: свод подсистем, каждая из которых отвечает за один приём подачи.
/// Сейчас их две — затенение частей модели и вид каркаса стройки.
///
/// ОТКУДА БЕРЁТСЯ ДЕЙСТВУЮЩИЙ СВОД. Основной источник — поле
/// <see cref="GameManager.GraphicsTuning"/>: сессия объявляет свои настройки явно, как она
/// делает это для мира и навигации. Если поле не заполнено или сессии нет вовсе, свод
/// загружается по фиксированному пути.
///
/// ПОЧЕМУ ПУТЬ ВООБЩЕ ОСТАЁТСЯ. Затенение и каркас нужны в местах, до которых сцена сессии
/// не дотягивается: узел затенения лежит в сцене модели, а предпросмотр застройки работает
/// в редакторе, где менеджера нет. Проводить настройки в каждое такое место отдельно значило
/// бы заводить поле на каждом узле, который что-нибудь рисует.
///
/// ПОЧЕМУ ЕСТЬ ЗАПАСНОЙ ЭКЗЕМПЛЯР. Если ресурс не нашёлся и по пути, подставляются умолчания
/// полей. Отсутствие файла настроек не должно оставлять мир без картинки; в журнал при этом
/// идёт предупреждение.
/// </summary>
[Tool]
[GlobalClass]
public partial class GraphicsSettings : Resource
{
    private const string Path = "res://resources/tuning/graphics/graphics.tres";

    /// <summary>Затенение частей модели: тени под корпусом и затемнение по кайме.</summary>
    [Export] public ShadingSettings Shading = new();

    /// <summary>Вид каркаса строящейся постройки.</summary>
    [Export] public ConstructionSettings Construction = new();

    private static GraphicsSettings _active;

    /// <summary>
    /// Действующий свод настроек. Пока сессия не объявила свой через <see cref="Use"/>,
    /// загружается по фиксированному пути при первом обращении.
    /// </summary>
    public static GraphicsSettings Active
    {
        get
        {
            if (_active != null)
                return _active;

            var loaded = ResourceLoader.Exists(Path) ? GD.Load<GraphicsSettings>(Path) : null;

            if (loaded == null)
            {
                GD.PushWarning($"[GraphicsSettings] нет ресурса {Path}, взяты умолчания");
                loaded = new GraphicsSettings();
            }

            _active = Complete(loaded);
            return _active;
        }
    }

    /// <summary>
    /// Объявить свод действующим. Вызывается менеджером сессии из поля
    /// <see cref="GameManager.GraphicsTuning"/>; пустая ссылка возвращает загрузку по пути,
    /// поэтому незаполненное поле в сцене ничего не ломает.
    /// </summary>
    public static void Use(GraphicsSettings settings) =>
        _active = settings == null ? null : Complete(settings);

    /// <summary>
    /// Восполнить незаполненные подсистемы умолчаниями. Свод, собранный в инспекторе с нуля,
    /// приходит с пустыми ссылками, и обращаться к ним при отрисовке нельзя.
    /// </summary>
    private static GraphicsSettings Complete(GraphicsSettings settings)
    {
        settings.Shading ??= new ShadingSettings();
        settings.Construction ??= new ConstructionSettings();
        return settings;
    }

    /// <summary>Настройки затенения действующего свода.</summary>
    public static ShadingSettings Shade => Active.Shading;

    /// <summary>Настройки вида каркаса действующего свода.</summary>
    public static ConstructionSettings Building => Active.Construction;
}
