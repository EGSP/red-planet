using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

/// <summary>
/// Панель формы волны: где именно появится наступление и как оно будет расставлено.
///
/// ЗАЧЕМ ОНА НУЖНА. Числа секции <c>[spawn]</c> — углы двух дуг, глубина, промежуток,
/// число очагов — задают геометрию, которую по отдельным полям инспектора представить
/// нельзя: ширина фронта зависит от угла и радиуса разом, а число рядов — от глубины,
/// промежутка и размера волны. Панель показывает итог сразу, как измерительное поле
/// показывает габариты сущности.
///
/// ЧЕМ ОНА ОТЛИЧАЕТСЯ ОТ СТЕНДА ПРЕДПРОСМОТРА МИРА. Стенд <c>Preview.tscn</c> показывает
/// партию целиком: кольца руды, точки метала, облака. Здесь рисуются только окружность
/// появления, поле застройки и само построение, зато прямо в редакторе контента и по
/// правимому черновику, включая не прошедший проверку.
///
/// КООРДИНАТЫ. Мировые пиксели переводятся в экранные как <c>world * zoom + pan</c>; центр
/// мира (точка высадки игрока) лежит в начале координат, поэтому волна всегда идёт к нему.
/// </summary>
[Tool]
public partial class WaveShapeView : Control
{
    private static readonly Color Background = new(0.10f, 0.11f, 0.14f);
    private static readonly Color FieldColor = new(1f, 0.58f, 0.18f, 0.55f);
    private static readonly Color SpawnColor = new(0.92f, 0.28f, 0.88f, 0.6f);
    private static readonly Color SectorStroke = new(1f, 0.85f, 0.22f, 0.85f);
    private static readonly Color SectorFill = new(1f, 0.85f, 0.22f, 0.10f);
    private static readonly Color TextColor = new(0.88f, 0.92f, 1f);

    private WaveShapePreview _preview;
    private string _title = "";
    private string _note = "";

    private float _zoom = 0.05f;
    private Vector2 _pan;
    private bool _panning;
    private Vector2 _panFrom;
    private Vector2 _panMouse;
    private bool _viewInitialized;

    /// <summary>Было ли построение вписано в панель хотя бы раз.</summary>
    private bool _fitted;

    /// <summary>Показывать ли состав волны, а не одни границы секторов.</summary>
    public bool ShowUnits { get; set; } = true;

    /// <summary>Показывать ли окружность появления и границу поля застройки.</summary>
    public bool ShowWorld { get; set; } = true;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(320, 200);
    }

    /// <summary>
    /// Задать показываемое построение. <paramref name="note"/> объясняет пустую панель либо
    /// предупреждает, что волна собрана из черновика.
    /// </summary>
    public void SetPreview(WaveShapePreview preview, string title, string note)
    {
        _preview = preview;
        _title = title ?? "";
        _note = note ?? "";

        // Первое построение вписывается само: подбирать масштаб вручную ради того, чтобы
        // вообще увидеть форму, пользователю незачем. Дальше камера остаётся на месте,
        // иначе правка чисел сбрасывала бы выбранный масштаб.
        if (preview != null && !_fitted)
        {
            _fitted = true;
            Fit();
            return;
        }

        QueueRedraw();
    }

    /// <summary>Вписать построение целиком в размер панели.</summary>
    public void Fit()
    {
        if (_preview == null || Size.X <= 0f || Size.Y <= 0f)
            return;

        // Наружная граница — дальняя дуга самого дальнего ряда либо окружность появления,
        // смотря что больше: ряды продолжаются за дальнюю дугу, когда состав не поместился.
        float extent = Mathf.Max(
            _preview.SpawnRadiusPx,
            _preview.Shape.NearRadiusPx + _preview.Shape.DepthPx);
        foreach (var (position, definition) in _preview.Slots())
            extent = Mathf.Max(extent, position.Length() + definition.RadiusPx);

        extent = Mathf.Max(extent, Const.Unit);
        float margin = 48f;
        _zoom = Mathf.Clamp(
            Mathf.Min(Size.X - margin, Size.Y - margin) / (extent * 2f), 0.005f, 4f);
        _pan = Size * 0.5f;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized)
            return;

        if (!_viewInitialized && Size.X > 0f && Size.Y > 0f)
        {
            _viewInitialized = true;
            Fit();
        }

        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                ZoomAt(mouse.Position, 1.1f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                ZoomAt(mouse.Position, 1f / 1.1f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex is MouseButton.Middle or MouseButton.Left)
            {
                _panning = mouse.Pressed;
                _panFrom = _pan;
                _panMouse = mouse.Position;
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _panning)
        {
            _pan = _panFrom + (motion.Position - _panMouse);
            QueueRedraw();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);

        if (_preview == null)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(16f, 28f),
                _note.Length > 0 ? _note : "Select a wave to see its spawn shape",
                HorizontalAlignment.Left, Size.X - 32f, 13, new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        if (ShowWorld)
            DrawWorld();

        foreach (float angle in _preview.GroupAngles())
            DrawSector(angle);

        if (ShowUnits)
            DrawUnits();

        DrawCenter();
        DrawScaleBar();
        DrawLegend();
    }

    // ── Части изображения ─────────────────────────────────────────────────────────

    /// <summary>Поле застройки и окружность появления: между ними волна и проходит.</summary>
    private void DrawWorld()
    {
        var origin = WorldToScreen(Vector2.Zero);
        DrawArc(origin, _preview.FieldRadiusPx * _zoom, 0f, Mathf.Tau, 96, FieldColor, 1.5f, true);
        DrawArc(origin, _preview.SpawnRadiusPx * _zoom, 0f, Mathf.Tau, 128, SpawnColor, 1.5f, true);

        DrawString(ThemeDB.FallbackFont,
            origin + new Vector2(_preview.FieldRadiusPx * _zoom + 4f, -4f),
            "build field", HorizontalAlignment.Left, -1, 11, FieldColor);
        DrawString(ThemeDB.FallbackFont,
            origin + new Vector2(_preview.SpawnRadiusPx * _zoom + 4f, -4f),
            "spawn radius", HorizontalAlignment.Left, -1, 11, SpawnColor);
    }

    /// <summary>
    /// Кольцевой сектор одного очага. Обе дуги строятся по <see cref="WaveShape.ArcAt"/>,
    /// поэтому сходящаяся и расходящаяся формы видны так же, как их считает игра.
    /// </summary>
    private void DrawSector(float center)
    {
        var shape = _preview.Shape;
        float near = shape.NearRadiusPx;
        float far = near + Mathf.Max(shape.DepthPx, 1f);
        float nearHalf = shape.ArcAt(0f) * 0.5f;
        float farHalf = shape.ArcAt(1f) * 0.5f;

        var outline = new List<Vector2>();
        AddArcPoints(outline, near, center - nearHalf, center + nearHalf);
        AddArcPoints(outline, far, center + farHalf, center - farHalf);

        var points = outline.ToArray();
        if (points.Length >= 3)
            DrawColoredPolygon(points, SectorFill);

        var loop = new Vector2[points.Length + 1];
        points.CopyTo(loop, 0);
        loop[^1] = points[0];
        DrawPolyline(loop, SectorStroke, 1.5f, true);

        // Ряды показывают, во сколько слоёв встанет волна при этой глубине и промежутке.
        int rows = _preview.Rows;
        for (int row = 1; row < rows; row++)
        {
            float t = rows > 1 ? row / (float)(rows - 1) : 0f;
            float radius = near + row * shape.SpacingPx;
            float half = shape.ArcAt(t) * 0.5f;
            var line = new List<Vector2>();
            AddArcPoints(line, radius, center - half, center + half);
            if (line.Count >= 2)
                DrawPolyline(line.ToArray(), new Color(1f, 0.85f, 0.22f, 0.18f), 1f, true);
        }
    }

    /// <summary>Состав волны: круг цвета вида и радиуса его корпуса.</summary>
    private void DrawUnits()
    {
        foreach (var (position, definition) in _preview.Slots())
        {
            var screen = WorldToScreen(position);
            float radius = Mathf.Max(definition.RadiusPx * _zoom, 1.5f);
            DrawCircle(screen, radius, definition.Color);
            if (radius >= 3f)
                DrawArc(screen, radius, 0f, Mathf.Tau, 16, new Color(0f, 0f, 0f, 0.35f), 1f, true);
        }
    }

    /// <summary>Точка высадки игрока: к ней и направлено наступление.</summary>
    private void DrawCenter()
    {
        var origin = WorldToScreen(Vector2.Zero);
        var color = new Color(0.55f, 0.75f, 1f, 0.9f);
        DrawLine(origin - new Vector2(6f, 0f), origin + new Vector2(6f, 0f), color, 1.5f);
        DrawLine(origin - new Vector2(0f, 6f), origin + new Vector2(0f, 6f), color, 1.5f);
    }

    /// <summary>Отметка масштаба в клетках: без неё размеры на глаз не читаются.</summary>
    private void DrawScaleBar()
    {
        // Шаг подбирается так, чтобы отметка занимала около сотни пикселей при любом зуме.
        float cells = 1f;
        while (cells * Const.Unit * _zoom < 60f && cells < 4096f)
            cells *= 2f;

        float length = cells * Const.Unit * _zoom;
        var pos = new Vector2(16f, Size.Y - 16f);
        DrawLine(pos, pos + new Vector2(length, 0f), TextColor, 2f);
        DrawLine(pos, pos - new Vector2(0f, 5f), TextColor, 2f);
        DrawLine(pos + new Vector2(length, 0f), pos + new Vector2(length, -5f), TextColor, 2f);
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(0f, -8f),
            $"{cells.ToString("0.##", CultureInfo.InvariantCulture)} cells",
            HorizontalAlignment.Left, -1, 11, TextColor);
    }

    /// <summary>Числа, которые нельзя прочитать с картинки: бюджет, состав, ряды, очаги.</summary>
    private void DrawLegend()
    {
        var shape = _preview.Shape;
        var lines = new List<string>
        {
            $"budget {_preview.Budget.ToString("0.#", CultureInfo.InvariantCulture)}"
            + $" · spent {_preview.Spent.ToString("0.#", CultureInfo.InvariantCulture)}"
            + $" · {_preview.Ordered.Count} units",
            $"groups {_preview.Groups} × {shape.GroupsArcDegrees.ToString("0.#", CultureInfo.InvariantCulture)}°"
            + $" · delay {shape.GroupDelaySeconds.ToString("0.##", CultureInfo.InvariantCulture)} s",
            $"arcs {shape.NearArcDegrees.ToString("0.#", CultureInfo.InvariantCulture)}°"
            + $" → {shape.FarArcDegrees.ToString("0.#", CultureInfo.InvariantCulture)}°"
            + $" · rows {_preview.Rows}"
            + $" · spacing {shape.SpacingCells.ToString("0.##", CultureInfo.InvariantCulture)}",
            _preview.Composition,
        };

        if (!_preview.Applicable)
            lines.Add("this wave does not fit the probed terror");
        if (_preview.FromDraft)
            lines.Add("read from the draft: the file does not pass content checks yet");
        if (_note.Length > 0)
            lines.Add(_note);

        float y = 18f;
        if (_title.Length > 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(12f, y), _title,
                HorizontalAlignment.Left, Size.X - 24f, 13, new Color(0.75f, 0.86f, 1f));
            y += 17f;
        }

        foreach (string line in lines)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(12f, y), line,
                HorizontalAlignment.Left, Size.X - 24f, 12, new Color(0.88f, 0.92f, 1f, 0.85f));
            y += 15f;
        }
    }

    // ── Координаты ────────────────────────────────────────────────────────────────

    /// <summary>Точки дуги от одного угла к другому; направление обхода задаёт порядок.</summary>
    private void AddArcPoints(List<Vector2> points, float radius, float from, float to)
    {
        const int segments = 48;
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(from, to, i / (float)segments);
            points.Add(WorldToScreen(Heading.Forward(angle) * radius));
        }
    }

    private void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        _zoom = Mathf.Clamp(_zoom * factor, 0.005f, 4f);
        var after = ScreenToWorld(screen);
        _pan += (after - before) * _zoom;
        QueueRedraw();
    }

    private Vector2 WorldToScreen(Vector2 world) => world * _zoom + _pan;

    private Vector2 ScreenToWorld(Vector2 screen) => (screen - _pan) / _zoom;
}
