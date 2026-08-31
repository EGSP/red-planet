using System;
using Godot;

/// <summary>
/// Признак, по которому части эффектов сливаются в одно общее поле частиц
/// (<see cref="ParticleField"/>).
///
/// ЗАЧЕМ СЛИЯНИЕ. Каждый узел <see cref="GpuParticles2D"/> обходится движку в отдельный
/// вычислительный вызов, отдельный набор параметров и отдельный вызов отрисовки,
/// разрывающий пакетирование двумерной сцены. Цена эта постоянна и от числа частиц почти
/// не зависит, поэтому узел на одну частицу стоит почти столько же, сколько узел на тысячу.
/// В бою таких узлов набирались сотни — пыль под каждой машиной, вспышка при каждом стволе,
/// набор готовых взрывов и попаданий, — и складывались в десятки миллисекунд кадра.
///
/// ЧТО ВХОДИТ В ПРИЗНАК. Ровно то, что описывает вид и правила поведения частицы: материал
/// обработки, изображение, материал холста, срок жизни, фильтрация, уровень отрисовки
/// и слой мира. Две части эффектов, совпавшие по всем этим полям, различаются лишь местом
/// и мигом рождения, а значит могут жить в общем поле.
///
/// ПОЧЕМУ МНОЖИТЕЛЬ РАЗМЕРА ТОЖЕ ВХОДИТ. Размер частицы вычисляется материалом обработки
/// заново каждый кадр и внешним преобразованием не задаётся, поэтому множитель размера
/// эффекта, объявленный в модели, нельзя передать вместе с вбросом. Вместо этого поле
/// с множителем, отличным от натурального, получает собственную копию материала
/// с домноженными размером, скоростью и разбросом. Множитель округляется до сотых долей
/// с шагом <see cref="Step"/>: близкие величины сливаются в одно поле, и число копий
/// остаётся малым.
///
/// СРАВНЕНИЕ РЕСУРСОВ ИДЁТ ПО ССЫЛКЕ, а не по содержанию. Вложенные ресурсы сцены общие
/// у всех её экземпляров, поэтому у сотни машин одного вида материал пыли — один объект,
/// и сравнение по ссылке сливает их правильно. Два разных материала с одинаковыми полями
/// в одно поле не сольются, но это лишь упущенная выгода, а не ошибка.
/// </summary>
public readonly struct ParticleKey : IEquatable<ParticleKey>
{
    /// <summary>Множитель размера, соответствующий натуральной величине.</summary>
    public const int Unit = 100;

    /// <summary>Шаг округления множителя размера в сотых долях.</summary>
    public const int Step = 5;

    public readonly ParticleProcessMaterial Process;
    public readonly Texture2D Texture;
    public readonly Material Canvas;
    public readonly float Lifetime;
    public readonly CanvasItem.TextureFilterEnum Filter;
    public readonly int ZIndex;
    public readonly WorldLayer Layer;

    /// <summary>Множитель размера в сотых долях; <see cref="Unit"/> означает натуральный.</summary>
    public readonly int Grade;

    public ParticleKey(GpuParticles2D node, WorldLayer layer)
        : this(node.ProcessMaterial as ParticleProcessMaterial, node.Texture, node.Material,
            (float)node.Lifetime, node.TextureFilter, node.ZIndex, layer, Unit)
    {
    }

    private ParticleKey(ParticleProcessMaterial process, Texture2D texture, Material canvas,
        float lifetime, CanvasItem.TextureFilterEnum filter, int zIndex, WorldLayer layer,
        int grade)
    {
        Process = process;
        Texture = texture;
        Canvas = canvas;
        Lifetime = lifetime;
        Filter = filter;
        ZIndex = zIndex;
        Layer = layer;
        Grade = grade;
    }

    /// <summary>Множитель размера дробью.</summary>
    public float Size => Grade / (float)Unit;

    /// <summary>Натуральный ли размер, то есть можно ли брать материал как есть.</summary>
    public bool Natural => Grade == Unit;

    /// <summary>Тот же признак с иным множителем размера, округлённым до шага.</summary>
    public ParticleKey Resized(float size) =>
        new(Process, Texture, Canvas, Lifetime, Filter, ZIndex, Layer, Quantize(size));

    /// <summary>
    /// Округлить множитель размера до шага. Ноль и отрицательные величины подтягиваются
    /// к одному шагу: поле с нулевым размером не показало бы ничего, а вызвавший ошибся бы
    /// молча.
    /// </summary>
    public static int Quantize(float size)
    {
        int grade = Mathf.RoundToInt(size * Unit / Step) * Step;
        return Mathf.Max(grade, Step);
    }

    public bool Equals(ParticleKey other) =>
        ReferenceEquals(Process, other.Process)
        && ReferenceEquals(Texture, other.Texture)
        && ReferenceEquals(Canvas, other.Canvas)
        && Lifetime.Equals(other.Lifetime)
        && Filter == other.Filter
        && ZIndex == other.ZIndex
        && Layer == other.Layer
        && Grade == other.Grade;

    public override bool Equals(object obj) => obj is ParticleKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Process?.GetInstanceId() ?? 0UL);
        hash.Add(Texture?.GetInstanceId() ?? 0UL);
        hash.Add(Canvas?.GetInstanceId() ?? 0UL);
        hash.Add(Lifetime);
        hash.Add((int)Filter);
        hash.Add(ZIndex);
        hash.Add((int)Layer);
        hash.Add(Grade);

        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"{Texture?.ResourcePath ?? "—"} × {Size:0.00} на {Layer}";
}
