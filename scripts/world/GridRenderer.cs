using Godot;

/// <summary>Рисует сетку застройки и границы мира.</summary>
public partial class GridRenderer : Node2D
{
    public override void _Draw()
    {
        int r = World.RadiusCells;
        float min = -r * Const.Unit;
        float max = (r + 1) * Const.Unit;

        var line = DrawTheme.Line(VizKind.GridLine);
        var axis = DrawTheme.Line(VizKind.GridAxis);

        for (int i = -r; i <= r + 1; i++)
        {
            float p = i * Const.Unit;
            var style = i == 0 ? axis : line;

            ShapeDraw.Line(this, new Vector2(p, min), new Vector2(p, max), style);
            ShapeDraw.Line(this, new Vector2(min, p), new Vector2(max, p), style);
        }

        ShapeDraw.Rect(this, new Rect2(min, min, max - min, max - min),
            DrawTheme.Line(VizKind.WorldBorder));
    }
}
