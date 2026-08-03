using Godot;

/// <summary>
/// Отрисовка тумана войны: закрытая часть карты и обводка по границе поля зрения.
///
/// РИСУЕТСЯ ОДНИМ ПРЯМОУГОЛЬНИКОМ. Растр видимости выгружается в текстуру, а всю работу
/// по цвету и границе делает шейдер <c>fog.gdshader</c>. Отсюда и стоимость отрисовки:
/// один вызов на кадр независимо от того, сколько на карте источников зрения.
///
/// ПРЯМОУГОЛЬНИК БОЛЬШЕ КАРТЫ. Камера отъезжает за край мира, и туман, обрезанный по краю,
/// оставлял бы там светлую рамку. Поэтому прямоугольник расширен, а шейдеру передан
/// пересчёт координат: за пределами растра выборка повторяет краевое значение, то есть
/// закрытую область.
///
/// ПЕРЕРИСОВКА РЕДКАЯ. Содержимое текстуры меняется без перерисовки холста, поэтому
/// QueueRedraw зовётся только тогда, когда изменились границы мира, — то есть при правке
/// настроек в редакторе.
/// </summary>
public partial class FogRenderer : Node2D
{
    /// <summary>Насколько прямоугольник тумана выходит за край карты, долей стороны мира.</summary>
    private const float MarginFactor = 0.5f;

    /// <summary>Растр видимости. Ставит система зрения сразу после создания узла.</summary>
    public VisionField Field;

    /// <summary>Настройки отображения. Читаются каждый кадр: их правят по ходу партии.</summary>
    public FogSettings Settings;

    private Image _image;
    private ImageTexture _texture;
    private ShaderMaterial _material;

    private int _shownRevision = -1;
    private Rect2 _shownArea;

    public override void _Ready()
    {
        // Линейная фильтрация обязательна: по градиенту значения шейдер находит границу,
        // а «ближайший сосед» превратил бы её в ступеньки по ячейкам растра
        TextureFilter = TextureFilterEnum.Linear;

        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://resources/shaders/fog.gdshader"),
        };

        Material = _material;
    }

    public override void _Process(double delta)
    {
        if (Field == null || Settings == null)
        {
            Visible = false;
            return;
        }

        Visible = Settings.Fog || Settings.Outline;

        if (!Visible)
            return;

        Apply();
        Refresh();
    }

    public override void _Draw()
    {
        if (_texture == null)
            return;

        DrawTextureRect(_texture, Area(), false);
    }

    /// <summary>Прямоугольник отрисовки: карта плюс запас за её краем.</summary>
    private static Rect2 Area()
    {
        float margin = World.SizePx * MarginFactor;
        return World.Bounds.Grow(margin);
    }

    /// <summary>Передать шейдеру настройки и пересчёт координат прямоугольника в координаты растра.</summary>
    private void Apply()
    {
        _material.SetShaderParameter("fog_color", Settings.FogColor);
        _material.SetShaderParameter("outline_color", Settings.OutlineColor);
        _material.SetShaderParameter("outline_width", Settings.OutlineWidthPx);
        _material.SetShaderParameter("softness", Settings.SoftnessPx);
        _material.SetShaderParameter("fog_on", Settings.Fog);
        _material.SetShaderParameter("outline_on", Settings.Outline);
        _material.SetShaderParameter("sdf_range", VisionField.RangePx);

        float size = World.SizePx;

        _material.SetShaderParameter("field_size", size);
        float margin = size * MarginFactor;
        float scale = (size + margin * 2f) / size;
        float offset = -margin / size;

        _material.SetShaderParameter("field_scale", new Vector2(scale, scale));
        _material.SetShaderParameter("field_offset", new Vector2(offset, offset));
    }

    /// <summary>
    /// Обновить текстуру по растру. Пересоздаётся она только при изменении размера мира:
    /// размер изображения задан при создании, и подогнать его на месте нельзя.
    /// </summary>
    private void Refresh()
    {
        int width = Field.Width;
        var values = Field.Values;

        if (values.Length != width * width)
            return;

        if (_image == null || _image.GetWidth() != width)
        {
            _image = Image.CreateFromData(width, width, false, Image.Format.L8, values);
            _texture = ImageTexture.CreateFromImage(_image);
            _shownRevision = Field.Revision;
            _shownArea = Area();

            QueueRedraw();
            return;
        }

        if (_shownRevision != Field.Revision)
        {
            _shownRevision = Field.Revision;
            _image.SetData(width, width, false, Image.Format.L8, values);
            _texture.Update(_image);
        }

        // Границы мира правятся в редакторе на ходу, а размер растра при этом мог и не
        // измениться — например, когда поле осталось прежним, а сместился его край
        var area = Area();

        if (_shownArea != area)
        {
            _shownArea = area;
            QueueRedraw();
        }
    }
}
