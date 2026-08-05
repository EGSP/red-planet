using Godot;

/// <summary>Вид шума. Определяет, какие настройки имеют смысл, а какие скрыты.</summary>
public enum NoiseKind
{
    /// <summary>Гладкий шум общего назначения. Годится почти всюду.</summary>
    Simplex = 0,

    /// <summary>Классический Перлин: те же пятна, но с более выраженной решёткой.</summary>
    Perlin = 1,

    /// <summary>Значения в узлах решётки. Даёт угловатые области, годен для ярусов.</summary>
    Value = 2,

    /// <summary>Клеточный шум Ворного. Даёт ячейки и прожилки, а не пятна.</summary>
    Cellular = 3,
}

/// <summary>
/// Поле шума: частота, контрастность и настройки выбранного вида.
///
/// ПОЧЕМУ КОНТРАСТНОСТЬ ОТДЕЛЕНА ОТ ЧАСТОТЫ. Это два независимых вопроса: частота решает,
/// насколько крупны пятна, контрастность — какая доля карты попадёт в заданный правилом
/// диапазон. Слитые в один параметр, они не позволяют изменить густоту покрытия, не
/// перестроив рисунок целиком. В Planetary Annihilation ту же роль исполняет параметр,
/// названный там <c>zoom</c>, что вводит в заблуждение: масштабом он не управляет.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ РЕСУРС, А НЕ ПОЛЯ В НАСТРОЙКАХ ПОВЕРХНОСТИ. Ресурс сохраняется
/// отдельным файлом, поэтому одно поле переиспользуется несколькими правилами по ссылке,
/// а готовые заготовки в <c>resources/surface/noise/</c> подставляются в инспекторе через
/// быструю загрузку. Отдельного механизма заготовок заводить не понадобилось.
///
/// ПОЧЕМУ ЗЕРНО ЗДЕСЬ ТОЛЬКО СМЕЩЕНИЕ. Зерно принадлежит партии, а не описанию поля;
/// поле лишь сдвигает его, чтобы два разных поля на одном зерне не совпали.
/// </summary>
[Tool]
[GlobalClass]
public partial class NoiseSettings : Resource
{
    private NoiseKind _kind = NoiseKind.Simplex;

    /// <summary>
    /// Вид шума. При смене список настроек в инспекторе перестраивается: показаны только
    /// те, что относятся к выбранному виду.
    /// </summary>
    [Export]
    public NoiseKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            NotifyPropertyListChanged();
        }
    }

    /// <summary>
    /// Частота: величина, обратная периоду в пикселях мира. Значение 0.004 даёт пятна
    /// размером около 250 пикселей, то есть примерно четыре клетки застройки.
    /// </summary>
    [Export(PropertyHint.Range, "0.0002,0.05,0.0002")]
    public float Frequency = 0.004f;

    /// <summary>
    /// Приближение: множитель размера пятен, действующий при любом виде шума. Двойка
    /// растягивает рисунок вдвое, половина сжимает; единица оставляет как есть.
    ///
    /// ПОЧЕМУ ОТДЕЛЬНО ОТ ЧАСТОТЫ, ХОТЯ ДЕЙСТВУЕТ НА НЕЁ ЖЕ. Частота подбирается один раз
    /// и выражает замысел поля — насколько крупными задуманы пятна. Приближение правится
    /// при примерке, когда то же поле надо посмотреть крупнее или мельче, не трогая
    /// подобранного числа и не пересчитывая его в уме. Итоговая частота есть частное от
    /// деления <see cref="Frequency"/> на это значение.
    ///
    /// К <c>zoom</c> из Planetary Annihilation отношения не имеет: там так назван параметр,
    /// который управляет не масштабом, а контрастностью, и его роль исполняет
    /// <see cref="Contrast"/>.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,20,0.05")]
    public float Zoom = 1f;

    /// <summary>
    /// Контрастность вокруг середины: единица оставляет распределение как есть, большие
    /// значения вытесняют его к краям диапазона, меньшие единицы стягивают к 0.5.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,8,0.1")]
    public float Contrast = 1f;

    /// <summary>
    /// Смещение зерна. Двум полям с одинаковыми настройками разные смещения дают разный
    /// рисунок на одном и том же зерне партии.
    /// </summary>
    [Export] public int SeedOffset;

    // ── Фрактальные настройки: Simplex, Perlin, Value ─────────────────────────────

    /// <summary>Число октав. Единица даёт плавные переходы, пять и более — дробность.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int Octaves = 3;

    /// <summary>Во сколько раз растёт частота от октавы к октаве.</summary>
    [Export(PropertyHint.Range, "1,4,0.05")] public float Lacunarity = 2f;

    /// <summary>
    /// Стойкость: вклад каждой следующей октавы. Ниже 0.5 сглаживает, выше подчёркивает
    /// мелкие подробности.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Persistence = 0.5f;

    // ── Клеточные настройки ───────────────────────────────────────────────────────

    /// <summary>Мера расстояния до узла ячейки: от неё зависит форма ячеек.</summary>
    [Export] public FastNoiseLite.CellularDistanceFunctionEnum CellularDistance =
        FastNoiseLite.CellularDistanceFunctionEnum.Euclidean;

    /// <summary>Что берётся значением: расстояние, разность расстояний, номер ячейки.</summary>
    [Export] public FastNoiseLite.CellularReturnTypeEnum CellularReturn =
        FastNoiseLite.CellularReturnTypeEnum.Distance;

    /// <summary>Насколько узлы смещены от центров ячеек. Ноль даёт правильную решётку.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float CellularJitter = 1f;

    /// <summary>
    /// Скрыть настройки, не относящиеся к выбранному виду. Без этого инспектор показывает
    /// клеточные параметры у гладкого шума, где они ничего не делают.
    /// </summary>
    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string name = property["name"].AsString();
        bool cellular = Kind == NoiseKind.Cellular;

        bool hide = name switch
        {
            nameof(Octaves) or nameof(Lacunarity) or nameof(Persistence) => cellular,
            nameof(CellularDistance) or nameof(CellularReturn) or nameof(CellularJitter) => !cellular,
            _ => false,
        };

        if (hide)
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
    }

    /// <summary>
    /// Собрать источник шума на зерне партии. Один и тот же вызов обслуживает и выборку
    /// на процессоре при размещении декалей, и построение текстуры для шейдера, поэтому
    /// разойтись они не могут.
    /// </summary>
    public FastNoiseLite Build(ulong seed)
    {
        var noise = new FastNoiseLite
        {
            Seed = unchecked((int)seed + SeedOffset),
            Frequency = Mathf.Max(Frequency / Mathf.Max(Zoom, 0.01f), 0.00001f),
            NoiseType = Kind switch
            {
                NoiseKind.Perlin => FastNoiseLite.NoiseTypeEnum.Perlin,
                NoiseKind.Value => FastNoiseLite.NoiseTypeEnum.Value,
                NoiseKind.Cellular => FastNoiseLite.NoiseTypeEnum.Cellular,
                _ => FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            },
        };

        if (Kind == NoiseKind.Cellular)
        {
            noise.FractalType = FastNoiseLite.FractalTypeEnum.None;
            noise.CellularDistanceFunction = CellularDistance;
            noise.CellularReturnType = CellularReturn;
            noise.CellularJitter = CellularJitter;
        }
        else
        {
            noise.FractalType = FastNoiseLite.FractalTypeEnum.Fbm;
            noise.FractalOctaves = Mathf.Max(Octaves, 1);
            noise.FractalLacunarity = Lacunarity;
            noise.FractalGain = Persistence;
        }

        return noise;
    }

    /// <summary>
    /// Значение поля в точке мира, приведённое к отрезку от нуля до единицы. Середина
    /// отрезка — 0.5, как в Planetary Annihilation: диапазоны правил отсчитываются от неё.
    /// </summary>
    public float Sample(FastNoiseLite noise, Vector2 world)
    {
        float raw = noise.GetNoise2D(world.X, world.Y) * 0.5f + 0.5f;
        return Mathf.Clamp(0.5f + (raw - 0.5f) * Contrast, 0f, 1f);
    }
}
