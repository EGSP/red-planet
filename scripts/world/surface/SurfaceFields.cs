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
/// ТЕМПЕРАТУРА НУЖНА И ШЕЙДЕРУ, И РАСКЛАДКЕ. Отбор биомов и декалей читает
/// <see cref="TemperatureAt"/> на процессоре; базовый слой читает ту же карту в шейдере,
/// чтобы покрытия проявлялись в отрезке своего биома. Оверлей
/// <see cref="SurfaceSettings.ShowTemperature"/> показывает то же поле поверх местности.
///
/// ГЕОМЕТРИЯ ИЗОТЕРМЫ РАЗЫГРЫВАЕТСЯ ОДИН РАЗ. Настройки местности задают не сами угол,
/// кривизну и ширину перехода, а пределы их розыгрыша; конкретные значения выводятся здесь
/// из зерна партии и далее не меняются. Это то же правило, по которому в
/// <see cref="SurfaceSettings"/> не хранится зерно: ресурс описывает правила, зерно даёт
/// их разрешение, отчего две партии на одной местности различаются рисунком карты.
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
    private readonly FastNoiseLite _warpX;
    private readonly FastNoiseLite _warpY;

    /// <summary>Единичное направление, в котором растёт температура. Изотерма ему поперечна.</summary>
    private readonly Vector2 _growth;

    /// <summary>Кривизна изотермы, величина обратная радиусу дуги, в единицах на пиксель.</summary>
    private readonly float _curvature;

    /// <summary>Ширина перехода в пикселях мира: на ней укладывается весь отрезок значений.</summary>
    private readonly float _width;

    /// <summary>Размах искажения области в пикселях мира.</summary>
    private readonly float _warp;

    /// <summary>Точка, через которую проходит изотерма, сдвинутая тем же искажением.</summary>
    private readonly Vector2 _origin;

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

    /// <summary>Итоговая температура одним каналом — то же, что <see cref="TemperatureAt"/>.</summary>
    public ImageTexture TemperatureTexture { get; }

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
        // Все поля строятся на одном зерне партии: поле определяется своими настройками и
        // ничем более, а два поля с одинаковыми настройками разводятся SeedOffset ресурса.
        // Примесь номера поля нарушала бы это правило так же, как номер декали в
        // SurfaceLayout: один и тот же ресурс давал бы разный рисунок в разных местах
        _temperature = settings.TemperatureNoise?.Build(seed);

        // Двум составляющим сдвига нужны два независимых потока значений. Взять их выборкой
        // одного поля в двух точках, разнесённых постоянным смещением, нельзя: составляющие
        // тогда согласованы, и сдвиг всюду направлен вдоль одной диагонали, отчего искажение
        // вырождается в сжатие рисунка. Поэтому второе поле отличается зерном
        _warpX = (settings.TemperatureWarpNoise ?? settings.BaseNoise)?.Build(seed);
        _warpY = (settings.TemperatureWarpNoise ?? settings.BaseNoise)?.Build(seed);

        if (_warpY != null)
            _warpY.Seed = unchecked(_warpY.Seed + 1);

        // Порядок розыгрышей менять нельзя: он определяет, какое число достанется какому
        // параметру, и перестановка изменила бы карты всех уже подобранных местностей
        var random = new RandomNumberGenerator { Seed = seed };
        float span = Mathf.Max(bounds.Size.X, Mathf.Max(bounds.Size.Y, 1f));

        // Направление роста температуры не настраивается вовсе: у карты нет ни верха, ни
        // сторон света, поэтому ограничивать оборот нечем
        float angle = random.Randf() * Mathf.Tau;

        _growth = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Изгиб и смещение разыгрываются вокруг нуля, а не вокруг заданного значения: в
        // настройках хранится наибольший размах, отчего сторона изгиба и сторона смещения
        // достаются зерну вместе с их величиной
        _curvature = Spread(random, settings.IsothermCurvature) / span;

        _width = Mathf.Max(
            settings.IsothermWidth * (1f + Spread(random, settings.IsothermWidthVariation)),
            0.05f) * span;

        // Сила искажения не разыгрывается: рисунок поля шума и без того выпадает свой на
        // каждом зерне, а размах силы поверх этого различим не был
        _warp = Mathf.Max(settings.TemperatureWarp, 0f) * span;

        // Смещение ограничено и после розыгрыша: уведённая за край карты дуга оставила бы
        // всю арену в одном биоме, и подобранные отрезки температуры перестали бы работать
        float offset = Mathf.Clamp(Spread(random, settings.IsothermOffset), -0.45f, 0.45f);

        // Точка, через которую проходит дуга, сдвигается тем же искажением, что и все прочие:
        // иначе искажение сместило бы и её саму, и заданное смещение выполнялось бы неточно
        _origin = Warp(bounds.GetCenter() + _growth * (offset * span));

        BaseTexture = Bake(settings.BaseNoise, _base);
        HeightTexture = Bake(settings.HeightNoise, _height);
        TemperatureTexture = BakeTemperature();
    }

    /// <summary>Значение шума базового слоя в точке мира.</summary>
    public float BaseAt(Vector2 world) =>
        _base == null ? 0.5f : _settings.BaseNoise.Sample(_base, world);

    /// <summary>Высота в точке мира. При незаданном поле — середина отрезка.</summary>
    public float HeightAt(Vector2 world) =>
        _height == null ? 0.5f : _settings.HeightNoise.Sample(_height, world);

    /// <summary>
    /// Температура в точке мира, от нуля до единицы. У изотермы значение растёт в сторону
    /// разыгранного направления, а на самой дуге равно середине шкалы.
    /// </summary>
    public float TemperatureAt(Vector2 world)
    {
        float value = _settings.Temperature == TemperatureSource.Noise
            ? (_temperature == null ? 0.5f : _settings.TemperatureNoise.Sample(_temperature, world))
            : Isotherm(world);

        return Mathf.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Температура по знаковому расстоянию до дуги, проходящей через <see cref="_origin"/>.
    ///
    /// ПОЧЕМУ КРИВИЗНА, А НЕ РАДИУС. Прямая линия есть дуга нулевой кривизны, но радиуса
    /// бесконечного, поэтому при хранении радиуса прямой случай пришлось бы выделять
    /// отдельной ветвью кода. Запись ниже получена подстановкой <c>R = 1/k</c> в
    /// <c>d = R − |p − c|</c> с последующим избавлением от разности близких величин:
    /// при <c>k = 0</c> она даёт в точности расстояние до прямой, и ветвление не требуется.
    ///
    /// РАЗВИТИЕ, ЕСЛИ ПОНАДОБИТСЯ. Несколько дуг с разной кривизной, из которых берётся
    /// наименьшее расстояние, дадут полосчатую структуру — подобие климатических поясов
    /// вместо единственного перехода от холода к жаре. Смысл в этом появится тогда, когда
    /// у местности станет больше трёх биомов: при двух биомах вторая дуга даст лишь
    /// повторение уже занятого отрезка шкалы.
    /// </summary>
    private float Isotherm(Vector2 world)
    {
        var offset = Warp(world) - _origin;

        float across = offset.Dot(_growth);
        float square = offset.LengthSquared();

        // Подкоренное выражение не превосходит единицы при любых значениях, поскольку
        // square не меньше квадрата across; ограничение защищает от потери точности
        float inner = Mathf.Min(
            2f * across * _curvature - _curvature * _curvature * square, 1f);

        float distance = (2f * across - _curvature * square) / (1f + Mathf.Sqrt(1f - inner));

        return 0.5f + distance / _width;
    }

    /// <summary>
    /// Сдвинуть точку по векторному полю шума перед вычислением расстояния. Правильная дуга
    /// от этого превращается в изрезанную линию, а порядок значений вдоль направления роста
    /// сохраняется, поскольку сдвиг всюду мал по сравнению с шириной перехода.
    /// </summary>
    private Vector2 Warp(Vector2 world)
    {
        if (_warp <= 0f || _warpX == null || _warpY == null)
            return world;

        return world + new Vector2(
            _warpX.GetNoise2D(world.X, world.Y),
            _warpY.GetNoise2D(world.X, world.Y)) * _warp;
    }

    /// <summary>
    /// Разыграть отклонение от нуля в пределах заданного размаха, вместе со стороной. Число
    /// из потока берётся и при нулевом размахе: иначе выключенная случайность сдвинула бы всю
    /// последовательность и изменила прочие параметры.
    /// </summary>
    private static float Spread(RandomNumberGenerator random, float amount) =>
        (random.Randf() * 2f - 1f) * amount;

    /// <summary>Выгрузить поле в одноканальную текстуру для шейдера.</summary>
    private ImageTexture Bake(NoiseSettings settings, FastNoiseLite source)
    {
        if (settings == null || source == null)
            return null;

        return BakeGrid(world => settings.Sample(source, world));
    }

    /// <summary>Выгрузить итоговую температуру — ту же, что читает отбор биомов.</summary>
    private ImageTexture BakeTemperature() => BakeGrid(TemperatureAt);

    /// <summary>Пройти сетку области и записать значения в одноканальную текстуру.</summary>
    private ImageTexture BakeGrid(Func<Vector2, float> sample)
    {
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
                float value = sample(new Vector2(wx, wy));

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
        hash = HashCode.Combine(hash, SignatureOf(settings.TemperatureWarpNoise));
        hash = HashCode.Combine(hash, (int)settings.Temperature, settings.IsothermCurvature,
            settings.IsothermWidth, settings.IsothermWidthVariation, settings.IsothermOffset,
            settings.TemperatureWarp);

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
