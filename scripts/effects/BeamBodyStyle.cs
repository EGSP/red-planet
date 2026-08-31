using Godot;

/// <summary>
/// Вид тела луча — той полосы, что тянется от инструмента к цели. Часть <see cref="BeamStyle"/>;
/// отрисовкой заведует <see cref="BeamPainter"/>.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ РЕСУРС НА КАЖДУЮ ЧАСТЬ. Тело, вспышка у среза и вспышка в точке
/// попадания рисуются разными шейдерами: у тела рисунок повторяется вдоль и бежит, у вспышки
/// он круговой и вращается. Материал же в множественной сетке один на все её экземпляры,
/// поэтому части не помещаются в общую сетку и получают по своей — а вместе с сеткой
/// и свой свод настроек.
///
/// ЧИСЛА ЗАДАНЫ В ПИКСЕЛЯХ МИРА и растут вместе с приближением камеры. Экранной поправки
/// у луча нет намеренно: он принадлежит миру, а не тому, кто на мир смотрит.
/// </summary>
[Tool]
[GlobalClass]
public partial class BeamBodyStyle : Resource
{
    /// <summary>Материал по умолчанию, если поле <see cref="Material"/> пустое.</summary>
    private const string DefaultMaterialPath = "res://resources/materials/beam_body.tres";

    [ExportGroup("Изображение")]

    /// <summary>Рисовать ли тело луча.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>
    /// Текстура тела. Растягивается по толщине луча и повторяется вдоль него с шагом
    /// <see cref="TileLength"/>. Пустое поле оставляет ровную полосу цвета.
    /// </summary>
    [Export] public Texture2D Texture { get; set; }

    /// <summary>
    /// Материал тела — один на все лучи этого вида. Пустое поле означает материал
    /// по умолчанию: сложение цветов и смягчение краёв полосы.
    /// </summary>
    [Export] public Material Material { get; set; }

    /// <summary>Цвет полосы вместе с её непрозрачностью.</summary>
    [Export] public Color Color { get; set; } = new(0.28f, 0.92f, 0.38f);

    [ExportGroup("Размер")]

    /// <summary>Толщина полосы, пикселей мира.</summary>
    [Export(PropertyHint.Range, "0.5,64,0.5")] public float Width { get; set; } = 13f;

    /// <summary>
    /// Длина одного повтора рисунка вдоль луча, пикселей мира. Чем меньше, тем чаще
    /// повторяется текстура; ноль растягивает её на всю длину без повторов.
    /// </summary>
    [Export(PropertyHint.Range, "0,512,1")] public float TileLength { get; set; } = 64f;

    [ExportGroup("Движение")]

    /// <summary>Скорость бега рисунка вдоль луча, пикселей мира в секунду.</summary>
    [Export(PropertyHint.Range, "-512,512,1")] public float ScrollSpeed { get; set; } = 90f;

    /// <summary>Частота пульсации толщины, колебаний в секунду.</summary>
    [Export(PropertyHint.Range, "0,20,0.1")] public float PulseSpeed { get; set; } = 6f;

    /// <summary>Размах пульсации, долей толщины. Ноль даёт неподвижную полосу.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float PulseDepth { get; set; } = 0.25f;

    /// <summary>Материал с подстановкой умолчания.</summary>
    public Material Coat => _material ??= Material ?? BeamStyle.Fallback<Material>(DefaultMaterialPath);

    private Material _material;
}
