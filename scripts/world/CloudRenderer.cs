using Godot;

/// <summary>
/// Отрисовка облаков и их тени одним прямоугольником поверх мира.
///
/// ОДИН ВЫЗОВ ОТРИСОВКИ. Устройство то же, что у тени препятствий и у поверхности:
/// прямоугольник натянут на видимую область, а всю работу выполняет шейдер
/// <c>clouds.gdshader</c>. Закраска ограничена площадью экрана и от приближения не зависит,
/// поэтому стоимость кадра постоянна независимо от размера карты.
///
/// ОБЛАСТЬ СЧИТАЕТСЯ ПО КАМЕРЕ, А НЕ ПО ГРАНИЦАМ МИРА. Небо не кончается на краю карты,
/// и полоса чистой земли по периметру читалась бы как дефект. Видимая область объединяется
/// с границами арены, отчего облака есть везде, куда камера может смотреть.
///
/// ВРЕМЯ СЧИТАЕТСЯ САМИМ УЗЛОМ, А НЕ БЕРЁТСЯ ИЗ ВСТРОЕННОЙ ПЕРЕМЕННОЙ TIME. Она идёт и при
/// остановленной партии, отчего облака ползли бы над замершим миром. Путь, пройденный
/// ветром, накапливается здесь от delta и потому подчиняется остановке игры.
///
/// РАБОТАЕТ И В РЕДАКТОРЕ. Узел помечен атрибутом Tool и перерисовывается каждый кадр,
/// поэтому облака идут прямо на холсте открытой сцены, и подбор ведётся без запуска партии.
/// Поля <see cref="Freeze"/> и <see cref="Phase"/> к настройкам вида не относятся: они
/// задают, какой случай показывать, — форма подбирается по неподвижной картинке, а скорость,
/// наоборот, только в движении.
/// </summary>
[Tool]
public partial class CloudRenderer : Node2D
{
    private const string ShaderPath = "res://resources/shaders/clouds.gdshader";

    /// <summary>Сторона процедурной карты плотности, пикселей.</summary>
    private const int NoiseSize = 512;

    /// <summary>Периодов шума на сторону карты. Меньше — крупнее клубы внутри одного пятна.</summary>
    private const float NoisePeriods = 3f;

    /// <summary>Точек в запечённой цветовой шкале. Заведомо мельче, чем различает глаз.</summary>
    private const int RampWidth = 256;

    [ExportGroup("Облака")]

    /// <summary>Настройки вида. Не назначены — облака не рисуются вовсе.</summary>
    [Export] public CloudSettings Settings;

    [ExportGroup("Стенд")]

    /// <summary>Остановить ветер: смещение берётся из <see cref="Phase"/>, а не из времени.</summary>
    [Export] public bool Freeze;

    /// <summary>Пройденный ветром путь при остановке, пикселей. Им пролистываются облака.</summary>
    [Export(PropertyHint.Range, "0,20000,1")] public float Phase;

    private ShaderMaterial _material;
    private ImageTexture _white;

    private GradientTexture1D _shadeRamp;
    private GradientTexture1D _bodyRamp;

    private NoiseTexture2D _noise;
    private int _noiseSeed;

    private float _travel;
    private bool _frozen;

    private Rect2 _area;

    /// <summary>
    /// Снять с узла всё тяжёлое перед сохранением сцены и вернуть после. Причина та же, что
    /// у поверхности: редактор сохраняет содержимое свойств, а в веществе лежит карта
    /// плотности размером в четверть мегабайта. Без развязки она попадает в файл сцены
    /// встроенным ресурсом, причём устаревшим на момент сохранения. Считается она из зерна
    /// за доли секунды, поэтому хранить её незачем.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationEditorPreSave)
            Detach();
        else if (what == NotificationEditorPostSave)
            Attach();
    }

    public override void _Process(double delta)
    {
        Visible = Settings != null && Settings.Enabled && (Settings.Shade != null || Settings.Body != null);

        if (!Visible)
            return;

        Attach();
        Advance(delta);

        var area = Area();
        bool moved = _area != area;

        _area = area;
        Apply(area);

        // Перерисовка нужна каждый кадр: облака идут. При остановленном ветре картинка
        // не меняется, и редактор не занимается напрасной работой
        if (!Freeze || moved)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_material == null || Settings == null)
            return;

        // Прямоугольник рисуется белой точкой: цвет берёт шейдер, а текстура нужна лишь
        // затем, чтобы холст выдал UV, растянутое по области
        _white ??= White();

        DrawTextureRect(_white, GetGlobalTransform().AffineInverse() * _area, false);
    }

    /// <summary>
    /// Продвинуть ветер. При остановке путь запоминается в <see cref="Phase"/> один раз,
    /// чтобы пролистывание начиналось с того места, где облака застали, а не с нуля.
    /// </summary>
    private void Advance(double delta)
    {
        if (Freeze)
        {
            if (!_frozen)
            {
                _frozen = true;
                Phase = _travel;
            }

            _travel = Phase;
            return;
        }

        _frozen = false;
        _travel += (float)delta * Mathf.Max(Settings.WindSpeedPx, 0f);
    }

    /// <summary>
    /// Область отрисовки в мировых координатах: видимое камерой, объединённое с ареной.
    /// Объединение нужно затем, чтобы при неготовом преобразовании холста — а в редакторе
    /// оно на первом кадре именно таково — облака всё же покрывали карту.
    /// </summary>
    private Rect2 Area()
    {
        var view = GetCanvasTransform().AffineInverse() * GetViewportRect();
        return World.ArenaBounds.Merge(view);
    }

    /// <summary>Передать шейдеру шкалы, карту плотности и величины формы.</summary>
    private void Apply(Rect2 area)
    {
        _material.SetShaderParameter("rect_origin", area.Position);
        _material.SetShaderParameter("rect_size", area.Size);

        _material.SetShaderParameter("density", Density());

        _shadeRamp = Ramp(_shadeRamp, Settings.Shade);
        _bodyRamp = Ramp(_bodyRamp, Settings.Body);

        _material.SetShaderParameter("shade_ramp", _shadeRamp);
        _material.SetShaderParameter("body_ramp", _bodyRamp);
        _material.SetShaderParameter("has_shade", Settings.Shade != null);
        _material.SetShaderParameter("has_body", Settings.Body != null);

        _material.SetShaderParameter("wind_dir", Settings.WindDirection);
        _material.SetShaderParameter("travel", _travel);

        _material.SetShaderParameter("size_px", Mathf.Max(Settings.SizePx, 1f));
        _material.SetShaderParameter("stretch", Mathf.Max(Settings.Stretch, 1f));
        _material.SetShaderParameter("coverage", Mathf.Clamp(Settings.Coverage, 0f, 1f));
        _material.SetShaderParameter("erosion", Mathf.Clamp(Settings.Erosion, 0f, 1f));
        _material.SetShaderParameter("lift", Settings.LiftPx);
    }

    /// <summary>
    /// Карта плотности: нарисованная, если назначена, иначе процедурный шум по зерну.
    /// Шум пересобирается только при смене зерна, поэтому вызов каждый кадр безобиден.
    /// </summary>
    private Texture2D Density()
    {
        if (Settings.Texture != null)
        {
            _noise = null;
            return Settings.Texture;
        }

        if (_noise != null && _noiseSeed == Settings.Seed)
            return _noise;

        _noiseSeed = Settings.Seed;

        // Бесшовность обязательна: карта повторяется по всей плоскости мира. Она же сжимает
        // размах значений к середине отрезка, и это расширяет обратно сам шейдер
        _noise = new NoiseTexture2D
        {
            Width = NoiseSize,
            Height = NoiseSize,
            Seamless = true,
            GenerateMipmaps = true,
            Noise = new FastNoiseLite
            {
                NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
                Seed = Settings.Seed,
                Frequency = NoisePeriods / NoiseSize,
                FractalOctaves = 4,
            },
        };

        return _noise;
    }

    /// <summary>Запечь шкалу в текстуру. Пересоздаётся она только при смене самого градиента.</summary>
    private static GradientTexture1D Ramp(GradientTexture1D baked, Gradient gradient)
    {
        if (gradient == null)
            return baked;

        if (baked != null && baked.Gradient == gradient)
            return baked;

        return new GradientTexture1D
        {
            Gradient = gradient,
            Width = RampWidth,
        };
    }

    /// <summary>
    /// Завести вещество, если его нет. Оно создаётся заново при каждой загрузке и в сцене
    /// не хранится: причина описана у <see cref="_Notification"/>.
    /// </summary>
    private void Attach()
    {
        if (_material == null)
        {
            _material = Material as ShaderMaterial ?? new ShaderMaterial();
            Material = _material;
        }

        _material.Shader ??= GD.Load<Shader>(ShaderPath);
    }

    /// <summary>Снять вещество и карту плотности: после этого узлу нечего отдать сохранению.</summary>
    private void Detach()
    {
        Material = null;
        _material = null;

        _noise = null;
        _noiseSeed = 0;
        _shadeRamp = null;
        _bodyRamp = null;
    }

    /// <summary>Белая точка: холсту нужна текстура, чтобы выдать шейдеру UV прямоугольника.</summary>
    private static ImageTexture White()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, Colors.White);

        return ImageTexture.CreateFromImage(image);
    }
}
