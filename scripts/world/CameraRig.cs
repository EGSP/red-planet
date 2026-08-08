using Godot;

/// <summary>
/// Камера: WASD или перетаскивание средней кнопкой, зум колесом.
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
    /// ZoomMin до ZoomMax (ноль — максимальное отдаление, единица — максимальное
    /// приближение), второй — её дополнение до единицы. Шкала логарифмическая, потому что
    /// зум колесом множится на ZoomStep, а не сдвигается на постоянный шаг. На сигнал
    /// подписаны слои вида: соединение задаётся в редакторе сцены.
    /// </summary>
    [Signal]
    public delegate void ZoomFactorChangedEventHandler(float factor, float inverted);

    /// <summary>Контур кадра при дальнем упоре зума.</summary>
    private static readonly Color FarFrame = new(0.35f, 0.75f, 1.00f, 0.85f);

    /// <summary>Контур кадра при ближнем упоре зума.</summary>
    private static readonly Color NearFrame = new(1.00f, 0.72f, 0.28f, 0.85f);

    private bool _dragging;

    /// <summary>
    /// Доля приближения на логарифмическом отрезке от ZoomMin до ZoomMax. Ноль — камера у
    /// дальнего упора, единица — у ближнего. При линейном пересчёте Zoom = 1 оказывался у
    /// первой трети диапазона, и ужатие прозрачности облаков срабатывало почти только при
    /// приближении; логарифм ставит тот же Zoom = 1 около трёх пятых — туда, где зум обычно
    /// и держат.
    /// </summary>
    public float ZoomFactor
    {
        get
        {
            float min = Mathf.Max(ZoomMin, 0.001f);
            float max = Mathf.Max(ZoomMax, min * 1.001f);
            float zoom = Mathf.Clamp(Zoom.X, min, max);

            return Mathf.Clamp(
                Mathf.Log(zoom / min) / Mathf.Log(max / min),
                0f, 1f);
        }
    }

    private float PanSpeed => Settings != null ? Settings.PanSpeed : 700f;
    private float ZoomStep => Settings != null ? Settings.ZoomStep : 1.12f;
    private float ZoomMin => Settings != null ? Settings.ZoomMin : 0.25f;
    private float ZoomMax => Settings != null ? Settings.ZoomMax : 2.5f;

    /// <summary>Дополнение <see cref="ZoomFactor"/> до единицы: единица у дальнего упора.</summary>
    public float ZoomFactorInverted => 1f - ZoomFactor;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        MakeCurrent();
        NotifyZoom();
    }

    public override void _Process(double dt)
    {
        if (Engine.IsEditorHint())
        {
            QueueRedraw();
            return;
        }

        var dir = Vector2.Zero;

        if (Input.IsKeyPressed(Key.W)) dir.Y -= 1f;
        if (Input.IsKeyPressed(Key.S)) dir.Y += 1f;
        if (Input.IsKeyPressed(Key.A)) dir.X -= 1f;
        if (Input.IsKeyPressed(Key.D)) dir.X += 1f;

        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)dt / Zoom.X;
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
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint())
            return;

        // Размер игрового окна: именно он задаёт кадр в партии, а не размер холста
        // редактора, который меняется при растягивании панели
        Vector2 view = GameViewportSize();

        DrawFrame(view, ZoomMin, FarFrame);
        DrawFrame(view, ZoomMax, NearFrame);
    }

    private void ApplyZoom(float factor)
    {
        float value = Mathf.Clamp(Zoom.X * factor, ZoomMin, ZoomMax);

        if (Mathf.IsEqualApprox(value, Zoom.X))
            return;

        Zoom = new Vector2(value, value);
        NotifyZoom();
    }

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
