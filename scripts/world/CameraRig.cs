using Godot;

/// <summary>
/// Камера: сдвиг по границам экрана, перетаскивание средней кнопкой, зум колесом
/// и по ступеням с клавиш.
///
/// ПАНОРАМИРОВАНИЕ КЛАВИАТУРУ НЕ ЧИТАЕТ. Прежде камера двигалась по WASD, и это занимало
/// три клавиши из тех, на которые просятся приказы. Причём занимало намертво: направление
/// бралось опросом состояния клавиши в <see cref="_Process"/>, а не из события, поэтому
/// <c>SetInputAsHandled</c> камеру не остановил бы — какая бы система ни приняла событие,
/// камера всё равно сместилась бы. Сдвиг по границам экрана заодно избавляет от чтения
/// ввода вовсе: он выводится из положения курсора. Ступени зума этому не противоречат:
/// они разбираются событием в <see cref="_UnhandledInput"/> и потому останавливаются
/// тем, кто пометил событие обработанным.
///
/// В РЕДАКТОРЕ рисуются две рамки — видимая область при дальнем и ближнем упоре зума из
/// <see cref="CameraSettings"/>. По ним видно, что попадёт в кадр. В запущенной игре рамки
/// не рисуются.
/// </summary>
[Tool]
public partial class CameraRig : Camera2D
{
    /// <summary>Настройки хода и зума. Не назначены — берутся значения по умолчанию ресурса.</summary>
    [Export] public CameraSettings Settings;

    /// <summary>
    /// Приближение изменилось. Первый аргумент — доля на логарифмическом отрезке от
    /// ZoomFar до ZoomNear (ноль — полное отдаление, единица — полное приближение),
    /// второй — её дополнение до единицы. Шкала логарифмическая, потому что
    /// зум колесом множится на ZoomStep, а не сдвигается на постоянный шаг. На сигнал
    /// подписаны слои вида: соединение задаётся в редакторе сцены.
    /// </summary>
    [Signal]
    public delegate void ZoomFactorChangedEventHandler(float factor, float inverted);

    /// <summary>Контур кадра при дальнем упоре зума.</summary>
    private static readonly Color FarFrame = new(0.35f, 0.75f, 1.00f, 0.85f);

    /// <summary>Контур кадра при ближнем упоре зума.</summary>
    private static readonly Color NearFrame = new(1.00f, 0.72f, 0.28f, 0.85f);

    /// <summary>Ступени ближе этой доли отрезка считаются той, на которой камера стоит.</summary>
    private const float StepEpsilon = 0.001f;

    private bool _dragging;

    /// <summary>
    /// Зум, к которому камера идёт. Пока сглаживание не догнало его, он расходится
    /// с <see cref="Camera2D.Zoom"/>, и соседняя ступень отсчитывается именно от него:
    /// иначе второе нажатие посреди перехода возвращало бы туда, откуда камера ещё
    /// не успела уйти.
    /// </summary>
    private float _zoomTarget;

    /// <summary>
    /// Доля приближения на логарифмическом отрезке от ZoomFar до ZoomNear. Ноль — камера у
    /// дальнего упора, единица — у ближнего. При линейном пересчёте Zoom = 1 оказывался у
    /// первой трети диапазона, и ужатие прозрачности облаков срабатывало почти только при
    /// приближении; логарифм ставит тот же Zoom = 1 около трёх пятых — туда, где зум обычно
    /// и держат.
    /// </summary>
    public float ZoomFactor => FactorOf(Zoom.X);

    private float PanSpeed => Settings != null ? Settings.PanSpeed : 700f;
    private float EdgePanMargin => Settings != null ? Settings.EdgePanMarginPx : 24f;
    private float ZoomStep => Settings != null ? Settings.ZoomStep : 1.12f;
    private float ZoomLerpSpeed => Settings != null ? Settings.ZoomLerpSpeed : 16f;
    private float[] ZoomSteps => Settings?.ZoomSteps;
    private float ZoomStepTolerance => Settings != null ? Settings.ZoomStepTolerance : 0.2f;

    /// <summary>
    /// Зум при полном отдалении, огороженный от нуля: на нём стоит деление и логарифм.
    /// Настройка читается только отсюда, чтобы огораживание не пришлось повторять
    /// в каждом расчёте.
    /// </summary>
    private float ZoomFar => Mathf.Max(Settings != null ? Settings.ZoomFar : 0.25f, 0.001f);

    /// <summary>
    /// Зум при полном приближении, заведомо больший дальнего: при равных упорах отрезок
    /// вырождается и доля приближения теряет смысл.
    /// </summary>
    private float ZoomNear =>
        Mathf.Max(Settings != null ? Settings.ZoomNear : 2.5f, ZoomFar * 1.001f);

    /// <summary>Дополнение <see cref="ZoomFactor"/> до единицы: единица у дальнего упора.</summary>
    public float ZoomFactorInverted => 1f - ZoomFactor;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        MakeCurrent();

        // Начальный зум применяется сразу и без перехода: сглаживать нечего, игрок этого
        // значения не задавал
        _zoomTarget = StartZoom();
        Zoom = new Vector2(_zoomTarget, _zoomTarget);

        NotifyZoom();
    }

    public override void _Process(double dt)
    {
        if (Engine.IsEditorHint())
        {
            QueueRedraw();
            return;
        }

        FollowZoomTarget(dt);

        var dir = EdgeDirection();

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)dt / Zoom.X;
    }

    /// <summary>
    /// Зум, с которого начинается партия: ступень наименьшего отдаления, то есть наименьшее
    /// число в <see cref="CameraSettings.ZoomSteps"/>.
    ///
    /// ПОЧЕМУ НЕ ЗНАЧЕНИЕ ИЗ СЦЕНЫ. Партия начинается у коммандера, и разглядеть его
    /// в первые секунды важнее, чем охватить карту; ступень же взята потому, что всякое
    /// другое начальное значение оказалось бы между ступенями, и первое нажатие клавиши
    /// тратилось бы на приведение камеры к шкале.
    ///
    /// Перечень ступеней пуст — начальным остаётся значение, выставленное в сцене,
    /// приведённое к упорам.
    /// </summary>
    private float StartZoom()
    {
        var rungs = Rungs();

        if (rungs.Length == 0)
            return Mathf.Clamp(Zoom.X, ZoomFar, ZoomNear);

        // Наименьшему отдалению отвечает наибольшая доля приближения: отсчёты обратны
        // друг другу, и обращение уже сделано в Rungs
        float nearest = rungs[0];

        foreach (float rung in rungs)
            if (rung > nearest)
                nearest = rung;

        return ZoomOf(nearest);
    }

    /// <summary>
    /// Подвести зум к заданному значению за этот кадр.
    ///
    /// СБЛИЖЕНИЕ СЧИТАЕТСЯ ПО ЛОГАРИФМУ, как и всё прочее в зуме: при линейном сближении
    /// одна и та же доля расхождения означала бы у дальнего упора вдвое больший переход
    /// в кратности, чем у ближнего, и переход между дальними ступенями выглядел бы рывком.
    ///
    /// Доля сближения берётся через экспоненту, а не умножением быстроты на длительность
    /// кадра, потому что второе привязывает вид перехода к частоте кадров: при просадке
    /// множитель перевалил бы за единицу и дал бы перелёт.
    /// </summary>
    private void FollowZoomTarget(double dt)
    {
        float current = Mathf.Max(Zoom.X, 0.001f);

        if (Mathf.IsEqualApprox(current, _zoomTarget))
            return;

        float speed = ZoomLerpSpeed;
        float value;

        if (speed <= 0f)
        {
            value = _zoomTarget;
        }
        else
        {
            float share = 1f - Mathf.Exp(-speed * (float)dt);

            value = Mathf.Exp(Mathf.Lerp(Mathf.Log(current), Mathf.Log(_zoomTarget), share));

            // Остаток, который сглаживание сокращает бесконечно, отбрасывается: иначе
            // камера каждый кадр оповещала бы подписчиков о неразличимом изменении
            if (Mathf.Abs(value - _zoomTarget) <= _zoomTarget * 0.0005f)
                value = _zoomTarget;
        }

        Zoom = new Vector2(value, value);
        NotifyZoom();
    }

    /// <summary>
    /// Направление сдвига по границам окна. Курсор, попавший в полосу шириной
    /// <see cref="EdgePanMargin"/> от края, толкает камеру в сторону этого края; в углу
    /// складываются обе оси, и движение идёт по диагонали.
    ///
    /// СДВИГ МОЛЧИТ, ПОКА ОКНО НЕ В ФОКУСЕ ИЛИ КУРСОР ВНЕ ЕГО. Иначе камера ехала бы
    /// всё время, что игрок работает в другом окне: указатель, оставленный у края,
    /// от потери фокуса никуда не девается, а на втором мониторе он и вовсе лежит
    /// за границей вьюпорта.
    ///
    /// Полоса шире половины окна сама себя гасит: обе противоположные проверки срабатывают
    /// разом и дают ноль по этой оси. Отдельной проверки на такую настройку поэтому нет.
    /// </summary>
    private Vector2 EdgeDirection()
    {
        float margin = EdgePanMargin;

        if (margin <= 0f)
            return Vector2.Zero;

        var window = GetWindow();

        if (window == null || !window.HasFocus())
            return Vector2.Zero;

        var viewport = GetViewport();

        if (viewport == null)
            return Vector2.Zero;

        var rect = viewport.GetVisibleRect();
        var mouse = viewport.GetMousePosition();

        if (!rect.HasPoint(mouse))
            return Vector2.Zero;

        var dir = Vector2.Zero;

        if (mouse.X - rect.Position.X <= margin) dir.X -= 1f;
        if (rect.End.X - mouse.X <= margin) dir.X += 1f;
        if (mouse.Y - rect.Position.Y <= margin) dir.Y -= 1f;
        if (rect.End.Y - mouse.Y <= margin) dir.Y += 1f;

        return dir;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint())
            return;

        if (@event is InputEventMouseButton mouse)
        {
            switch (mouse.ButtonIndex)
            {
                case MouseButton.WheelUp when mouse.Pressed:
                    ApplyZoom(ZoomStep);
                    break;

                case MouseButton.WheelDown when mouse.Pressed:
                    ApplyZoom(1f / ZoomStep);
                    break;

                case MouseButton.Middle:
                    _dragging = mouse.Pressed;
                    break;
            }
        }

        if (@event is InputEventMouseMotion motion && _dragging)
            Position -= motion.Relative / Zoom;

        if (@event.IsActionPressed(InputActions.CameraZoomIn))
            StepZoom(1);
        else if (@event.IsActionPressed(InputActions.CameraZoomOut))
            StepZoom(-1);
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;

        // Размер игрового окна: именно он задаёт кадр в партии, а не размер холста
        // редактора, который меняется при растягивании панели
        Vector2 view = GameViewportSize();

        // Упоры берутся огороженные, те же, что и в расчётах зума: иначе рамка показывала бы
        // не тот кадр, к которому камера придёт при заведомо неверной настройке
        DrawFrame(view, ZoomFar, FarFrame);
        DrawFrame(view, ZoomNear, NearFrame);
    }

    /// <summary>
    /// Зум колесом: умножение нынешнего значения на шаг, без сглаживания и без привязки
    /// к ступеням.
    ///
    /// СГЛАЖИВАНИЕ ЗДЕСЬ НЕ ПРИМЕНЯЕТСЯ НАМЕРЕННО. Колесо задаёт произвольное значение,
    /// и щелчок его отвечает малому изменению, которое игрок ведёт непрерывно, следя
    /// за результатом; переход, растянутый во времени, означал бы, что картинка отстаёт
    /// от руки. Ступени с клавиш, напротив, переводят камеру на заметное расстояние
    /// одним нажатием, и там сглаживание нужно.
    ///
    /// Переход, начатый клавишей, прокрут колеса прерывает: заданное значение становится
    /// равно нынешнему, и сближаться больше не с чем.
    /// </summary>
    private void ApplyZoom(float factor)
    {
        float value = Mathf.Clamp(Zoom.X * factor, ZoomFar, ZoomNear);

        if (Mathf.IsEqualApprox(value, Zoom.X))
        {
            _zoomTarget = value;
            return;
        }

        _zoomTarget = value;
        Zoom = new Vector2(value, value);
        NotifyZoom();
    }

    /// <summary>
    /// Перейти на соседнюю ступень: <paramref name="direction"/> больше нуля — на ближнюю
    /// к упору приближения, меньше нуля — к упору отдаления.
    ///
    /// Ступени перебираются, а не берутся по порядку в массиве, поскольку порядок задаёт
    /// игрок в ресурсе настроек и полагаться на него нельзя. Отсчёт ведётся от заданного
    /// зума, поэтому нажатия, сделанные подряд, складываются в переход через несколько
    /// ступеней.
    ///
    /// ЗУМ, ПОДКРУЧЕННЫЙ КОЛЕСОМ, СЧИТАЕТСЯ СТОЯЩИМ НА БЛИЗКОЙ СТУПЕНИ. Иначе нажатие,
    /// сделанное рядом со ступенью, тратилось бы на то, чтобы к ней притянуться, и отдалить
    /// камеру заметно удавалось бы только со второго раза. Насколько близко — задаёт
    /// <see cref="CameraSettings.ZoomStepTolerance"/>; см. <see cref="Snapped"/>.
    /// </summary>
    private void StepZoom(int direction)
    {
        var rungs = Rungs();

        if (rungs.Length == 0)
            return;

        float current = FactorOf(_zoomTarget);
        float from = Snapped(rungs, current, direction);
        float target = NextStep(rungs, from, direction);

        // Ступень, к которой примкнул нынешний зум, оказалась крайней в эту сторону.
        // Нажатие всё же должно что-то делать, поэтому переход идёт на неё саму,
        // и камера встаёт ровно на шкалу
        if (float.IsNaN(target) && !Mathf.IsEqualApprox(from, current))
            target = from;

        // Камера уже на крайней ступени: идти в эту сторону некуда
        if (float.IsNaN(target))
            return;

        SetZoomTarget(ZoomOf(target));
    }

    /// <summary>
    /// Шкала ступеней в долях приближения: в точности то, что задано в настройках,
    /// приведённое к отрезку и обращённое.
    ///
    /// ОБРАЩЕНИЕ ЗДЕСЬ И ЕСТЬ ГРАНИЦА ДВУХ ОТСЧЁТОВ. В настройке доля считается
    /// от отдаления (единица — камера отведена дальше всего), в расчётах — от приближения
    /// (единица — наибольший зум), поскольку так же считает <see cref="ZoomFactor"/>,
    /// от которого зависят слои вида. Переводится одно в другое единственной вычитающей
    /// строкой ниже, и второго такого места в проекте нет.
    ///
    /// УПОРЫ САМИ СОБОЙ В ШКАЛУ НЕ ВХОДЯТ. Если ноля и единицы в перечне нет, клавиши
    /// до дальнего и ближнего упора не доводят, и достаются они только колесу. Это
    /// решение, а не упущение: число записей в настройке равно числу положений, между
    /// которыми ходят клавиши, и упор становится ступенью тогда, когда он записан.
    ///
    /// Наружу метод открыт ради <see cref="ZoomScale"/>: шкала в интерфейсе показывает
    /// те же ступени, по которым камера ходит, и собирать их вторым способом означало бы
    /// допустить расхождение между показанным и происходящим.
    /// </summary>
    public float[] Rungs()
    {
        var steps = ZoomSteps;
        int count = steps?.Length ?? 0;
        var rungs = new float[count];

        for (int i = 0; i < count; i++)
            rungs[i] = 1f - Mathf.Clamp(steps[i], 0f, 1f);

        return rungs;
    }

    /// <summary>
    /// Ближайшая ступень строго за <paramref name="factor"/> в заданную сторону
    /// либо <see cref="float.NaN"/>, если таковой нет.
    /// </summary>
    private static float NextStep(float[] rungs, float factor, int direction)
    {
        float best = float.NaN;

        foreach (float candidate in rungs)
        {
            bool ahead = direction > 0
                ? candidate > factor + StepEpsilon
                : candidate < factor - StepEpsilon;

            if (!ahead)
                continue;

            bool closer = float.IsNaN(best)
                || (direction > 0 ? candidate < best : candidate > best);

            if (closer)
                best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Ступень, к которой примыкает <paramref name="factor"/>, либо он сам, если ближайшая
    /// ступень дальше допуска.
    ///
    /// ДОПУСК ОТМЕРЯЕТСЯ ДОЛЕЙ ПРОМЕЖУТКА, А НЕ ДОЛЕЙ ВСЕГО ОТРЕЗКА, поскольку ступени
    /// расставляются неравномерно: пятая часть промежутка означает одно и то же
    /// в частой и в редкой части шкалы, тогда как пятая часть отрезка поглотила бы
    /// частые ступени целиком.
    ///
    /// Промежуток берётся до соседней ступени в сторону движения: примыкание решает,
    /// откуда отсчитывать следующий переход, и мерить его следует тем расстоянием,
    /// которое предстоит пройти. У ступени, стоящей на упоре, такой соседней нет, и тогда
    /// берётся промежуток с обратной стороны.
    /// </summary>
    private float Snapped(float[] rungs, float factor, int direction)
    {
        float tolerance = ZoomStepTolerance;

        if (tolerance <= 0f)
            return factor;

        float nearest = NearestStep(rungs, factor);

        if (float.IsNaN(nearest))
            return factor;

        float neighbour = NextStep(rungs, nearest, direction);

        if (float.IsNaN(neighbour))
            neighbour = NextStep(rungs, nearest, -direction);

        float gap = float.IsNaN(neighbour) ? 1f : Mathf.Abs(neighbour - nearest);

        return Mathf.Abs(factor - nearest) <= gap * tolerance ? nearest : factor;
    }

    /// <summary>Ступень, ближайшая к доле, либо <see cref="float.NaN"/> при пустом перечне.</summary>
    private static float NearestStep(float[] rungs, float factor)
    {
        float best = float.NaN;
        float distance = float.MaxValue;

        foreach (float candidate in rungs)
        {
            float current = Mathf.Abs(candidate - factor);

            if (current >= distance)
                continue;

            distance = current;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Задать ступень, к которой камера идёт. Сглаживание выключено (быстрота равна нулю) —
    /// значение применяется тем же кадром, чтобы мгновенный переход не ждал <c>_Process</c>.
    /// </summary>
    private void SetZoomTarget(float zoom)
    {
        _zoomTarget = Mathf.Clamp(zoom, ZoomFar, ZoomNear);

        if (ZoomLerpSpeed <= 0f && !Mathf.IsEqualApprox(_zoomTarget, Zoom.X))
        {
            Zoom = new Vector2(_zoomTarget, _zoomTarget);
            NotifyZoom();
        }
    }

    /// <summary>Доля зума на логарифмическом отрезке между упорами.</summary>
    private float FactorOf(float zoom)
    {
        float far = ZoomFar;
        float near = ZoomNear;
        float value = Mathf.Clamp(zoom, far, near);

        return Mathf.Clamp(Mathf.Log(value / far) / Mathf.Log(near / far), 0f, 1f);
    }

    /// <summary>Зум по доле отрезка: обращение <see cref="FactorOf"/>.</summary>
    private float ZoomOf(float factor) =>
        ZoomFar * Mathf.Pow(ZoomNear / ZoomFar, Mathf.Clamp(factor, 0f, 1f));

    /// <summary>Оповестить подписчиков зума и перерисовать экранные обводки.</summary>
    private void NotifyZoom()
    {
        float factor = ZoomFactor;

        EmitSignal(SignalName.ZoomFactorChanged, factor, 1f - factor);
        ShapeDraw.NotifyZoomChanged(GetTree());
    }

    /// <summary>
    /// Рамка видимой области при заданном зуме, в локальных координатах камеры.
    /// Центр совпадает с камерой (режим DragCenter).
    /// </summary>
    private void DrawFrame(Vector2 view, float zoom, Color color)
    {
        float z = Mathf.Max(zoom, 0.001f);
        Vector2 size = view / z;
        var rect = new Rect2(-size * 0.5f, size);

        ShapeDraw.Rect(this, rect, ShapeStyle.Outline(color, 2f, WidthMode.Screen));
    }

    /// <summary>Размер игрового viewport из настроек проекта.</summary>
    private static Vector2 GameViewportSize()
    {
        int width = ProjectSettings.GetSetting("display/window/size/viewport_width").AsInt32();
        int height = ProjectSettings.GetSetting("display/window/size/viewport_height").AsInt32();

        if (width <= 0)
            width = 1152;

        if (height <= 0)
            height = 648;

        return new Vector2(width, height);
    }
}
