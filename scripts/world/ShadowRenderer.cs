using Godot;

/// <summary>
/// Отрисовка тени препятствий: затенение вокруг всего, что попадает в растр навигации.
///
/// ИСТОЧНИК — САМ РАСТР, А НЕ ПЕРЕЧЕНЬ СУЩНОСТЕЙ. Тень выводится из поля chamfer-расстояний
/// <see cref="NavGrid"/>, поэтому она есть у всего, что этому полю мешает: сейчас у построек
/// и каркасов, а новый род препятствий получит её в тот же миг, когда начнёт влиять на
/// проходимость. Рассогласования между тем, что перекрывает проход, и тем, что даёт тень,
/// возникнуть не может по устройству.
///
/// РИСУЕТСЯ ОДНИМ ПРЯМОУГОЛЬНИКОМ, как туман войны: растр выгружается в текстуру, а цвет
/// выбирает шейдер <c>shadow.gdshader</c> по градиенту из настроек. Отсюда стоимость кадра —
/// один вызов отрисовки независимо от того, сколько на карте зданий.
///
/// ПЕРЕСБОРКА ПО РЕВИЗИИ. Ревизия <see cref="NavGrid.Revision"/> растёт и при изменении
/// препятствий, и при публикации нового снимка, то есть ровно тогда, когда поле расстояний
/// могло стать другим. При неизменной застройке текстура не трогается вовсе.
///
/// ГРАНИЦА КАРТЫ ТОЖЕ ЗАТЕНЯЕТСЯ: за краем поля растр считает ячейки непроходимыми, и по
/// периметру мира идёт та же полоса, что вдоль стены. Это согласуется с поведением — выйти
/// за край действительно нельзя.
///
/// РАБОТАЕТ И В РЕДАКТОРЕ. Источник расстояний задаётся полем <see cref="Field"/>, поэтому
/// тот же узел рисует тень в предпросмотре застройки, где сессии нет вовсе.
/// </summary>
[Tool]
public partial class ShadowRenderer : Node2D
{
    /// <summary>Точек в запечённой цветовой шкале. Заведомо мельче, чем различает растр.</summary>
    private const int RampWidth = 256;

    /// <summary>Плавная подача: плотность меняется непрерывно.</summary>
    private const string SmoothShader = "res://resources/shaders/shadow.gdshader";

    /// <summary>Ступенчатая подача: плотность меняется по ячейкам растра.</summary>
    private const string PixelShader = "res://resources/shaders/shadow_pixel.gdshader";

    /// <summary>
    /// Настройки вида: цвет, ширина, угасание. В партии их подставляет
    /// <see cref="SurfaceRenderer"/> из <see cref="SurfaceSettings.Shadows"/> текущей
    /// местности; в предпросмотре застройки назначают напрямую.
    /// </summary>
    [Export] public ShadowSettings Settings;

    /// <summary>
    /// Ступенчатая тень вместо плавной: подача сменяется с <c>shadow.gdshader</c> на
    /// <c>shadow_pixel.gdshader</c>, а фильтрация растра — на «ближайший сосед».
    ///
    /// Ступеней столько, сколько ячеек растра укладывается в <see cref="ShadowSettings.WidthPx"/>.
    /// Признак лежит здесь, а не в настройках местности: язык подачи общий для всех
    /// поверхностей, тогда как цвет и плотность зависят от палитры.
    /// </summary>
    [Export] public bool Pixelated;

    /// <summary>
    /// Источник расстояний. Не назначен — берётся растр текущей сессии. Назначают его там,
    /// где сессии нет: в предпросмотре застройки поле собирается на месте.
    /// </summary>
    public IClearanceField Field;

    private ShaderMaterial _material;
    private GradientTexture1D _ramp;

    /// <summary>Путь шейдера, который сейчас стоит на веществе. По нему видно смену подачи.</summary>
    private string _shaderPath;

    private Image _image;
    private ImageTexture _texture;
    private byte[] _values;

    private int _shownRevision = -1;
    private Rect2 _shownArea;

    public override void _Ready()
    {
        // Назначенное в сцене вещество берётся как есть. Иначе узел с атрибутом Tool
        // создавал бы новое при каждой загрузке, а редактор сохранял бы его в сцену.
        // Шейдер ставит Apply: он зависит от выбранной подачи и меняется по ходу правки
        if (Material is ShaderMaterial assigned)
        {
            _material = assigned;
            return;
        }

        _material = new ShaderMaterial();
        Material = _material;
    }

    public override void _Process(double delta)
    {
        // Настройки в сцене могли и не назначить: без них узел рисует на умолчаниях,
        // а не роняет сессию
        Settings ??= new ShadowSettings();

        var field = Source();

        Visible = field != null && field.Width > 0 && Settings.Enabled && Settings.Tint != null;

        if (!Visible)
            return;

        Refresh(field);
        Apply();
    }

    /// <summary>
    /// Откуда брать расстояния. Назначенное поле старше растра сессии: предпросмотр в
    /// редакторе показывает свою застройку, а не ту, которой там нет.
    /// </summary>
    private IClearanceField Source()
    {
        if (Field != null)
            return Field;

        var gm = GameManager.I;

        if (gm == null)
            return null;

        gm.Nav.Fresh();
        return gm.Nav;
    }

    public override void _Draw()
    {
        if (_texture == null)
            return;

        DrawTextureRect(_texture, World.Bounds, false);
    }

    /// <summary>
    /// Обновить текстуру по растру. Пересоздаётся она только при изменении размера мира:
    /// размер изображения задан при создании, и подогнать его на месте нельзя.
    /// </summary>
    private void Refresh(IClearanceField field)
    {
        int width = field.Width;

        if (_image == null || _image.GetWidth() != width)
        {
            _values = new byte[width * width];
            Fill(field, width);

            _image = Image.CreateFromData(width, width, false, Image.Format.L8, _values);
            _texture = ImageTexture.CreateFromImage(_image);
            _shownRevision = field.Revision;
            _shownArea = World.Bounds;

            QueueRedraw();
            return;
        }

        if (_shownRevision != field.Revision)
        {
            _shownRevision = field.Revision;
            Fill(field, width);
            _image.SetData(width, width, false, Image.Format.L8, _values);
            _texture.Update(_image);
        }

        // Границы мира правятся в редакторе на ходу, а размер растра при этом мог и не
        // измениться — например, когда сместился только край поля
        var area = World.Bounds;

        if (_shownArea != area)
        {
            _shownArea = area;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Переписать буфер расстояниями. В партии чтение идёт через <see cref="NavGrid.DistanceAt"/>,
    /// то есть с учётом временной маски: только что поставленное здание даёт тень сразу,
    /// не дожидаясь фонового пересчёта снимка.
    /// </summary>
    private void Fill(IClearanceField field, int width)
    {
        int area = width * width;

        for (int i = 0; i < area; i++)
            _values[i] = (byte)Mathf.Min(field.DistanceAt(i), 255);
    }

    /// <summary>Передать шейдеру шкалу и величины, по которым он переводит расстояние в цвет.</summary>
    private void Apply()
    {
        Present(Pixelated);

        if (_ramp == null || _ramp.Gradient != Settings.Tint)
        {
            _ramp = new GradientTexture1D
            {
                Gradient = Settings.Tint,
                Width = RampWidth,
            };
        }

        _material.SetShaderParameter("ramp", _ramp);
        _material.SetShaderParameter("width_px", Mathf.Min(Settings.WidthPx, ShadowSettings.MaxWidthPx));
        _material.SetShaderParameter("falloff", Mathf.Max(Settings.Falloff, 0f));
        _material.SetShaderParameter("cell_px", NavGrid.Cell);
        _material.SetShaderParameter("steps_per_cell", NavGrid.Straight);
    }

    /// <summary>
    /// Выбрать подачу: шейдер и фильтрацию растра. Плавной нужна линейная фильтрация, иначе
    /// спад распадётся на ступеньки по ячейкам сам собой; ступенчатой — «ближайший сосед»,
    /// иначе края ступеней окажутся размыты, и весь смысл подачи пропадёт.
    ///
    /// Сравнение по строке пути дешевле загрузки, поэтому вызов каждый кадр безобиден.
    /// </summary>
    private void Present(bool pixelated)
    {
        string path = pixelated ? PixelShader : SmoothShader;

        if (_shaderPath != path)
        {
            _shaderPath = path;
            _material.Shader = GD.Load<Shader>(path);
        }

        var filter = pixelated ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;

        if (TextureFilter != filter)
            TextureFilter = filter;
    }
}
