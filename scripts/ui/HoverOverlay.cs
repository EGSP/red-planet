using Godot;

/// <summary>
/// Обводка сущности под курсором. Отдельная нода в слое служебной графики — по тем же
/// причинам, по которым отдельной нодой рисуются приказы (<see cref="OrderOverlay"/>):
/// обводка принадлежит не миру, а тому, кто на мир смотрит, и сама сущность о ней не знает.
///
/// Кольцо выделения при этом не подменяется и не гасится: выделение и наведение — разные
/// сведения, и различаются они цветом. Обводка проходит снаружи кольца, чтобы у выделенной
/// сущности были видны обе метки сразу.
/// </summary>
public partial class HoverOverlay : Node2D
{
    /// <summary>Отступ обводки от границы сущности, в пикселях.</summary>
    private const float Gap = 5f;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var hovered = GameManager.I?.System<HoverSystem>()?.Hovered;

        if (hovered is not Node2D node || !Alive.Is(node))
            return;

        var style = DrawTheme.Outline(VizKind.Hover, 2f, WidthMode.Screen, 0.9f);

        // Занимающая место сущность обводится по своему прямоугольнику: окружность вокруг
        // постройки два на четыре отстояла бы от её боков на целую клетку
        if (hovered is IObstacle { Footprint.IsEmpty: false } obstacle)
        {
            ShapeDraw.Obb(this, obstacle.Footprint.Grow(Gap), style);
            return;
        }

        ShapeDraw.Circle(this, ToLocal(hovered.GlobalPosition), hovered.HitRadius + Gap, style, 28);
    }
}
