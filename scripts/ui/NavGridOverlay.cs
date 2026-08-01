using Godot;

/// <summary>
/// Растр навигации поверх земли: непроходимость, клиренс, связные области, а также
/// прямоугольники строений с зазорами.
///
/// РИСУЕТСЯ ОДНОЙ ТЕКСТУРОЙ. Поле состоит из 27 тысяч ячеек, и вызывать DrawRect на каждую
/// каждый кадр нельзя. Вместо этого поле собирается в изображение 164×164 и растягивается
/// на мир одним вызовом, а пересобирается только при смене ревизии растра или набора
/// признаков отладки. Фильтрация «ближайший сосед» оставляет границы ячеек различимыми.
/// </summary>
public partial class NavGridOverlay : Node2D
{
    /// <summary>Радиус, по которому считаются проходимость и связность на картинке.</summary>
    private const float SampleRadius = Const.Unit * 0.35f;

    private ImageTexture _texture;
    private Image _image;

    private int _drawnRevision = -1;
    private int _drawnMode = -1;

    /// <summary>Рисовали ли в прошлом кадре: погасить нарисованное тоже нужно перерисовкой.</summary>
    private bool _shown;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;

        _image = Image.CreateEmpty(NavGrid.Width, NavGrid.Width, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
    }

    public override void _Process(double delta)
    {
        bool shown = DebugFlags.AnyNav;

        if (shown || _shown)
            QueueRedraw();

        _shown = shown;
    }

    public override void _Draw()
    {
        var gm = GameManager.I;

        if (gm == null || !DebugFlags.AnyNav)
            return;

        if (DebugFlags.NavBlocked || DebugFlags.NavClearance || DebugFlags.NavComponents)
        {
            Refresh(gm);
            DrawTextureRect(_texture, Const.WorldBounds, false);
        }

        if (DebugFlags.Footprints)
            DrawFootprints(gm);
    }

    /// <summary>Пересобрать картинку, если растр или выбранный режим изменились.</summary>
    private void Refresh(GameManager gm)
    {
        int mode = Mode();

        if (_drawnRevision == gm.Nav.Revision && _drawnMode == mode)
            return;

        _drawnRevision = gm.Nav.Revision;
        _drawnMode = mode;

        Repaint(gm);
    }

    private void Repaint(GameManager gm)
    {
        int required = NavGrid.Required(SampleRadius);

        for (int y = 0; y < NavGrid.Width; y++)
        {
            for (int x = 0; x < NavGrid.Width; x++)
            {
                int index = y * NavGrid.Width + x;
                _image.SetPixel(x, y, Tint(gm, index, required));
            }
        }

        _texture.Update(_image);
    }

    private static Color Tint(GameManager gm, int index, int required)
    {
        if (DebugFlags.NavBlocked && gm.Nav.BlockedAt(index))
            return new Color(1f, 0.25f, 0.2f, 0.55f);

        if (DebugFlags.NavComponents)
        {
            int label = gm.Nav.ComponentAt(index, SampleRadius);

            if (label == 0)
                return DebugFlags.NavBlocked
                    ? new Color(0.6f, 0.15f, 0.15f, 0.35f)
                    : new Color(0f, 0f, 0f, 0f);

            // Золотое сечение по кругу цветов: соседние области заведомо не сливаются
            float hue = Mathf.PosMod(label * 0.618034f, 1f);
            return Color.FromHsv(hue, 0.65f, 0.95f, 0.3f);
        }

        if (DebugFlags.NavClearance)
        {
            int distance = gm.Nav.DistanceAt(index);

            if (distance <= 0)
                return new Color(0.4f, 0f, 0f, 0.5f);

            // Насыщаем на восьми ячейках: дальше от стен разница уже ничего не говорит
            float depth = Mathf.Clamp(distance / (8f * 3f), 0f, 1f);
            bool tight = distance < required;

            return tight
                ? new Color(1f, 0.55f, 0.1f, 0.4f)
                : new Color(0.2f, 0.55f + depth * 0.4f, 1f, 0.08f + depth * 0.22f);
        }

        return new Color(0f, 0f, 0f, 0f);
    }

    /// <summary>Отпечаток набора признаков: по нему видно, что картинку надо пересобрать.</summary>
    private static int Mode() =>
        (DebugFlags.NavBlocked ? 1 : 0) |
        (DebugFlags.NavClearance ? 2 : 0) |
        (DebugFlags.NavComponents ? 4 : 0);

    private void DrawFootprints(GameManager gm)
    {
        var body = new Color(0.4f, 0.85f, 1f, 0.85f);
        var margin = new Color(1f, 0.85f, 0.3f, 0.5f);

        foreach (var obstacle in gm.Obstacles.All)
        {
            var shape = gm.Obstacles.ShapeOf(obstacle);

            DrawOutline(shape, body, 1.5f);
            DrawOutline(shape.Grow(Const.BuildMarginPx), margin, 1f);
        }
    }

    /// <summary>Контур повёрнутого прямоугольника: замкнутая ломаная по его углам.</summary>
    private void DrawOutline(in Obb shape, Color color, float width)
    {
        var corners = shape.Corners();

        DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] },
            color, width);
    }
}
