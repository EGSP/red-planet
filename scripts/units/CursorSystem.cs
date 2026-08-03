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
    }

    public override void _ExitTree()
    {
        // Восстанавливаем только если глобальное значение всё ещё наше: иначе чужая
        // настройка была бы затёрта при сносе сессии
        if (_appliedAccumulated && Input.UseAccumulatedInput == UseAccumulatedInput)
            Input.UseAccumulatedInput = _priorUseAccumulated;

        _appliedAccumulated = false;
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
