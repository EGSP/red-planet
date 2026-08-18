using Godot;

/// <summary>
/// Частота кадров за последние секунды: число и график у левого верхнего угла.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ ПАНЕЛИ ОТЛАДКИ. Панель отладки и панель песочницы занимают одно место
/// экрана и потому открыты не более чем по одной (<see cref="ToolPanel"/>). Частота кадров
/// нужна не вместо них, а вместе с ними: провал ищут, глядя на растр, на пути или на волну,
/// то есть при открытой панели отладки. Поэтому лента заведена самостоятельным слоем
/// со своей клавишей и в набор взаимоисключающих панелей не входит.
///
/// ЧТО ИМЕННО ЗАПИСЫВАЕТСЯ. Длительность кадра, обращённая в частоту, а не сглаженный
/// показатель движка: провал на один кадр — это и есть то, ради чего график открывают,
/// а сглаживание его прячет. Сглаженное число движка показано рядом отдельной величиной,
/// потому что по нему судят об общем самочувствии.
/// </summary>
public partial class FpsPanel : CanvasLayer
{
    /// <summary>Сколько секунд держится в памяти.</summary>
    public const float Window = 5f;

    public override void _Ready()
    {
        // Тот же слой, что у служебных панелей: частота кадров читается поверх всего
        Layer = 30;

        var frame = new UiFrame();
        AddChild(frame);
        frame.AddChild(new FpsGraph());

        frame.Visible = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(InputActions.DebugFps))
            return;

        var frame = GetChildCount() > 0 ? GetChild<Control>(0) : null;

        if (frame == null)
            return;

        frame.Visible = !frame.Visible;
        GetViewport().SetInputAsHandled();
    }
}

/// <summary>
/// Само поле графика. Отделено от слоя по той же причине, по какой отделена лента скорости:
/// рисование в <see cref="Control._Draw"/> требует собственного прямоугольника, а слой
/// его не имеет.
/// </summary>
public partial class FpsGraph : Control
{
    private const int Capacity = 1024;

    private const int GraphHeight = 44;

    /// <summary>
    /// Нижняя граница верха шкалы. Шкала подстраивается под наблюдаемый разброс, но не ниже
    /// этого значения: иначе ровные шестьдесят кадров рисовались бы у самого верха поля,
    /// и всякое мелкое колебание выглядело бы обвалом.
    /// </summary>
    private const float MinTop = 75f;

    private static readonly Color Background = new(0.05f, 0.07f, 0.1f, 0.75f);
    private static readonly Color Border = new(0.35f, 0.45f, 0.6f, 0.8f);
    private static readonly Color Grid = new(0.35f, 0.45f, 0.6f, 0.35f);
    private static readonly Color Ink = new(0.95f, 0.8f, 0.4f);
    private static readonly Color Text = new(0.8f, 0.85f, 0.9f);

    private readonly float[] _time = new float[Capacity];
    private readonly float[] _rate = new float[Capacity];

    private int _head;
    private int _count;
    private float _clock;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Левый верхний угол, над панелью отладки: та отступает от верха на 56 пикселей,
        // и лента укладывается в этот просвет целиком
        AnchorLeft = 0f;
        AnchorRight = 0f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = 12f;
        OffsetRight = 12f + 220f;
        OffsetTop = 4f;
        OffsetBottom = 4f + GraphHeight + 16f;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
            return;

        Sample((float)delta);
        QueueRedraw();
    }

    private void Sample(float delta)
    {
        if (delta <= 0f)
            return;

        _clock += delta;

        int at = (_head + _count) % Capacity;
        _time[at] = _clock;
        _rate[at] = 1f / delta;

        if (_count < Capacity)
            _count++;
        else
            _head = (_head + 1) % Capacity;

        while (_count > 0 && _clock - _time[_head] > FpsPanel.Window)
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }
    }

    public override void _Draw()
    {
        var size = Size;

        if (size.X <= 1f || size.Y <= 1f)
            return;

        var font = ThemeDB.FallbackFont;
        var plot = new Rect2(0f, size.Y - GraphHeight, size.X, GraphHeight);

        DrawRect(new Rect2(Vector2.Zero, size), Background);
        DrawRect(new Rect2(Vector2.Zero, size), Border, false);

        float worst = float.MaxValue;
        float best = 0f;

        for (int i = 0; i < _count; i++)
        {
            float value = _rate[(_head + i) % Capacity];
            worst = Mathf.Min(worst, value);
            best = Mathf.Max(best, value);
        }

        if (_count == 0)
        {
            worst = 0f;
            best = 0f;
        }

        DrawString(font, new Vector2(6f, 12f),
            $"{Engine.GetFramesPerSecond():0} к/с   худший кадр {worst:0}   окно {FpsPanel.Window:0} с",
            HorizontalAlignment.Left, -1f, 10, Text);

        float top = Mathf.Max(MinTop, Mathf.Ceil(best / 30f) * 30f);

        DrawRect(plot, Grid, false);

        // Черта шестидесяти кадров: по ней сразу видно, держится ли частота у обычной
        // развёртки или ходит под нею
        float sixty = plot.End.Y - plot.Size.Y * Mathf.Min(60f / top, 1f);
        DrawLine(new Vector2(plot.Position.X, sixty), new Vector2(plot.End.X, sixty), Grid);

        DrawString(font, new Vector2(plot.Position.X + 3f, plot.Position.Y + 9f),
            $"{top:0}", HorizontalAlignment.Left, -1f, 9, Grid);

        Curve(plot, top);
    }

    private void Curve(Rect2 plot, float top)
    {
        if (_count < 2)
            return;

        var points = new Vector2[_count];

        for (int i = 0; i < _count; i++)
        {
            int at = (_head + i) % Capacity;
            float age = _clock - _time[at];
            float x = plot.End.X - plot.Size.X * (age / FpsPanel.Window);
            float value = Mathf.Clamp(_rate[at] / top, 0f, 1f);

            points[i] = new Vector2(x, plot.End.Y - plot.Size.Y * value);
        }

        DrawPolyline(points, Ink, 1.5f, true);
    }
}
