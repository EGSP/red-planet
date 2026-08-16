using Godot;

/// <summary>
/// Шкала зума в правом нижнем углу: где камера стоит на отрезке между дальним и ближним
/// упором. Верх шкалы отвечает полному отдалению, низ — полному приближению, поскольку
/// зум читается как высота камеры над картой, и обратное расположение заставляло бы
/// переводить одно в другое при каждом взгляде.
///
/// ОТМЕТКА ДВИЖЕТСЯ ПО ТОЙ ЖЕ ДОЛЕ, ЧТО И СТУПЕНИ. Доля берётся у камеры готовой
/// (<see cref="CameraRig.ZoomFactor"/>), а не пересчитывается здесь из <c>Zoom</c>:
/// отрезок логарифмический, и второй пересчёт означал бы второй источник одного правила,
/// который разошёлся бы с первым при правке упоров.
///
/// ПОПЕРЕЧНЫЕ ЧЁРТОЧКИ — ЗАДАННЫЕ СТУПЕНИ. Без них шкала показывала бы только положение,
/// и предсказать, куда камера встанет по нажатию, было бы нельзя. Ступени берутся у камеры
/// той же шкалой, по которой она ходит, поэтому расхождение между показанным и
/// происходящим невозможно.
/// </summary>
public partial class ZoomScale : Control
{
    private static readonly Color Background = new(0.09f, 0.10f, 0.12f, 0.9f);
    private static readonly Color Tick = new(0.6f, 0.65f, 0.72f, 0.42f);

    /// <summary>Цвет отметки тот же, что у выбранной боевой группы и у курсора полосы событий.</summary>
    private static readonly Color Marker = new(0.55f, 0.95f, 1f);

    private static readonly Vector2 Extent = new(10, 72);

    /// <summary>Поле от края шкалы, чтобы крайние отметки не срезались, пикселей.</summary>
    private const float Inset = 1f;

    private CameraRig _camera;

    /// <summary>Доля, при которой шкала нарисована в последний раз. Вне отрезка — не рисована.</summary>
    private float _shown = -1f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = Extent;
        SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
    }

    /// <summary>
    /// Перерисовка только при изменении доли, а не каждый кадр: зум меняется редко,
    /// и рисовать неподвижную шкалу заново незачем.
    /// </summary>
    public override void _Process(double delta)
    {
        var camera = Camera();

        if (camera == null)
            return;

        float factor = camera.ZoomFactor;

        if (Mathf.IsEqualApprox(factor, _shown))
            return;

        _shown = factor;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var camera = Camera();

        if (camera == null)
            return;

        var size = Size;

        if (size.X <= 1f || size.Y <= 1f)
            return;

        DrawRect(new Rect2(Vector2.Zero, size), Background);

        foreach (float rung in camera.Rungs())
            DrawRect(new Rect2(2f, RowOf(rung, size), size.X - 4f, 1f), Tick);

        DrawRect(new Rect2(0f, RowOf(camera.ZoomFactor, size) - 1f, size.X, 2f), Marker);
    }

    /// <summary>
    /// Камера партии. Берётся текущая камера вьюпорта, а не узел по пути в сцене: путь
    /// пришлось бы согласовывать с <c>Session.tscn</c> при любой её перестройке, тогда как
    /// текущей камера становится сама (<c>CameraRig.MakeCurrent</c>).
    /// </summary>
    private CameraRig Camera()
    {
        if (Alive.Is(_camera))
            return _camera;

        _camera = GetViewport()?.GetCamera2D() as CameraRig;

        return _camera;
    }

    /// <summary>Высота отметки по доле приближения: ноль наверху, единица внизу.</summary>
    private static float RowOf(float factor, Vector2 size) =>
        Inset + Mathf.Clamp(factor, 0f, 1f) * (size.Y - Inset * 2f);
}
