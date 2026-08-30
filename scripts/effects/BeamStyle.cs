using Godot;

/// <summary>
/// Вид луча: толщина, цвет, свечение, пульсация, материал. Один ресурс на все лучи одного
/// рода — строительный луч у коммандера, фабрикатора и башни-сборщика есть одно и то же
/// изображение, и правиться оно должно в одном месте.
///
/// ПОЧЕМУ РЕСУРС, А НЕ ПОЛЯ УЗЛА. Поля пришлось бы повторять в каждой сцене модели, и три
/// строителя разошлись бы видом при первой же правке. Ссылка на ресурс лежит в узле
/// <see cref="BeamVisual"/> той же надобности ради, ради какой ссылка на декаль копоти
/// лежит в <see cref="ScorchStamp"/>: узел говорит, ЧТО показать, ресурс — КАК.
///
/// ЧИСЛА ЗАДАНЫ В ПИКСЕЛЯХ МИРА и потому растут вместе с приближением камеры. Совсем тонким
/// луч на дальнем плане всё же не становится: толщина проведена через
/// <see cref="WidthMode.MinScreen"/>, то есть не меньше своей величины в экранных пикселях.
/// </summary>
[Tool]
[GlobalClass]
public partial class BeamStyle : Resource
{
    /// <summary>Цвет луча. Ядро осветляется от него к белому, свечение — приглушается.</summary>
    [Export] public Color Color { get; set; } = new(0.45f, 0.9f, 1f);

    /// <summary>Толщина яркого ядра, пикселей мира.</summary>
    [Export(PropertyHint.Range, "0.5,32,0.5")] public float CoreWidth { get; set; } = 3f;

    /// <summary>Толщина внешнего свечения, пикселей мира. Ноль убирает его вовсе.</summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float GlowWidth { get; set; } = 10f;

    /// <summary>Насколько свечение прозрачнее ядра, долей его непрозрачности.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float GlowAlpha { get; set; } = 0.22f;

    /// <summary>Радиус вспышки у среза инструмента, пикселей мира.</summary>
    [Export(PropertyHint.Range, "0,32,0.5")] public float MuzzleRadius { get; set; } = 4f;

    /// <summary>Радиус вспышки в точке попадания, пикселей мира.</summary>
    [Export(PropertyHint.Range, "0,48,0.5")] public float ImpactRadius { get; set; } = 7f;

    /// <summary>Частота пульсации, колебаний в секунду.</summary>
    [Export(PropertyHint.Range, "0,20,0.1")] public float PulseSpeed { get; set; } = 7f;

    /// <summary>Размах пульсации, долей толщины. Ноль даёт неподвижный луч.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float PulseDepth { get; set; } = 0.22f;

    /// <summary>
    /// За сколько секунд луч разгорается и гаснет.
    ///
    /// ЗАЧЕМ ЭТО ВООБЩЕ ЕСТЬ. Работа прерывается на кадр всякий раз, когда исполнитель
    /// переподключается к соседнему каркасу либо когда постройка достроена, а следующая
    /// ещё не назначена. Луч, показываемый строго по признаку «работа идёт», на таких
    /// разрывах мигал бы.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float RiseTime { get; set; } = 0.05f;

    /// <inheritdoc cref="RiseTime"/>
    [Export(PropertyHint.Range, "0,1,0.01")] public float FadeTime { get; set; } = 0.12f;

    /// <summary>
    /// Текстура тела луча. Не назначена — тело рисуется двумя полосами, ядром и свечением;
    /// назначенная растягивается вдоль луча и повторяется, а высота её приходится
    /// на <see cref="GlowWidth"/>.
    /// </summary>
    [Export] public Texture2D Texture { get; set; }

    /// <summary>Скорость бега текстуры вдоль луча, пикселей мира в секунду.</summary>
    [Export] public float TextureScroll { get; set; } = 90f;

    /// <summary>
    /// Вещество, которым рисуется луч. Обычно это <see cref="CanvasItemMaterial"/>
    /// со сложением цветов: свечение обязано складываться с тем, что под ним, иначе
    /// два скрестившихся луча выглядят наложенными плёнками.
    /// </summary>
    [Export] public Material Material { get; set; }
}
