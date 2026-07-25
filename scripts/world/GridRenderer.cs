using Godot;

/// <summary>Рисует сетку застройки и границы мира.</summary>
public partial class GridRenderer : Node2D
{
    public override void _Draw()
    {
        int r = Const.WorldRadiusCells;
        float min = -r * Const.Unit;
        float max = (r + 1) * Const.Unit;

        var line = new Color(1f, 1f, 1f, 0.06f);
        var axis = new Color(1f, 1f, 1f, 0.16f);

        for (int i = -r; i <= r + 1; i++)
        {
            float p = i * Const.Unit;
            var color = i == 0 ? axis : line;

            DrawLine(new Vector2(p, min), new Vector2(p, max), color);
            DrawLine(new Vector2(min, p), new Vector2(max, p), color);
        }

        DrawRect(new Rect2(min, min, max - min, max - min),
            new Color(1f, 0.6f, 0.4f, 0.25f), false, 2f);
    }
}
