using Godot;

/// <summary>
/// Обводка сущности под курсором. Отдельная нода в слое служебной графики — по тем же
/// причинам, по которым отдельной нодой рисуются приказы (<see cref="OrderOverlay"/>):
/// обводка принадлежит не миру, а тому, кто на мир смотрит, и сама сущность о ней не знает.
///
/// Кольцо выделения при этом не подменяется и не гасится: выделение и наведение — разные
/// сведения, и различаются они цветом. Обводка проходит снаружи кольца, чтобы у выделенной
/// сущности были видны обе метки сразу.
///
/// СВОИХ НАСТРОЕК У НОДЫ НЕТ. Цвет, толщина и отступ живут в <see cref="HoverSystem"/>,
/// рядом с задержкой смены цели: настройки наведения принадлежат одному месту, а нода
/// заводится кодом и в сцене не значится, поэтому в инспекторе её всё равно не найти.
/// </summary>
public partial class HoverOverlay : Node2D
{
    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var hover = GameManager.I?.System<HoverSystem>();
        var hovered = hover?.Hovered;

        if (hovered is not Node2D node || !Alive.Is(node))
            return;

        var style = ShapeStyle.Outline(hover.OutlineColor, hover.OutlineWidth, WidthMode.Screen);
        float gap = hover.OutlineGap;

        // Занимающая место сущность обводится по своему прямоугольнику: окружность вокруг
        // постройки два на четыре отстояла бы от её боков на целую клетку
        if (hovered is IObstacle { Footprint.IsEmpty: false } obstacle)
        {
            ShapeDraw.Obb(this, obstacle.Footprint.Grow(gap), style);
            return;
        }

        ShapeDraw.Circle(this, ToLocal(hovered.GlobalPosition), hovered.HitRadius + gap, style, 28);
    }
}
