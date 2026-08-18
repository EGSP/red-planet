using Godot;

/// <summary>
/// Лента скорости выделенного юнита за последние секунды: график от нуля до полного хода.
///
/// ЗАЧЕМ ОНА НУЖНА. Скорость выводится из трёх правил сразу — торможения перед целью,
/// доли хода по расхождению курса с осью корпуса и ограничения по радиусу разворота, —
/// и на глаз они неразличимы: видно только, что юнит где-то ускорился, а где-то замедлился.
/// Числом в панели это тоже не читается, потому что величина меняется каждый кадр. График
/// же показывает форму: провал на повороте, ступень на подходе, дрожание от локального слоя.
///
/// ПИШЕТ ТОЛЬКО ПРИ ОДНОМ ВЫДЕЛЕННОМ. Складывать скорости отряда бессмысленно, а выбирать
/// из отряда представителя — значит показывать величину, о принадлежности которой зритель
/// не осведомлён. Смена выделения и его снятие стирают накопленное: лента принадлежит
/// юниту, а не экрану.
///
/// Показывается вместе с панелью отладки: лента живёт внутри её каркаса и потому включается
/// и гаснет вместе с ним, не заводя собственной клавиши.
/// </summary>
public partial class SpeedGraph : Control
{
    /// <summary>Сколько секунд держится в памяти.</summary>
    private const float Window = 5f;

    /// <summary>
    /// Ёмкость кольца. С запасом относительно окна при обычной частоте кадров: переполнение
    /// означало бы потерю старых замеров, а не порчу картины, но запас дешевле разбирательства.
    /// </summary>
    private const int Capacity = 1024;

    private const int GraphHeight = 84;

    private static readonly Color Background = new(0.05f, 0.07f, 0.1f, 0.75f);
    private static readonly Color Border = new(0.35f, 0.45f, 0.6f, 0.8f);
    private static readonly Color Grid = new(0.35f, 0.45f, 0.6f, 0.35f);
    private static readonly Color Ink = new(0.55f, 0.9f, 0.7f);
    private static readonly Color Text = new(0.8f, 0.85f, 0.9f);

    private readonly float[] _time = new float[Capacity];
    private readonly float[] _speed = new float[Capacity];

    private int _head;
    private int _count;

    /// <summary>Чья это лента. Смена наблюдаемого стирает накопленное.</summary>
    private IMobile _watched;

    /// <summary>Часы ленты. Отсчитываются от начала наблюдения, а не от запуска игры.</summary>
    private float _clock;

    /// <summary>Полный ход наблюдаемого: им задана верхняя граница шкалы.</summary>
    private float _top;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Справа по центру края: левый край занят самой панелью отладки, а низ — панелью
        // выделения, без которой выбирать наблюдаемого нечем
        AnchorLeft = 1f;
        AnchorRight = 1f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -232f;
        OffsetRight = -12f;
        OffsetTop = -(GraphHeight + 34f) * 0.5f;
        OffsetBottom = (GraphHeight + 34f) * 0.5f;
    }

    public override void _Process(double delta)
    {
        // Скрытая лента не пишет: панель закрыта, значит наблюдения не ведётся, и хранить
        // отрезок, снятый неизвестно когда, незачем
        if (!IsVisibleInTree())
        {
            Forget();
            return;
        }

        Sample((float)delta);
        QueueRedraw();
    }

    /// <summary>Кого наблюдать сейчас: ровно один выделенный подвижный юнит либо никто.</summary>
    private static IMobile Watchable()
    {
        var selected = GameManager.I?.Command?.Selected;

        if (selected is not { Count: 1 })
            return null;

        if (selected[0] is not IMobile mobile || !Alive.Is(mobile as Node))
            return null;

        return mobile.Definition is { SpeedPx: > 0f } ? mobile : null;
    }

    private void Sample(float delta)
    {
        var mobile = Watchable();

        if (mobile == null)
        {
            Forget();
            return;
        }

        if (!ReferenceEquals(mobile, _watched))
        {
            Forget();
            _watched = mobile;
            _top = mobile.Definition.SpeedPx;
        }

        _clock += delta;

        int at = (_head + _count) % Capacity;
        _time[at] = _clock;
        _speed[at] = mobile.Movement.Velocity.Length();

        if (_count < Capacity)
            _count++;
        else
            _head = (_head + 1) % Capacity;

        // Ушедшее за окно снимается с головы кольца: держать его значило бы растягивать
        // ту же ширину на всё большее время
        while (_count > 0 && _clock - _time[_head] > Window)
        {
            _head = (_head + 1) % Capacity;
            _count--;
        }
    }

    private void Forget()
    {
        _watched = null;
        _head = 0;
        _count = 0;
        _clock = 0f;
        _top = 0f;
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

        if (_watched == null || _top <= 0f)
        {
            DrawString(font, new Vector2(8f, 20f), "скорость: выделен не один юнит",
                HorizontalAlignment.Left, -1f, 11, Text);
            return;
        }

        float now = _count > 0 ? _speed[(_head + _count - 1) % Capacity] : 0f;

        DrawString(font, new Vector2(8f, 14f),
            $"{_watched.Definition.DisplayName}   {now:0} из {_top:0} px/с",
            HorizontalAlignment.Left, -1f, 11, Text);

        DrawString(font, new Vector2(8f, 28f),
            $"окно {Window:0} с, доля хода {now / _top:0.00}",
            HorizontalAlignment.Left, -1f, 10, Text);

        Frame(plot, font);
        Curve(plot);
    }

    /// <summary>Рамка поля с подписями границ шкалы: без них высота линии ничего не значит.</summary>
    private void Frame(Rect2 plot, Font font)
    {
        DrawRect(plot, Grid, false);

        // Половина полного хода: по ней читается, насколько глубок провал на повороте
        float middle = plot.Position.Y + plot.Size.Y * 0.5f;
        DrawLine(new Vector2(plot.Position.X, middle), new Vector2(plot.End.X, middle), Grid);

        DrawString(font, new Vector2(plot.Position.X + 3f, plot.Position.Y + 10f),
            $"{_top:0}", HorizontalAlignment.Left, -1f, 9, Grid);

        DrawString(font, new Vector2(plot.Position.X + 3f, plot.End.Y - 3f),
            "0", HorizontalAlignment.Left, -1f, 9, Grid);
    }

    /// <summary>
    /// Сама лента. Время откладывается вправо, поэтому последний замер стоит у правого края,
    /// а окно уезжает влево по мере наблюдения.
    /// </summary>
    private void Curve(Rect2 plot)
    {
        if (_count < 2)
            return;

        var points = new Vector2[_count];

        for (int i = 0; i < _count; i++)
        {
            int at = (_head + i) % Capacity;
            float age = _clock - _time[at];
            float x = plot.End.X - plot.Size.X * (age / Window);
            float value = Mathf.Clamp(_speed[at] / _top, 0f, 1f);

            points[i] = new Vector2(x, plot.End.Y - plot.Size.Y * value);
        }

        DrawPolyline(points, Ink, 1.5f, true);
    }
}
