using Godot;

/// <summary>Рисует сетку застройки и границы мира.</summary>
public partial class GridRenderer : Node2D
{
    public override void _Draw()
    {
        int r = World.RadiusCells;
        float min = -r * Const.Unit;
        float max = (r + 1) * Const.Unit;

        var line = ShapeStyle.Outline(new Color(1f, 1f, 1f, 0.06f), 1f, WidthMode.Screen);
        var axis = ShapeStyle.Outline(new Color(1f, 1f, 1f, 0.16f), 1f, WidthMode.Screen);

        for (int i = -r; i <= r + 1; i++)
        {
            float p = i * Const.Unit;
            var style = i == 0 ? axis : line;

            ShapeDraw.Line(this, new Vector2(p, min), new Vector2(p, max), style);
            ShapeDraw.Line(this, new Vector2(min, p), new Vector2(max, p), style);
        }

        ShapeDraw.Rect(this, new Rect2(min, min, max - min, max - min),
            ShapeStyle.Outline(new Color(1f, 0.6f, 0.4f, 0.25f), 2f, WidthMode.Screen));
    }
}
