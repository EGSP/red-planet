using Godot;

/// <summary>
/// Дорожка событий: отрезок времени партии, нарисованный слева направо, с курсором
/// текущего мига и ромбами волн по обе стороны от него.
///
/// ЧТО ИМЕННО ЗДЕСЬ РИСУЕТСЯ. Показывается ровно то, что подсистема волн знает заранее,
/// и ни секундой больше: ближайшая волна (её срок задан таймером отдыха) и очаги текущей
/// волны, которые набраны, но ещё не выведены на карту. Волны за ближайшей не показаны
/// потому, что их не существует: и состав, и срок следующей волны выбираются в миг запуска
/// предыдущей. Рисовать их пришлось бы догадкой, а догадка на шкале времени читается
/// как обещание.
///
/// ПРОШЛОЕ ПОКАЗЫВАЕТСЯ УЗКОЙ ПОЛОСОЙ. Оно нужно не как история — история есть
/// в <see cref="DebugPanel"/>, — а как ответ на вопрос «волна уже пришла или ещё нет»
/// в те секунды, когда ромб стоит вплотную к курсору.
///
/// МАСШТАБ ЕДИН НА ВСЮ ШИРИНУ. Курсор стоит на доле, которую прошлое составляет от суммы
/// обоих интервалов, и отдельной настройки положения нет: она означала бы разные масштабы
/// по сторонам от курсора, а значит и скачок скорости отметки при его пересечении.
/// Отметка идёт от правого края до левого равномерно.
///
/// В ПРОШЛОМ ШАГ ДЕЛЕНИЯ СВОЙ, мельче. Отрезок прошлого короток, и общий шаг в него
/// попросту не попадает: при десяти показываемых секундах ближайшее деление
/// пятнадцатисекундной сетки лежит за краем, и левая часть полосы оставалась пустой.
///
/// ШКАЛА ОТСЧИТЫВАЕТСЯ ОТ ТЕКУЩЕГО МИГА, а не от начала партии. Ноль стоит под курсором
/// и с места не сходит, поэтому деления неподвижны, а движутся только отметки событий.
/// Привязка делений к часам партии давала бы обратное: события стояли бы, а разметка
/// уползала влево, и глазу пришлось бы следить за обеими.
///
/// РАЗМЕР РОМБА ОЗНАЧАЕТ ТЕРРОР, при котором волна пришла или придёт, отложенный
/// между <see cref="TerrorAtSmallest"/> и <see cref="TerrorAtLargest"/>. Размер выбран
/// потому, что он читается боковым зрением: на полосу в один взгляд смотрят, чтобы
/// понять, крупное ли идёт событие, а не чтобы прочесть число.
/// </summary>
public partial class EventTrack : Control
{
    private static readonly Color Background = new(0.09f, 0.10f, 0.12f, 0.9f);
    private static readonly Color PastTint = new(1f, 1f, 1f, 0.05f);
    private static readonly Color TickMinor = new(0.6f, 0.65f, 0.72f, 0.2f);
    private static readonly Color TickMajor = new(0.6f, 0.65f, 0.72f, 0.42f);
    private static readonly Color CursorColor = new(0.55f, 0.95f, 1f);
    private static readonly Color WaveColor = new(1f, 0.62f, 0.45f);
    private static readonly Color PastWaveColor = new(1f, 0.62f, 0.45f, 0.4f);
    private static readonly Color GroupColor = new(1f, 0.85f, 0.5f, 0.75f);

    /// <summary>Шаг мелкого деления в будущем, секунд. Крупное стоит вчетверо реже.</summary>
    private const float MinorTickSeconds = 15f;

    private const int MajorEvery = 4;

    /// <summary>Шаг деления в прошлом, секунд.</summary>
    private const float PastTickSeconds = 5f;

    /// <summary>Полудиагональ ромба при наименьшем и при наибольшем терроре, пикселей.</summary>
    private const float SmallestRadius = 3f;
    private const float LargestRadius = 7f;

    /// <summary>Сколько прошедшего времени показывать, секунд. Задаёт <see cref="EventBar"/>.</summary>
    public float PastSeconds = 10f;

    /// <summary>Сколько предстоящего времени показывать, секунд. Задаёт <see cref="EventBar"/>.</summary>
    public float FutureSeconds = 120f;

    /// <summary>Террор, при котором ромб наименьший. Задаёт <see cref="EventBar"/>.</summary>
    public float TerrorAtSmallest;

    /// <summary>Террор, при котором ромб наибольший. Задаёт <see cref="EventBar"/>.</summary>
    public float TerrorAtLargest = 500f;

    // Геометрия кадра. Держится полем, потому что нужна всем частям отрисовки, а считается
    // однажды: передавать её доводами в каждый метод значило бы переписывать подпись
    // каждого при любой правке шкалы
    private float _cursorX;
    private float _scale;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    // Перерисовка каждый кадр: отметки движутся непрерывно, и любое разрежение
    // превратило бы плавное движение в подёргивание
    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var size = Size;

        if (size.X <= 1f || size.Y <= 1f)
            return;

        DrawRect(new Rect2(Vector2.Zero, size), Background);

        var waves = GameManager.I?.System<WaveSystem>();

        if (waves == null)
            return;

        float past = Mathf.Max(PastSeconds, 0f);
        float future = Mathf.Max(FutureSeconds, 1f);

        _scale = size.X / (past + future);
        _cursorX = past * _scale;

        DrawRect(new Rect2(0f, 0f, _cursorX, size.Y), PastTint);

        DrawTicks(size, past, future);
        DrawHistory(waves, size, past);
        DrawPending(waves, size, future);
        DrawUpcoming(waves, size, future);

        // Курсор рисуется последним, чтобы ромб волны, подошедшей к нулю, не перекрывал
        // границу между прошлым и будущим
        DrawCursor(size);
    }

    /// <summary>Место отметки, отстоящей от текущего мига на столько секунд вперёд.</summary>
    private float At(float offset) => _cursorX + offset * _scale;

    // ── Шкала ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Деления по обе стороны от нуля. Отсчёт ведётся от курсора, поэтому крупное деление
    /// приходится ровно на минуту ожидания и стоит на месте.
    /// </summary>
    private void DrawTicks(Vector2 size, float past, float future)
    {
        int ahead = Mathf.FloorToInt(future / MinorTickSeconds);

        for (int step = 1; step <= ahead; step++)
        {
            bool major = step % MajorEvery == 0;
            Tick(At(step * MinorTickSeconds), size, major ? TickMajor : TickMinor, major);
        }

        int back = Mathf.FloorToInt(past / PastTickSeconds);

        for (int step = 1; step <= back; step++)
            Tick(At(-step * PastTickSeconds), size, TickMinor, false);
    }

    private void Tick(float x, Vector2 size, Color color, bool full)
    {
        float height = full ? size.Y : size.Y * 0.35f;

        DrawRect(new Rect2(x, size.Y - height, 1f, height), color);
    }

    /// <summary>Курсор текущего мига: черта во всю высоту с засечками сверху и снизу.</summary>
    private void DrawCursor(Vector2 size)
    {
        float x = _cursorX;

        DrawRect(new Rect2(x - 1f, 0f, 2f, size.Y), CursorColor);

        DrawColoredPolygon(new[]
        {
            new Vector2(x - 4f, 0f),
            new Vector2(x + 4f, 0f),
            new Vector2(x, 4f),
        }, CursorColor);

        DrawColoredPolygon(new[]
        {
            new Vector2(x - 4f, size.Y),
            new Vector2(x + 4f, size.Y),
            new Vector2(x, size.Y - 4f),
        }, CursorColor);
    }

    // ── Отметки ───────────────────────────────────────────────────────────────────

    /// <summary>Волны, прошедшие внутри показываемого отрезка прошлого.</summary>
    private void DrawHistory(WaveSystem waves, Vector2 size, float past)
    {
        var history = waves.History;
        float now = waves.GameTime;

        for (int i = 0; i < history.Count; i++)
        {
            float ago = history[i].GameTime - now;

            if (ago < -past || ago > 0f)
                continue;

            Diamond(At(ago), size, PastWaveColor, history[i].Terror);
        }
    }

    /// <summary>Очаги текущей волны, которые ещё не вышли на карту.</summary>
    private void DrawPending(WaveSystem waves, Vector2 size, float future)
    {
        for (int i = 0; i < waves.PendingCount; i++)
        {
            float left = waves.PendingIn(i);

            if (left > future)
                continue;

            DrawRect(new Rect2(At(left) - 1f, size.Y * 0.5f, 2f, size.Y * 0.5f), GroupColor);
        }
    }

    /// <summary>
    /// Ближайшая волна. Её террор ещё не известен — волна отбирается в миг запуска, —
    /// поэтому размер ромба берётся по текущему сглаженному показателю: это лучшая оценка
    /// из имеющихся, и она же используется при отборе.
    ///
    /// Когда срок волны выходит за правый край, вместо ромба рисуется стрелка у края:
    /// иначе волна, до которой ещё три минуты, прижималась бы к краю и выглядела бы
    /// наступающей.
    /// </summary>
    private void DrawUpcoming(WaveSystem waves, Vector2 size, float future)
    {
        float left = waves.TimeLeft;

        if (left > future)
        {
            DrawColoredPolygon(new[]
            {
                new Vector2(size.X - 8f, size.Y * 0.5f - 4f),
                new Vector2(size.X - 1f, size.Y * 0.5f),
                new Vector2(size.X - 8f, size.Y * 0.5f + 4f),
            }, new Color(WaveColor, 0.55f));

            return;
        }

        float terror = GameManager.I?.System<TerrorSystem>()?.Smoothed ?? 0f;
        Diamond(At(left), size, WaveColor, terror);
    }

    /// <summary>Отметка волны: ромб, растущий с террором.</summary>
    private void Diamond(float x, Vector2 size, Color color, float terror)
    {
        float span = TerrorAtLargest - TerrorAtSmallest;
        float ratio = span > 0f ? Mathf.Clamp((terror - TerrorAtSmallest) / span, 0f, 1f) : 0f;

        // Ромб не должен вылезать за дорожку даже при узкой полосе: половина высоты
        // здесь верхняя граница, а не пожелание
        float radius = Mathf.Min(Mathf.Lerp(SmallestRadius, LargestRadius, ratio), size.Y * 0.5f);
        float middle = size.Y * 0.5f;

        DrawColoredPolygon(new[]
        {
            new Vector2(x, middle - radius),
            new Vector2(x + radius, middle),
            new Vector2(x, middle + radius),
            new Vector2(x - radius, middle),
        }, color);
    }
}
