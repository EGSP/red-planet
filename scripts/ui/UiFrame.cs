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
///
/// ЗДЕСЬ ЖЕ ПРИМЕНЯЕТСЯ МАСШТАБ ИНТЕРФЕЙСА (<see cref="UiScale"/>). Каркас — единственное
/// место, общее для всех деревьев интерфейса и притом знающее свой слой, поэтому множитель
/// ставится на слой отсюда, а не в каждой панели по отдельности. Мировые оверлеи лежат вне
/// слоёв интерфейса и масштабом не задеваются, что и требуется: размером мира ведает камера.
///
/// Размер каркаса при этом делится на множитель: слой увеличивает всё нарисованное в нём,
/// и каркас, оставленный в пикселях окна, вышел бы за экран ровно во столько же раз.
/// </summary>
public partial class UiFrame : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        Apply();

        GetViewport().SizeChanged += Apply;
        UiScale.Changed += Apply;
    }

    public override void _ExitTree()
    {
        UiScale.Changed -= Apply;

        var viewport = GetViewport();

        if (Alive.Is(viewport))
            viewport.SizeChanged -= Apply;
    }

    private void Apply()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        float scale = UiScale.Effective(viewport);

        // Растеризация шрифтов согласуется с масштабом слоя, иначе текст расплывается;
        // свойство принадлежит вьюпорту, поэтому значение у всех каркасов одно и то же
        UiScale.ApplyOversampling(GetViewport(), scale);

        // Каркас встречается и вне слоя — в предварительных сценах инструментов;
        // там масштабировать нечего, и размер берётся как есть
        if (GetParent() is CanvasLayer layer)
            layer.Scale = new Vector2(scale, scale);
        else
            scale = 1f;

        Position = Vector2.Zero;
        Size = viewport / scale;
    }
}
