using Godot;

/// <summary>
/// Вид вспышки на конце луча — у среза инструмента либо в точке попадания. Часть
/// <see cref="BeamStyle"/>; отрисовкой заведует <see cref="BeamPainter"/>.
///
/// Обе вспышки описываются этим сводом и рисуются общим шейдером: отличаются они размером,
/// цветом и текстурой, а не приёмом отрисовки. Своды при этом разные, поскольку у среза
/// и у точки попадания вид обычно расходится, и сетки тоже разные: материал у сетки один.
/// </summary>
[Tool]
[GlobalClass]
public partial class BeamCapStyle : Resource
{
    /// <summary>Материал по умолчанию, если поле <see cref="Material"/> пустое.</summary>
    private const string DefaultMaterialPath = "res://resources/materials/beam_cap.tres";

    [ExportGroup("Изображение")]

    /// <summary>Рисовать ли вспышку.</summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>
    /// Текстура вспышки. Занимает квадрат со стороной в два радиуса. Пустое поле оставляет
    /// круглое пятно, построенное шейдером из расстояния до середины.
    /// </summary>
    [Export] public Texture2D Texture { get; set; }

    /// <summary>Материал вспышек — один на все вспышки этого рода. Пустое поле — умолчание.</summary>
    [Export] public Material Material { get; set; }

    /// <summary>Цвет вспышки вместе с её непрозрачностью.</summary>
    [Export] public Color Color { get; set; } = new(0.7f, 1f, 0.75f);

    [ExportGroup("Размер")]

    /// <summary>Радиус вспышки, пикселей мира. Ноль убирает её вовсе.</summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float Radius { get; set; } = 5f;

    [ExportGroup("Движение")]

    /// <summary>Частота пульсации радиуса, колебаний в секунду.</summary>
    [Export(PropertyHint.Range, "0,20,0.1")] public float PulseSpeed { get; set; } = 6f;

    /// <summary>Размах пульсации, долей радиуса.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float PulseDepth { get; set; } = 0.25f;

    /// <summary>Скорость вращения рисунка, оборотов в секунду. Ноль оставляет его неподвижным.</summary>
    [Export(PropertyHint.Range, "-4,4,0.05")] public float SpinSpeed { get; set; }

    /// <summary>Материал с подстановкой умолчания.</summary>
    public Material Coat => _material ??= Material ?? BeamStyle.Fallback<Material>(DefaultMaterialPath);

    private Material _material;
}
