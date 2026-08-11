using Godot;

/// <summary>
/// Единственный владелец преобразования экранной позиции мыши в мировые координаты.
///
/// Исполняется в графическом цикле (<see cref="UpdateCycle.Process"/>) раньше
/// <see cref="CommandSystem"/>: к моменту шага приказов мировые точки уже пересчитаны
/// по текущему canvas transform (в том числе когда камера ехала без движения мыши).
///
/// Движение принимается через <see cref="Node._Input"/>, а не через
/// <see cref="Node._UnhandledInput"/>: элементы UI не должны останавливать отслеживание
/// экранной позиции. Игровые решения по кнопкам берут точную позицию события через
/// <see cref="WorldFromEvent"/> и никогда не читают прогнозируемую точку.
/// </summary>
public partial class CursorSystem : GameSystem
{
    /// <summary>
    /// Управление глобальным <see cref="Input.UseAccumulatedInput"/>.
    /// По умолчанию выключено: каждое движение приходит отдельным событием, удобнее
    /// сравнивать задержку. При удалении системы прежнее значение восстанавливается,
    /// если глобальный флаг никто не успел сменить.
    /// </summary>
    [Export]
    public bool UseAccumulatedInput
    {
        get => _useAccumulatedInput;
        set
        {
            _useAccumulatedInput = value;

            // Setter вызывается и при загрузке сцены до регистрации. После регистрации
            // он позволяет сравнивать режимы из удалённого инспектора без перезапуска.
            if (_appliedAccumulated)
                Input.UseAccumulatedInput = value;
        }
    }

    /// <summary>
    /// Экстраполяция только для <see cref="VisualWorldPosition"/>.
    /// На игровые решения не влияет. По умолчанию выключена.
    /// </summary>
    [Export] public bool Extrapolate;

    /// <summary>Горизонт прогноза, секунды. Дальше этого интервала позиция не уводится.</summary>
    [Export] public float ExtrapolationHorizonSec = 0.02f;

    /// <summary>Предел смещения прогноза от фактической экранной точки, пиксели.</summary>
    [Export] public float ExtrapolationMaxOffsetPx = 32f;

    /// <summary>
    /// После стольких секунд без движения прогноз отключается: скорость считается устаревшей.
    /// </summary>
    [Export] public float VelocityStaleSec = 0.05f;

    /// <summary>Ниже этой экранной скорости (px/s) мышь считается остановившейся.</summary>
    [Export] public float MinVelocityPxPerSec = 8f;

    /// <summary>
    /// Фактическая мировая позиция под курсором: последнее известное экранное положение,
    /// переведённое текущим canvas transform. Без прогноза.
    /// </summary>
    public Vector2 ActualWorldPosition { get; private set; }

    /// <summary>
    /// Визуальная мировая позиция: фактическая либо ограниченный прогноз по собственной
    /// оценке скорости. Только для призрака, рамки и прочего представления.
    /// </summary>
    public Vector2 VisualWorldPosition { get; private set; }

    private Vector2 _screenPos;
    private bool _hasScreenPos;

    private Vector2 _prevScreenPos;
    private ulong _prevMotionUsec;
    private bool _hasMotionSample;

    private Vector2 _screenVelocityPxPerSec;
    private ulong _lastMotionUsec;
    private bool _hasVelocity;

    private bool _priorUseAccumulated;
    private bool _appliedAccumulated;
    private bool _useAccumulatedInput;

    protected override void OnRegister()
    {
        _priorUseAccumulated = Input.UseAccumulatedInput;
        Input.UseAccumulatedInput = UseAccumulatedInput;
        _appliedAccumulated = true;

        // Стрелку ставим сразу и безусловно. Свойство Kind меняет картинку только
        // при смене вида, а начальное значение и есть Arrow, поэтому без этой строки
        // игра начиналась бы с системного курсора и меняла его лишь после того,
        // как игрок наведёт указатель на цель и уведёт обратно
        Apply(_kind);
    }

    public override void _ExitTree()
    {
        // Восстанавливаем только если глобальное значение всё ещё наше: иначе чужая
        // настройка была бы затёрта при сносе сессии
        if (_appliedAccumulated && Input.UseAccumulatedInput == UseAccumulatedInput)
            Input.UseAccumulatedInput = _priorUseAccumulated;

        _appliedAccumulated = false;

        // Картинка курсора глобальна и сессию не переживает: выйдя в меню с включённым
        // режимом приказа, игрок остался бы с прицелом вместо стрелки
        Input.SetCustomMouseCursor(null);
        _kind = CursorKind.Arrow;

        base._ExitTree();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion)
            return;

        TrackMotion(motion.Position);
    }

    public override void Step(double dt)
    {
        EnsureScreenPos();

        var inverse = GetViewport().GetCanvasTransform().AffineInverse();
        ActualWorldPosition = inverse * _screenPos;
        VisualWorldPosition = Extrapolate
            ? inverse * ExtrapolatedScreenPos()
            : ActualWorldPosition;
    }

    /// <summary>Экранная точка вьюпорта → мировые координаты площадки.</summary>
    public Vector2 WorldFromScreen(Vector2 screenPos) =>
        GetViewport().GetCanvasTransform().AffineInverse() * screenPos;

    /// <summary>
    /// Видимая часть мира прямоугольником. По ней ограничивается всякое выделение,
    /// сделанное не мышью: жест мыши задаёт свои границы сам, а выбор по признаку
    /// не имеет их вовсе и без ограничения приводил бы в отряд юнитов с другого конца
    /// карты, о которых игрок в этот миг не думал.
    ///
    /// Хватает двух углов, поскольку камера не поворачивается: при повороте
    /// описанный прямоугольник пришлось бы считать по всем четырём.
    /// </summary>
    public Rect2 VisibleWorldRect
    {
        get
        {
            var inverse = GetViewport().GetCanvasTransform().AffineInverse();
            var screen = GetViewport().GetVisibleRect();
            var a = inverse * screen.Position;
            var b = inverse * screen.End;

            return new Rect2(a, b - a).Abs();
        }
    }

    /// <summary>
    /// Вид курсора. Единственный владелец — эта система: она уже отвечает за всё,
    /// что касается указателя, и заводить второе место, откуда вид меняется, незачем.
    ///
    /// Курсор показывает, что произойдёт по нажатию, — и в режиме выбранного приказа,
    /// и в обычном, где вид выводится из того, что лежит под указателем. Поэтому он
    /// и есть главный ответ на вопрос «что я сейчас прикажу»: панель приказов говорит
    /// то же самое, но словами и в стороне от места действия.
    /// </summary>
    public CursorKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
                return;

            _kind = value;
            Apply(value);
        }
    }

    private CursorKind _kind = CursorKind.Arrow;

    /// <summary>
    /// Наибольшая сторона стрелки в пикселях после подготовки.
    ///
    /// Стрелка мельче перекрестья намеренно: она сопровождает игрока постоянно и должна
    /// указывать, не заслоняя того, на что указывает. Перекрестье же появляется тогда,
    /// когда приказ вот-вот уйдёт, и обязано читаться с одного взгляда.
    ///
    /// Уменьшает картинки эта система, а не правка ассетов: исходные нарисованы крупно —
    /// 56 и 64 пикселя в стороне, — и та же картинка может понадобиться панели
    /// или подсказке в своём размере.
    /// </summary>
    private const int ArrowSide = 22;

    /// <summary>Наибольшая сторона курсора приказа в пикселях после подготовки.</summary>
    private const int CommandSide = 26;

    /// <summary>Подготовленные курсоры: картинка и её горячая точка. Готовятся один раз.</summary>
    private readonly System.Collections.Generic.Dictionary<CursorKind, (Texture2D Texture, Vector2 Hotspot)>
        _art = new();

    private void Apply(CursorKind kind)
    {
        var (texture, hotspot) = ArtOf(kind);

        if (texture == null)
        {
            Input.SetCustomMouseCursor(null);
            return;
        }

        Input.SetCustomMouseCursor(texture, Input.CursorShape.Arrow, hotspot);
    }

    private (Texture2D Texture, Vector2 Hotspot) ArtOf(CursorKind kind)
    {
        if (_art.TryGetValue(kind, out var cached))
            return cached;

        var prepared = Prepare(kind);
        _art[kind] = prepared;

        return prepared;
    }

    /// <summary>
    /// Подготовить картинку курсора: обрезать поля, уменьшить и найти горячую точку.
    ///
    /// ПОЛЯ ОБРЕЗАЮТСЯ, И ЭТО НЕ УКРАШЕНИЕ. Стрелка нарисована не по всему полю картинки,
    /// а в его правой нижней четверти, поэтому горячая точка, взятая от угла картинки,
    /// промахивалась мимо острия на половину стороны — курсор рисовался в стороне от места,
    /// куда игрок целился. После обрезки координаты рисунка и координаты картинки совпадают,
    /// и промаху взяться неоткуда.
    ///
    /// ГОРЯЧАЯ ТОЧКА У СТРЕЛКИ НА ОСТРИЕ, У ПРОЧИХ В СЕРЕДИНЕ. Остриём служит первый
    /// непрозрачный пиксель сверху: у стрелки он и есть та точка, которой указывают.
    /// Перекрестье же указывает центром, а не краем рисунка.
    /// </summary>
    private static (Texture2D Texture, Vector2 Hotspot) Prepare(CursorKind kind)
    {
        string path = CursorArt.PathOf(kind);
        var source = GD.Load<Texture2D>(path);
        var image = source?.GetImage();

        if (image == null)
        {
            GD.PushWarning($"[CursorSystem] нет картинки курсора: {path}");
            return (null, Vector2.Zero);
        }

        image = SpriteArt.TrimUsed(image);

        int side = Mathf.Max(image.GetWidth(), image.GetHeight());
        int target = kind == CursorKind.Arrow ? ArrowSide : CommandSide;

        if (side > target)
        {
            float k = target / (float)side;

            image.Resize(
                Mathf.Max(1, Mathf.RoundToInt(image.GetWidth() * k)),
                Mathf.Max(1, Mathf.RoundToInt(image.GetHeight() * k)),
                Image.Interpolation.Lanczos);
        }

        var hotspot = kind == CursorKind.Arrow
            ? Tip(image)
            : new Vector2(image.GetWidth() / 2, image.GetHeight() / 2);

        return (ImageTexture.CreateFromImage(image), hotspot);
    }

    /// <summary>
    /// Остриё: первый непрозрачный пиксель при обходе сверху вниз. Уменьшение размывает
    /// края, поэтому едва заметные точки за остриё не считаются.
    /// </summary>
    private static Vector2 Tip(Image image)
    {
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (image.GetPixel(x, y).A > 0.25f)
                    return new Vector2(x, y);

        return Vector2.Zero;
    }

    /// <summary>
    /// Точная мировая позиция кнопки или иного события мыши. Не использует прогноз:
    /// приказ и выделение обязаны совпасть с тем, куда ткнули.
    /// </summary>
    public Vector2 WorldFromEvent(InputEventMouse mouse) =>
        WorldFromScreen(mouse.Position);

    private void EnsureScreenPos()
    {
        if (_hasScreenPos)
            return;

        // До первого движения берём текущую точку вьюпорта — иначе призрак стартует в нуле
        _screenPos = GetViewport().GetMousePosition();
        _hasScreenPos = true;
    }

    private void TrackMotion(Vector2 screenPos)
    {
        ulong now = Time.GetTicksUsec();

        if (_hasMotionSample)
        {
            double sampleDt = (now - _prevMotionUsec) * 1e-6;

            // Слишком длинный промежуток значит, что выборка не про скорость, а про паузу
            // между жестами: такую дельту в оценку не берём
            if (sampleDt > 1e-6 && sampleDt <= VelocityStaleSec)
            {
                _screenVelocityPxPerSec = (screenPos - _prevScreenPos) / (float)sampleDt;
                _hasVelocity = true;
            }
            else
            {
                _screenVelocityPxPerSec = Vector2.Zero;
                _hasVelocity = false;
            }
        }

        _prevScreenPos = screenPos;
        _prevMotionUsec = now;
        _hasMotionSample = true;

        _screenPos = screenPos;
        _lastMotionUsec = now;
        _hasScreenPos = true;
    }

    /// <summary>
    /// Экранная точка с прогнозом. Скорость — только по меткам собственных событий;
    /// <see cref="Input.GetLastMouseVelocity"/> не используется: она усредняется около 0,1 с
    /// и запаздывает относительно свежего движения.
    /// </summary>
    private Vector2 ExtrapolatedScreenPos()
    {
        if (!_hasVelocity || !_hasMotionSample)
            return _screenPos;

        double age = (Time.GetTicksUsec() - _lastMotionUsec) * 1e-6;

        if (age > VelocityStaleSec)
            return _screenPos;

        float speed = _screenVelocityPxPerSec.Length();

        if (speed < MinVelocityPxPerSec)
            return _screenPos;

        float horizon = Mathf.Max(ExtrapolationHorizonSec, 0f);
        float maxOffset = Mathf.Max(ExtrapolationMaxOffsetPx, 0f);

        // Горизонт ограничивает упреждение по времени; возраст события его только съедает,
        // чтобы застрявший прогноз не продолжал уезжать после остановки потока событий
        float lead = Mathf.Max(horizon - (float)age, 0f);

        if (lead <= 0f || maxOffset <= 0f)
            return _screenPos;

        var delta = _screenVelocityPxPerSec * lead;
        float distance = delta.Length();

        if (distance > maxOffset)
            delta *= maxOffset / distance;

        return _screenPos + delta;
    }
}
