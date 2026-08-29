using Godot;

/// <summary>
/// Отрисовка тумана войны: закрытая часть карты и обводка по границе поля зрения.
///
/// РИСУЕТСЯ ОДНИМ ПРЯМОУГОЛЬНИКОМ. Поле расстояний приходит текстурой из памяти видеокарты,
/// а всю работу по цвету и границе делает шейдер <c>fog.gdshader</c>. Отсюда и стоимость
/// отрисовки: один вызов на кадр независимо от того, сколько на карте источников зрения.
///
/// ПРЯМОУГОЛЬНИК БОЛЬШЕ КАРТЫ. Камера отъезжает за край мира, и туман, обрезанный по краю,
/// оставлял бы там светлую рамку. Поэтому прямоугольник расширен, а шейдеру передан
/// пересчёт координат: за пределами поля выборка повторяет краевое значение, то есть
/// закрытую область.
///
/// ПЕРЕРИСОВКА РЕДКАЯ. Содержимое поля меняется на видеокарте без участия холста, поэтому
/// QueueRedraw зовётся только тогда, когда изменились границы мира, — то есть при правке
/// настроек в редакторе.
///
/// ЗАЧЕМ ЕДИНИЧНАЯ ТЕКСТУРА. Поле передано шейдеру отдельной величиной, а не текстурой
/// холста, поскольку живёт оно в <see cref="Texture2Drd"/> и подчиняется своим правилам
/// выборки. Прямоугольник же нужно чем-то нарисовать, и рисуется он белым пикселем: от него
/// требуется только задать области координаты от нуля до единицы, по которым шейдер и берёт
/// значение поля.
/// </summary>
public partial class FogRenderer : Node2D
{
    /// <summary>Насколько прямоугольник тумана выходит за край карты, долей стороны мира.</summary>
    private const float MarginFactor = 0.5f;

    /// <summary>Поле расстояний. Ставит система зрения каждый кадр; null — поле ещё не готово.</summary>
    public Texture2D Source;

    /// <summary>Настройки отображения. Читаются каждый кадр: их правят по ходу партии.</summary>
    public FogSettings Settings;

    private ShaderMaterial _material;
    private ImageTexture _blank;

    private Rect2 _shownArea;

    public override void _Ready()
    {
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://resources/shaders/fog.gdshader"),
        };

        Material = _material;

        _blank = ImageTexture.CreateFromImage(Filled());
    }

    public override void _Process(double delta)
    {
        if (Source == null || Settings == null)
        {
            Visible = false;
            return;
        }

        Visible = Settings.Fog || Settings.Outline;

        if (!Visible)
            return;

        Apply();

        // Границы мира правятся в редакторе на ходу, и прямоугольник отрисовки за ними следует
        var area = Area();

        if (_shownArea != area)
        {
            _shownArea = area;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_blank != null)
            DrawTextureRect(_blank, Area(), false);
    }

    /// <summary>Прямоугольник отрисовки: карта плюс запас за её краем.</summary>
    private static Rect2 Area()
    {
        float margin = World.SizePx * MarginFactor;
        return World.Bounds.Grow(margin);
    }

    private static Image Filled()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, Colors.White);
        return image;
    }

    /// <summary>Передать шейдеру поле, настройки и пересчёт координат прямоугольника в координаты поля.</summary>
    private void Apply()
    {
        _material.SetShaderParameter("field", Source);
        _material.SetShaderParameter("fog_color", Settings.FogColor);
        _material.SetShaderParameter("outline_color", Settings.OutlineColor);
        _material.SetShaderParameter("outline_width", Settings.OutlineWidthPx);
        _material.SetShaderParameter("softness", Settings.SoftnessPx);
        _material.SetShaderParameter("fog_on", Settings.Fog);
        _material.SetShaderParameter("outline_on", Settings.Outline);
        _material.SetShaderParameter("sdf_range", VisionSystem.RangePx);

        float size = World.SizePx;

        _material.SetShaderParameter("field_size", size);

        float margin = size * MarginFactor;
        float scale = (size + margin * 2f) / size;
        float offset = -margin / size;

        _material.SetShaderParameter("field_scale", new Vector2(scale, scale));
        _material.SetShaderParameter("field_offset", new Vector2(offset, offset));
    }
}
