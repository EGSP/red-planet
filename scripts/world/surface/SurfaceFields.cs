using System;
using Godot;

/// <summary>
/// Поля местности: шум базового слоя, температура и высота, посчитанные от зерна партии.
///
/// ОДИН ИСТОЧНИК НА ПРОЦЕССОР И НА ВИДЕОКАРТУ. По этим же полям и размещаются декали, и
/// смешиваются покрытия. Поэтому источник шума здесь один: <see cref="NoiseSettings.Build"/>
/// даёт <see cref="FastNoiseLite"/>, из него выбирает и размещение, и построение текстуры
/// для шейдера. Две независимые реализации одного поля разошлись бы, и декали легли бы не
/// туда, куда указывает изображение.
///
/// ПОЧЕМУ ТЕМПЕРАТУРА НЕ ВЫГРУЖАЕТСЯ В ТЕКСТУРУ. Она нужна только при отборе биомов, то
/// есть при размещении декалей, а это работа процессора. Базовый слой делится между
/// покрытиями по шуму, а не по температуре, поэтому шейдеру температура не требуется.
///
/// ПЕРЕСБОРКА ПО ОТПЕЧАТКУ НАСТРОЕК. Поля перестраиваются, когда изменился
/// <see cref="Signature"/>, — иначе правка любого числа в инспекторе означала бы
/// пересчёт миллиона выборок каждый кадр.
/// </summary>
public sealed class SurfaceFields
{
    /// <summary>Сторона выгружаемых текстур. При арене около 3400 пикселей это 3.3 px на тексель.</summary>
    public const int Resolution = 1024;

    private readonly SurfaceSettings _settings;
    private readonly FastNoiseLite _base;
    private readonly FastNoiseLite _height;
    private readonly FastNoiseLite _temperature;
    private readonly FastNoiseLite _jitter;

    /// <summary>Область мира, которую покрывают поля.</summary>
    public Rect2 Bounds { get; }

    /// <summary>Зерно, на котором поля построены.</summary>
    public ulong Seed { get; }

    /// <summary>Отпечаток настроек, при изменении которого поля нужно собрать заново.</summary>
    public int Signature { get; }

    /// <summary>Шум базового слоя одним каналом. Не назначен — остаётся пустым.</summary>
    public ImageTexture BaseTexture { get; }

    /// <summary>Поле высот одним каналом. Не назначено — остаётся пустым.</summary>
    public ImageTexture HeightTexture { get; }

    /// <summary>Задано ли поле высот: от этого зависит, проверяются ли отрезки высоты.</summary>
    public bool HasHeight => _height != null;

    public SurfaceFields(SurfaceSettings settings, ulong seed, Rect2 bounds)
    {
        _settings = settings;
        Seed = seed;
        Bounds = bounds;
        Signature = SignatureOf(settings, seed, bounds);

        _base = settings.BaseNoise?.Build(seed);
        _height = settings.HeightNoise?.Build(seed);
        _temperature = settings.TemperatureNoise?.Build(seed);
        // Все поля строятся на одном зерне партии: поле определяется своими настройками и
        // ничем более, а два поля с одинаковыми настройками разводятся SeedOffset ресурса.
        // Прежняя примесь 977 у неровности нарушала это правило так же, как номер декали
        // в SurfaceLayout: один и тот же ресурс давал разный рисунок в разных местах
        _jitter = (settings.TemperatureJitterNoise ?? settings.BaseNoise)?.Build(seed);

        BaseTexture = Bake(settings.BaseNoise, _base);
        HeightTexture = Bake(settings.HeightNoise, _height);
    }

    /// <summary>Значение шума базового слоя в точке мира.</summary>
    public float BaseAt(Vector2 world) =>
        _base == null ? 0.5f : _settings.BaseNoise.Sample(_base, world);

    /// <summary>Высота в точке мира. При незаданном поле — середина отрезка.</summary>
    public float HeightAt(Vector2 world) =>
        _height == null ? 0.5f : _settings.HeightNoise.Sample(_height, world);

    /// <summary>
    /// Температура в точке мира, от нуля до единицы. У радиального источника ноль в центре
    /// карты и единица на окружности появления противника — то есть тепло растёт по мере
    /// удаления от базы, что согласуется с нарастанием угрозы.
    /// </summary>
    public float TemperatureAt(Vector2 world)
    {
        float value;

        switch (_settings.Temperature)
        {
            case TemperatureSource.Noise:
                return _temperature == null
                    ? 0.5f
                    : _settings.TemperatureNoise.Sample(_temperature, world);

            case TemperatureSource.Linear:
            {
                float angle = Mathf.DegToRad(_settings.TemperatureAngleDegrees);
                var axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float span = Mathf.Max(Bounds.Size.X, Bounds.Size.Y);
                value = (world - Bounds.GetCenter()).Dot(axis) / span + 0.5f;
                break;
            }

            default:
            {
                float reach = Mathf.Max(World.SpawnRadiusPx, 1f);
                value = (world - Const.LandingPoint).Length() / reach;
                break;
            }
        }

        if (_settings.TemperatureJitter > 0f && _jitter != null)
        {
            float noise = _jitter.GetNoise2D(world.X, world.Y);
            value += noise * _settings.TemperatureJitter;
        }

        return Mathf.Clamp(value, 0f, 1f);
    }

    /// <summary>Выгрузить поле в одноканальную текстуру для шейдера.</summary>
    private ImageTexture Bake(NoiseSettings settings, FastNoiseLite source)
    {
        if (settings == null || source == null)
            return null;

        var data = new byte[Resolution * Resolution];
        float stepX = Bounds.Size.X / Resolution;
        float stepY = Bounds.Size.Y / Resolution;

        for (int y = 0; y < Resolution; y++)
        {
            float wy = Bounds.Position.Y + (y + 0.5f) * stepY;
            int row = y * Resolution;

            for (int x = 0; x < Resolution; x++)
            {
                float wx = Bounds.Position.X + (x + 0.5f) * stepX;
                float value = settings.Sample(source, new Vector2(wx, wy));

                data[row + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            }
        }

        var image = Image.CreateFromData(Resolution, Resolution, false, Image.Format.R8, data);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Отпечаток всего, от чего зависят поля. Складывается из зерна, области и настроек
    /// участвующих полей шума.
    /// </summary>
    public static int SignatureOf(SurfaceSettings settings, ulong seed, Rect2 bounds)
    {
        if (settings == null)
            return 0;

        int hash = HashCode.Combine(seed, bounds.Position, bounds.Size);

        hash = HashCode.Combine(hash, SignatureOf(settings.BaseNoise));
        hash = HashCode.Combine(hash, SignatureOf(settings.HeightNoise));
        hash = HashCode.Combine(hash, SignatureOf(settings.TemperatureNoise));
        hash = HashCode.Combine(hash, SignatureOf(settings.TemperatureJitterNoise));
        hash = HashCode.Combine(hash, (int)settings.Temperature,
            settings.TemperatureAngleDegrees, settings.TemperatureJitter);

        return hash;
    }

    /// <summary>Отпечаток одного поля шума. Пустая ссылка даёт ноль.</summary>
    public static int SignatureOf(NoiseSettings noise)
    {
        if (noise == null)
            return 0;

        return HashCode.Combine(
            (int)noise.Kind, noise.Frequency, noise.Contrast, noise.SeedOffset,
            noise.Octaves, noise.Lacunarity, noise.Persistence,
            HashCode.Combine(noise.Zoom, (int)noise.CellularDistance,
                (int)noise.CellularReturn, noise.CellularJitter));
    }
}
