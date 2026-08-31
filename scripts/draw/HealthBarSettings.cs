using Godot;

/// <summary>
/// Настройка полосы прочности — той, что висит над повреждённой сущностью.
/// Подсистема графики; отрисовкой заведует <see cref="HealthBarSystem"/>.
///
/// ПОЧЕМУ ЗДЕСЬ НЕТ НИ ЦВЕТОВ, НИ ШИРИНЫ. Цвета заливки и фона вместе с порогами прочности
/// заданы материалом (<see cref="Material"/>), общим на все полосы, и повторять их полями
/// настройки значило бы завести два источника одного значения. Ширина же и подъём полосы
/// принадлежат сущности, а не своду: они выводятся из её габарита — см.
/// <see cref="IHealthMarked"/>.
///
/// Высота, наоборот, у всех полос одна и от габарита не зависит: полоса читается по длине
/// заполненной части, и у крупной постройки она не должна становиться толще.
/// </summary>
[Tool]
[GlobalClass]
public partial class HealthBarSettings : Resource
{
    /// <summary>Материал по умолчанию, если поле <see cref="Material"/> пустое.</summary>
    private const string DefaultMaterialPath = "res://resources/materials/health_bar.tres";

    /// <summary>Рисовать ли полосы прочности.</summary>
    [Export] public bool Enabled = true;

    /// <summary>
    /// Материал полос — один на все. Им задаются цвета, пороги прочности и наложение
    /// поверх полосы. Пустое поле означает материал по умолчанию.
    /// </summary>
    [Export] public Material Material;

    /// <summary>Высота полосы в мировых пикселях, одна на все полосы.</summary>
    [Export(PropertyHint.Range, "1,24,0.5")] public float Height = 5f;

    /// <summary>
    /// Насколько полная прочность считается полной. Полоса целой сущности не рисуется:
    /// в мире и без того тесно, а сущность без повреждений в подсказке не нуждается.
    /// </summary>
    [Export(PropertyHint.Range, "0.9,1,0.001")] public float FullEnough = 0.999f;

    /// <summary>Материал полос с подстановкой умолчания.</summary>
    public Material Coat => _material ??= Material ?? Fallback<Material>(DefaultMaterialPath);

    private Material _material;

    private static T Fallback<T>(string path) where T : Resource =>
        ResourceLoader.Exists(path) ? GD.Load<T>(path) : null;
}
