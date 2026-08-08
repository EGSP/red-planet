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
///
/// ПРОЗРАЧНОСТЬ СЛОЁВ ОТДЕЛЕНА ОТ ШКАЛ. ShadeAmount и BodyAmount умножают альфу тени и тела.
/// Сигнал зума задаёт целевые величины; каждый кадр текущие сближаются с ними по
/// CloudZoomFade.LerpFactor. Ужатие и кривые ответа на зум лежат в CloudZoomFade; цепочка:
/// логарифмическая доля камеры → ZoomSqueeze → ShadeCurve / BodyCurve → цель → lerp → множитель.
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

    [ExportGroup("Прозрачность")]

    /// <summary>
    /// Текущий множитель непрозрачности тени. К целевому значению от зума сближается
    /// каждый кадр с скоростью <see cref="CloudZoomFade.LerpFactor"/>. Ноль скрывает тень,
    /// единица оставляет шкалу <see cref="CloudSettings.Shade"/> без изменений.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ShadeAmount = 1f;

    /// <summary>
    /// Текущий множитель непрозрачности тела облака. Устройство то же, что у
    /// <see cref="ShadeAmount"/>.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BodyAmount = 1f;

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

    /// <summary>Целевая непрозрачность тени от зума; к ней тянется <see cref="ShadeAmount"/>.</summary>
    private float _shadeTarget = 1f;

    /// <summary>Целевая непрозрачность тела от зума; к ней тянется <see cref="BodyAmount"/>.</summary>
    private float _bodyTarget = 1f;

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

        // В редакторе зум камеры на облака не пишет, а цели по умолчанию равны единице.
        // Сближение тогда тянуло бы ShadeAmount / BodyAmount к единице каждый кадр и
        // затирало значения в инспекторе. В партии цели задаёт сигнал ZoomFactorChanged.
        bool fading = !Engine.IsEditorHint() && BlendAmounts(delta);

        var area = Area();
        bool moved = _area != area;

        _area = area;
        Apply(area);

        // Перерисовка нужна каждый кадр: облака идут. При остановленном ветре — только
        // если сдвинулась область или ещё идёт сближение непрозрачности с целью
        if (!Freeze || moved || fading)
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

    /// <summary>Задать целевую непрозрачность тени. К ней сближается <see cref="ShadeAmount"/>.</summary>
    public void SetShadeAmount(float amount) => _shadeTarget = Mathf.Clamp(amount, 0f, 1f);

    /// <summary>Задать целевую непрозрачность тела облака.</summary>
    public void SetBodyAmount(float amount) => _bodyTarget = Mathf.Clamp(amount, 0f, 1f);

    /// <summary>Вести тень от доли приближения камеры. Сигнатура совпадает с ZoomFactorChanged.</summary>
    public void DriveShadeByZoom(float factor, float inverted)
    {
        _ = inverted;
        SetShadeAmount(MapZoom(factor, Settings?.ZoomFade?.ShadeCurve));
    }

    /// <summary>Вести тень от дополнения доли приближения: сильна при отдалённой камере.</summary>
    public void DriveShadeByZoomInverted(float factor, float inverted)
    {
        _ = factor;
        SetShadeAmount(MapZoom(inverted, Settings?.ZoomFade?.ShadeCurve));
    }

    /// <summary>Вести тело облака от доли приближения: проявляется при приближении.</summary>
    public void DriveBodyByZoom(float factor, float inverted)
    {
        _ = inverted;
        SetBodyAmount(MapZoom(factor, Settings?.ZoomFade?.BodyCurve));
    }

    /// <summary>Вести тело облака от дополнения доли приближения.</summary>
    public void DriveBodyByZoomInverted(float factor, float inverted)
    {
        _ = factor;
        SetBodyAmount(MapZoom(inverted, Settings?.ZoomFade?.BodyCurve));
    }

    /// <summary>
    /// Сблизить текущие множители с целевыми. Возвращает, осталось ли ещё расхождение —
    /// тогда нужна перерисовка даже при остановленном ветре.
    /// </summary>
    private bool BlendAmounts(double delta)
    {
        float factor = Settings?.ZoomFade?.LerpFactor ?? 0f;

        if (factor <= 0f)
        {
            bool changed = !Mathf.IsEqualApprox(ShadeAmount, _shadeTarget)
                || !Mathf.IsEqualApprox(BodyAmount, _bodyTarget);

            ShadeAmount = _shadeTarget;
            BodyAmount = _bodyTarget;
            return changed;
        }

        float t = Mathf.Clamp(factor * (float)delta, 0f, 1f);

        ShadeAmount = Mathf.Lerp(ShadeAmount, _shadeTarget, t);
        BodyAmount = Mathf.Lerp(BodyAmount, _bodyTarget, t);

        return !Mathf.IsEqualApprox(ShadeAmount, _shadeTarget)
            || !Mathf.IsEqualApprox(BodyAmount, _bodyTarget);
    }

    /// <summary>
    /// Доля зума → множитель слоя: сначала <see cref="CloudZoomFade.ZoomSqueeze"/>, затем
    /// кривая слоя из <see cref="CloudZoomFade"/>.
    /// </summary>
    private float MapZoom(float value, Curve curve)
    {
        float squeezed = SqueezeZoom(value);

        if (curve == null)
            return squeezed;

        return Mathf.Clamp(curve.Sample(squeezed), 0f, 1f);
    }

    /// <summary>
    /// Сжать долю зума: края отрезка отсекаются на ZoomSqueeze с каждой стороны, середина
    /// растягивается на полный отрезок от нуля до единицы.
    /// </summary>
    private float SqueezeZoom(float value)
    {
        float pad = Mathf.Clamp(Settings?.ZoomFade?.ZoomSqueeze ?? 0f, 0f, 0.49f);
        float span = 1f - 2f * pad;

        if (span <= 0f)
            return value >= 0.5f ? 1f : 0f;

        return Mathf.Clamp((value - pad) / span, 0f, 1f);
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
        _material.SetShaderParameter("shade_amount", Mathf.Clamp(ShadeAmount, 0f, 1f));
        _material.SetShaderParameter("body_amount", Mathf.Clamp(BodyAmount, 0f, 1f));

        _material.SetShaderParameter("wind_dir", Settings.WindDirection);
        _material.SetShaderParameter("travel", _travel);

        _material.SetShaderParameter("zoom", Mathf.Max(Settings.Zoom, 1f));
        _material.SetShaderParameter("scale_x", Mathf.Max(Settings.ScaleX, 0.001f));
        _material.SetShaderParameter("scale_y", Mathf.Max(Settings.ScaleY, 0.001f));
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
