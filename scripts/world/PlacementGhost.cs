using Godot;

/// <summary>
/// Призрак постройки под курсором: зелёный — можно ставить, красный — нельзя.
///
/// Рисует два контура. Сплошной — само место, которое займёт постройка. Пунктирный —
/// обязательный зазор вокруг него: игрок должен видеть не только габарит, но и то,
/// почему постановка вплотную к соседу отклоняется.
/// </summary>
public partial class PlacementGhost : Node2D
{
    public UnitDefinition Definition;

    /// <summary>Центр будущего места. Уже с притяжением к точке метала, если оно было.</summary>
    public Vector2 Center;

    public bool Valid;

    public override void _Draw()
    {
        if (Definition == null)
            return;

        var color = Valid ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);

        var area = Placement.Footprint(Definition, Center);
        var local = new Rect2(area.Position - GlobalPosition, area.Size);

        DrawRect(local, new Color(color, 0.22f));
        DrawRect(local, color, false, 2f);

        DrawRect(local.Grow(Const.BuildMarginPx), new Color(color, 0.35f), false, 1f);
    }
}
