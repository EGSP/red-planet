using Godot;

/// <summary>Призрак постройки под курсором: зелёный — можно ставить, красный — нельзя.</summary>
public partial class PlacementGhost : Node2D
{
    public BuildableDef Def;
    public Vector2I Origin;
    public bool Valid;

    public override void _Draw()
    {
        if (Def == null)
            return;

        var color = Valid ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);

        foreach (var cell in Def.Cells(Origin))
        {
            var rect = new Rect2(Const.CellCorner(cell) - GlobalPosition,
                new Vector2(Const.Unit, Const.Unit));

            DrawRect(rect, new Color(color, 0.25f));
            DrawRect(rect, color, false, 2f);
        }
    }
}
