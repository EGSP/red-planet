using Godot;

/// <summary>
/// Настройка метки выделения — той картинки, которая лежит над выделенной сущностью.
/// Подсистема графики; отрисовкой заведует <see cref="SelectionOverlay"/>.
///
/// ПОЧЕМУ ТЕКСТУРА, А НЕ ОКРУЖНОСТЬ. Кольцо строилось разбиением окружности на отрезки,
/// и на каждую выделенную сущность приходился отдельный набор вершин. Метки же различаются
/// только положением и размером, поэтому все они лежат в одной множественной сетке:
/// геометрия у неё одна на всех, а кадровая работа сводится к записи преобразований.
///
/// ЦВЕТА ЗДЕСЬ НЕТ НАМЕРЕННО. Цвет задан материалом (<see cref="Material"/>), общим на все
/// метки, и плотность живёт в альфе того же цвета; дублировать их полями настройки значило
/// бы завести два источника одного и того же значения.
///
/// ЗАЧЕМ ЗДЕСЬ ПОПРАВКИ РАЗМЕРА. Изображение занимает своё поле по-разному: у одного
/// рисунок доходит до краёв, у другого вокруг него остаётся пустая кайма. Поэтому сторона
/// метки считается не прямо по радиусу сущности, а с двумя поправками — каймой изображения
/// (<see cref="SourceImagePadding"/>) и общим запасом размера (<see cref="ScaleOffset"/>).
/// Первая принадлежит изображению и меняется вместе с ним, второй задаёт зазор между
/// корпусом и меткой. Обе заданы долями, а не пикселями: зазор в пикселях у крупной
/// постройки и у мелкого дрона выглядит по-разному, тогда как доля радиуса — одинаково.
/// </summary>
[Tool]
[GlobalClass]
public partial class SelectionSettings : Resource
{
    /// <summary>Изображение метки по умолчанию, если поле <see cref="Texture"/> пустое.</summary>
    private const string DefaultTexturePath = "res://assets/sprites/ui/commands/hover_friendly.png";

    /// <summary>Материал по умолчанию, если поле <see cref="Material"/> пустое.</summary>
    private const string DefaultMaterialPath = "res://resources/materials/selection_units.tres";

    [ExportGroup("Отображение")]

    /// <summary>Рисовать ли метки выделения.</summary>
    [Export] public bool Enabled = true;

    /// <summary>
    /// Изображение метки. Пустое поле означает изображение по умолчанию, а не отсутствие
    /// метки: свод, собранный в инспекторе с нуля, не должен оставлять выделение без вида.
    /// </summary>
    [Export] public Texture2D Texture;

    /// <summary>
    /// Материал меток — один на все. Им же задаётся цвет вместе с плотностью. Пустое поле
    /// означает материал по умолчанию.
    /// </summary>
    [Export] public Material Material;

    [ExportGroup("Размер")]

    /// <summary>
    /// Пустая кайма изображения по каждому краю, доля его стороны. Ноль означает, что
    /// рисунок доходит до краёв; большие значения увеличивают метку так, чтобы сам рисунок
    /// сел по радиусу сущности.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.45,0.005")] public float SourceImagePadding = 0.1f;

    /// <summary>
    /// Множитель радиуса сущности. Значения больше единицы отодвигают метку от корпуса,
    /// меньше — вводят её внутрь габарита.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,4,0.01")] public float ScaleOffset = 1.35f;

    /// <summary>
    /// Сторона метки для сущности данного радиуса, мировые пиксели. Считается здесь,
    /// а не в отрисовщике, поскольку обе поправки принадлежат настройке.
    /// </summary>
    public float SideFor(float radius)
    {
        // Доля стороны изображения, занятая самим рисунком: кайма снята с обоих краёв
        float fill = Mathf.Clamp(1f - SourceImagePadding * 2f, 0.01f, 1f);

        return radius * 2f * Mathf.Max(ScaleOffset, 0.01f) / fill;
    }

    /// <summary>Изображение метки с подстановкой умолчания.</summary>
    public Texture2D Sprite => _sprite ??= Texture ?? Fallback<Texture2D>(DefaultTexturePath);

    /// <summary>Материал меток с подстановкой умолчания.</summary>
    public Material Coat => _material ??= Material ?? Fallback<Material>(DefaultMaterialPath);

    private Texture2D _sprite;
    private Material _material;

    private static T Fallback<T>(string path) where T : Resource =>
        ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
}
