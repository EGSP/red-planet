using Godot;

/// <summary>
/// Полноэкранный каркас дерева интерфейса.
///
/// Зачем нужен: Control, лежащий непосредственно под CanvasLayer, размера от него
/// не получает — слой не является опорным прямоугольником, поэтому якоря считаются
/// от нулевого размера и вся разметка схлопывается в левый верхний угол. Проверено
/// на первом заходе: якоря стояли верно, а размер выходил (0, 0).
///
/// Поэтому размер выставляется явно и обновляется при изменении окна. Все дальнейшие
/// элементы кладутся уже внутрь каркаса, где обычные якоря работают как положено.
/// </summary>
public partial class UiFrame : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        Resize();
        GetViewport().SizeChanged += Resize;
    }

    public override void _ExitTree()
    {
        var viewport = GetViewport();

        if (Alive.Is(viewport))
            viewport.SizeChanged -= Resize;
    }

    private void Resize()
    {
        Position = Vector2.Zero;
        Size = GetViewport().GetVisibleRect().Size;
    }
}
