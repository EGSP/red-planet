using Godot;

/// <summary>Откуда берётся температура.</summary>
public enum TemperatureSource
{
    /// <summary>От центра карты к краю: в середине холодно, у границы арены жарко.</summary>
    Radial = 0,

    /// <summary>Вдоль оси под заданным углом — подобие широты.</summary>
    Linear = 1,

    /// <summary>Из собственного поля шума: пятна вместо правильного перехода.</summary>
    Noise = 2,
}

/// <summary>
/// Местность целиком: базовый слой, карты температуры и высоты, набор биомов с декалями.
/// Один ресурс — одна разновидность поверхности («пустошь», «лава», «земля»).
///
/// ПОЧЕМУ ЧЕТЫРЕ ПОКРЫТИЯ, А НЕ СКОЛЬКО УГОДНО. Шейдер разбирает покрытия развёрнутым
/// кодом, поскольку выбирать текстуру по вычисляемому номеру в языке шейдеров нельзя.
/// Четырёх достаточно: при большем числе переходы становятся неразличимы, а стоимость
/// выборок растёт линейно.
///
/// ЗЕРНО ЗДЕСЬ НЕ ХРАНИТСЯ. Оно принадлежит партии и берётся из
/// <see cref="WorldSettings.Seed"/>: описание местности задаёт правила, а зерно —
/// их конкретное разрешение. Иначе две партии на одной местности были бы неотличимы.
/// </summary>
[Tool]
[GlobalClass]
public partial class SurfaceSettings : Resource
{
    /// <summary>Наибольшее число покрытий базового слоя, которое разбирает шейдер.</summary>
    public const int MaxTiles = 4;

    private TemperatureSource _temperature = TemperatureSource.Radial;

    /// <summary>Имя для чтения в инспекторе.</summary>
    [Export] public string Id = "";

    // ── Базовый слой ──────────────────────────────────────────────────────────────

    [ExportGroup("Базовый слой")]

    /// <summary>Поле, по которому базовый слой делится между покрытиями.</summary>
    [Export] public NoiseSettings BaseNoise;

    /// <summary>Покрытия по порядку. Первое служит подложкой там, где не проявилось ни одно.</summary>
    [Export] public SurfaceTile[] Tiles = System.Array.Empty<SurfaceTile>();

    /// <summary>
    /// Гексагональная укладка: текстура читается трижды со случайным смещением по узлам
    /// треугольной решётки и смешивается по барицентрическим весам. Устраняет видимый
    /// повтор ценой трёх выборок вместо одной, поэтому вынесена в переключатель.
    /// </summary>
    [Export] public bool HexTiling = true;

    /// <summary>
    /// Резкость переходов между покрытиями: веса возводятся в эту степень и
    /// перенормируются. Единица оставляет плавное смешивание, большие значения сужают
    /// переходную полосу, отчего стыки перестают выглядеть размытыми.
    /// </summary>
    [Export(PropertyHint.Range, "1,16,0.5")] public float Sharpness = 4f;

    /// <summary>Общий оттенок поверхности поверх всех покрытий.</summary>
    [Export] public Color Ambient = Colors.White;

    // ── Температура ───────────────────────────────────────────────────────────────

    [ExportGroup("Температура")]

    /// <summary>Откуда берётся температура. При смене список настроек перестраивается.</summary>
    [Export]
    public TemperatureSource Temperature
    {
        get => _temperature;
        set
        {
            _temperature = value;
            NotifyPropertyListChanged();
        }
    }

    /// <summary>Ось перехода в градусах при линейном источнике.</summary>
    [Export(PropertyHint.Range, "0,360,1")] public float TemperatureAngleDegrees;

    /// <summary>Поле температуры при шумовом источнике.</summary>
    [Export] public NoiseSettings TemperatureNoise;

    /// <summary>
    /// Подмешивание шума к правильному переходу. Без него граница биома у радиального
    /// источника есть в точности окружность, что читается как рисованная разметка.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")] public float TemperatureJitter = 0.08f;

    /// <summary>Поле, которым подмешивается неровность. Не назначено — берётся базовое.</summary>
    [Export] public NoiseSettings TemperatureJitterNoise;

    // ── Высота ────────────────────────────────────────────────────────────────────

    [ExportGroup("Высота")]

    /// <summary>
    /// Поле высот. На вид поверхности само по себе не влияет и рельефа не создаёт:
    /// служит дополнительным условием отбора покрытий и декалей, у которых сужен
    /// соответствующий отрезок. Не назначено — условия по высоте не проверяются.
    /// </summary>
    [Export] public NoiseSettings HeightNoise;

    // ── Биомы ─────────────────────────────────────────────────────────────────────

    [ExportGroup("Биомы")]

    /// <summary>Биомы местности. Пересечение отрезков температуры допустимо.</summary>
    [Export] public SurfaceBiome[] Biomes = System.Array.Empty<SurfaceBiome>();

    /// <summary>Скрыть настройки температуры, не относящиеся к выбранному источнику.</summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string name = property["name"].AsString();

        bool hide = name switch
        {
            nameof(TemperatureAngleDegrees) => Temperature != TemperatureSource.Linear,
            nameof(TemperatureNoise) => Temperature != TemperatureSource.Noise,
            nameof(TemperatureJitter) or nameof(TemperatureJitterNoise) =>
                Temperature == TemperatureSource.Noise,
            _ => false,
        };

        if (hide)
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }
}
