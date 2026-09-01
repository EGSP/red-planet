using Godot;

/// <summary>
/// Настройка отрисовки служебной графики множественными сетками: материалы форм и набор
/// значков приказов. Подсистема графики; отрисовкой заведует <see cref="ShapeMesh"/>.
///
/// ПОЧЕМУ МАТЕРИАЛОВ ЧЕТЫРЕ. Материал у множественной сетки один на все её экземпляры,
/// а форма фигуры считается в шейдере, и шейдеры у отрезка, круга, прямоугольника и значка
/// разные. Отсюда следует, что каждой форме принадлежит своя сетка со своим материалом,
/// и общий их список живёт здесь.
///
/// ЗАЧЕМ ЗДЕСЬ НАБОР ЗНАЧКОВ. Картинки значков лежат отдельными файлами, а сетке нужен
/// один массив изображений: смена текстуры принадлежит узлу и разбила бы отрисовку значков
/// на отдельные вызовы. Массив собирается при первом обращении — см. <see cref="Icons"/>, —
/// и до сетки доходит номер слоя, а не картинка.
///
/// ЗАЧЕМ РЕФЕРЕНСНЫЙ РАЗМЕР. Слои массива изображений обязаны совпадать по размеру,
/// тогда как исходные файлы совпадать не обязаны. Поэтому каждая картинка при сборке
/// приводится к стороне <see cref="IconSourceSide"/>. Величина эта принадлежит хранению
/// картинок, а не показу: на экране сторона значка задаётся отдельно
/// через <see cref="IconScreenSide"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class ShapeMeshSettings : Resource
{
    private const string LineMaterialPath = "res://resources/materials/shape_line.tres";
    private const string DiscMaterialPath = "res://resources/materials/shape_disc.tres";
    private const string FrameMaterialPath = "res://resources/materials/shape_frame.tres";
    private const string IconMaterialPath = "res://resources/materials/shape_icon.tres";

    private const string IconRoot = "res://assets/sprites/ui/commands/";

    [ExportGroup("Материалы форм")]

    /// <summary>Материал отрезков. Пустое поле означает материал по умолчанию.</summary>
    [Export] public Material LineMaterial;

    /// <summary>Материал кругов и колец.</summary>
    [Export] public Material DiscMaterial;

    /// <summary>Материал прямоугольников.</summary>
    [Export] public Material FrameMaterial;

    /// <summary>
    /// Материал значков. В него при сборке набора записывается параметр <c>icons</c>,
    /// поэтому отдельного материала на каждый набор заводить не требуется.
    /// </summary>
    [Export] public Material IconMaterial;

    [ExportGroup("Значки приказов")]

    /// <summary>Рисовать ли значок вида приказа в точке приказа.</summary>
    [Export] public bool IconsEnabled = true;

    /// <summary>
    /// Сторона слоя массива изображений, пиксели. Все картинки приводятся к ней при сборке,
    /// поскольку слои массива обязаны совпадать по размеру. Степень двойки взята ради
    /// уровней детализации: на отдалённой камере значок сжимается сильно.
    /// </summary>
    [Export(PropertyHint.Range, "8,256,1")] public int IconSourceSide = 64;

    /// <summary>
    /// Сторона значка на экране, пиксели. Задана экранной, а не мировой величиной: значок
    /// заменяет собой подпись к приказу, и читаться он должен одинаково на любом приближении.
    /// </summary>
    [Export(PropertyHint.Range, "4,64,0.5")] public float IconScreenSide = 22f;

    [ExportGroup("Картинки по видам приказа")]

    /// <summary>
    /// Картинка вида приказа. Пустое поле означает картинку по умолчанию из набора
    /// <c>assets/sprites/ui/commands</c>, а не отсутствие значка.
    /// </summary>
    [Export] public Texture2D Move;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Build;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Attack;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D AttackMove;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Repair;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Follow;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Delete;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D AttackArea;

    /// <inheritdoc cref="Move"/>
    [Export] public Texture2D Patrol;

    /// <summary>Материал отрезков с подстановкой умолчания.</summary>
    public Material LineCoat => _line ??= LineMaterial ?? Fallback<Material>(LineMaterialPath);

    /// <summary>Материал кругов и колец с подстановкой умолчания.</summary>
    public Material DiscCoat => _disc ??= DiscMaterial ?? Fallback<Material>(DiscMaterialPath);

    /// <summary>Материал прямоугольников с подстановкой умолчания.</summary>
    public Material FrameCoat => _frame ??= FrameMaterial ?? Fallback<Material>(FrameMaterialPath);

    /// <summary>Материал значков с подстановкой умолчания.</summary>
    public Material IconCoat => _icon ??= IconMaterial ?? Fallback<Material>(IconMaterialPath);

    /// <summary>Номер слоя значка для вида приказа. Совпадает с номером самого вида.</summary>
    public static int Layer(OrderKind kind) => (int)kind;

    /// <summary>
    /// Массив изображений значков, собранный из отдельных картинок. Сборка идёт один раз
    /// на свод настроек: она стоит чтения файлов и масштабирования, а состав набора
    /// в течение сессии не меняется.
    /// </summary>
    public Texture2DArray Icons => _icons ??= BuildIcons();

    private Material _line;
    private Material _disc;
    private Material _frame;
    private Material _icon;
    private Texture2DArray _icons;

    /// <summary>
    /// Картинка вида приказа с подстановкой умолчания. Соответствие видов картинкам задано
    /// здесь, а не в отрисовщике: набор принадлежит настройке.
    /// </summary>
    private Texture2D Picture(OrderKind kind)
    {
        var chosen = kind switch
        {
            OrderKind.Move => Move,
            OrderKind.Build => Build,
            OrderKind.Attack => Attack,
            OrderKind.AttackMove => AttackMove,
            OrderKind.Repair => Repair,
            OrderKind.Follow => Follow,
            OrderKind.Delete => Delete,
            OrderKind.AttackArea => AttackArea,
            OrderKind.Patrol => Patrol,
            _ => null,
        };

        return chosen ?? Fallback<Texture2D>(IconRoot + DefaultFile(kind));
    }

    /// <summary>
    /// Файл картинки по умолчанию. Стройка берёт значок помощи, сопровождение — значок
    /// применения, снос — значок переработки: своих картинок у них в наборе нет,
    /// а смысл ближе всего к этим.
    /// </summary>
    private static string DefaultFile(OrderKind kind) => kind switch
    {
        OrderKind.Move => "icons_command_move.png",
        OrderKind.Build => "icons_command_assist.png",
        OrderKind.Attack => "icons_command_attack.png",
        OrderKind.AttackMove => "icons_command_attack.png",
        OrderKind.Repair => "icons_command_repair.png",
        OrderKind.Follow => "icons_command_use.png",
        OrderKind.Delete => "icons_command_reclaim.png",
        OrderKind.AttackArea => "icons_command_attack.png",
        OrderKind.Patrol => "icons_command_patrol.png",
        _ => "icons_command_stop.png",
    };

    /// <summary>
    /// Собрать массив изображений: слой на вид приказа в порядке перечисления
    /// <see cref="OrderKind"/>. Отсюда следует, что номер слоя равен номеру вида,
    /// и отдельной таблицы соответствия не требуется.
    /// </summary>
    private Texture2DArray BuildIcons()
    {
        int side = Mathf.Clamp(IconSourceSide, 8, 512);
        var images = new Godot.Collections.Array<Image>();

        foreach (OrderKind kind in System.Enum.GetValues<OrderKind>())
            images.Add(Slice(Picture(kind), side));

        var array = new Texture2DArray();
        array.CreateFromImages(images);

        return array;
    }

    /// <summary>
    /// Привести картинку к слою массива: общий формат, общая сторона, уровни детализации.
    /// Отсутствующая картинка даёт пустой слой — значка не будет, но остальные слои
    /// не должны из-за этого сдвинуться.
    /// </summary>
    private static Image Slice(Texture2D texture, int side)
    {
        var image = texture?.GetImage();

        if (image == null)
        {
            image = Image.CreateEmpty(side, side, true, Image.Format.Rgba8);
            image.Fill(new Color(1f, 1f, 1f, 0f));
            return image;
        }

        image = (Image)image.Duplicate();

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        if (image.GetWidth() != side || image.GetHeight() != side)
            image.Resize(side, side, Image.Interpolation.Lanczos);

        if (!image.HasMipmaps())
            image.GenerateMipmaps();

        return image;
    }

    private static T Fallback<T>(string path) where T : Resource =>
        ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
}
