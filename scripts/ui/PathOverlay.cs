using Godot;

/// <summary>
/// Пути поверх мира: ломаная от сущности к цели, пройденная часть отдельно от предстоящей,
/// и узлы, раскрытые последним поиском.
///
/// Кого показывать, решает выделение — то же самое, по которому рисуются очереди приказов.
/// Отдельный признак «все» существует затем, чтобы поймать случай, где странно ведёт себя
/// не выделенный, а посторонний юнит: выделять его в этот момент значит спугнуть ситуацию.
/// </summary>
public partial class PathOverlay : Node2D
{
    private static readonly Color Ahead = new(0.45f, 0.95f, 1f, 0.85f);
    private static readonly Color Behind = new(0.4f, 0.5f, 0.6f, 0.5f);
    private static readonly Color Failed = new(1f, 0.45f, 0.35f, 0.9f);
    private static readonly Color Visited = new(0.6f, 0.7f, 1f, 0.22f);

    private bool _shown;

    public override void _Process(double delta)
    {
        bool shown = DebugFlags.AnyPath;

        if (shown || _shown)
            QueueRedraw();

        _shown = shown;
    }

    public override void _Draw()
    {
        var gm = GameManager.I;

        if (gm == null || !DebugFlags.AnyPath)
            return;

        var pathfinding = gm.System<PathfindingSystem>();

        if (pathfinding == null)
            return;

        DrawExpanded(pathfinding);

        if (DebugFlags.PathsAll)
        {
            foreach (var mobile in gm.Index.All<IMobile>())
                DrawPath(pathfinding, mobile);

            return;
        }

        if (!DebugFlags.Paths || gm.Command == null)
            return;

        foreach (var actor in gm.Command.Selected)
            if (actor is IMobile mobile)
                DrawPath(pathfinding, mobile);
    }

    /// <summary>Что перебрал последний поиск. Показывает, куда A* потратил бюджет узлов.</summary>
    private void DrawExpanded(PathfindingSystem pathfinding)
    {
        if (!DebugFlags.PathsExpanded || pathfinding.Expanded == null)
            return;

        float half = NavGrid.Cell * 0.5f;
        var style = ShapeStyle.Solid(Visited);

        foreach (var point in pathfinding.Expanded)
            ShapeDraw.Rect(this, new Rect2(ToLocal(point) - new Vector2(half, half),
                NavGrid.Cell, NavGrid.Cell), style);
    }

    private void DrawPath(PathfindingSystem pathfinding, IMobile mobile)
    {
        var handle = pathfinding.Peek(mobile);

        if (handle == null || handle.Points.Count == 0)
            return;

        var color = handle.Status == PathStatus.Unreachable || mobile.Movement.Blocked
            ? Failed
            : Ahead;

        var behind = ShapeStyle.Outline(Behind, 1.5f, WidthMode.MinScreen);
        var ahead = ShapeStyle.Outline(color, 2f, WidthMode.Screen);
        var waypoint = ShapeStyle.Filled(new Color(color, 0.55f), color, 1.5f, WidthMode.MinScreen);
        var goal = ShapeStyle.Filled(new Color(color, 0.35f), new Color(color, 0.7f), 2f, WidthMode.Screen);

        var from = mobile.GlobalPosition;

        for (int i = 0; i < handle.Points.Count; i++)
        {
            var previous = i == 0 ? from : handle.Points[i - 1];
            var to = handle.Points[i];

            // Пройденная часть остаётся видимой, но приглушённой: по ней видно,
            // откуда сущность пришла и не крутится ли она на месте
            if (i < handle.Cursor)
            {
                ShapeDraw.Line(this, ToLocal(previous), ToLocal(to), behind);
                continue;
            }

            var start = i == handle.Cursor ? from : previous;

            ShapeDraw.Line(this, ToLocal(start), ToLocal(to), ahead);
            ShapeDraw.Circle(this, ToLocal(to), 3.5f, waypoint);
        }

        ShapeDraw.Circle(this, ToLocal(handle.Goal), 5f, goal);
    }
}
