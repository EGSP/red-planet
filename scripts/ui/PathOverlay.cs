using Godot;

/// <summary>
/// Пути поверх мира: ломаная от сущности к цели, пройденная часть отдельно от предстоящей,
/// узлы, раскрытые последним поиском, и полоса макро-поиска, которой поиск был ограничен.
///
/// Кого показывать, решает выделение — то же самое, по которому рисуются очереди приказов.
/// Отдельный признак «все» существует затем, чтобы поймать случай, где странно ведёт себя
/// не выделенный, а посторонний юнит: выделять его в этот момент значит спугнуть ситуацию.
/// </summary>
public partial class PathOverlay : Node2D
{
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

        // Полоса макро-поиска хранится у пути, поэтому показывается вместе с выделением
        // даже тогда, когда сами ломаные показывать не просили
        if ((!DebugFlags.Paths && !DebugFlags.PathMacro) || gm.Command == null)
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

        float half = NavGrid.CellPx * 0.5f;
        var style = DrawTheme.Fill(VizKind.PathVisited, 0.22f);

        foreach (var point in pathfinding.Expanded)
            ShapeDraw.Rect(this, new Rect2(ToLocal(point) - new Vector2(half, half),
                NavGrid.CellPx, NavGrid.CellPx), style);
    }

    /// <summary>
    /// Верхний уровень пути: области полосы макро-поиска кружками по их серединам
    /// и цепочка от старта к цели ломаной по тем же серединам.
    ///
    /// Показано то, чем поиск ограничивал себя, когда считал ИМЕННО ЭТОТ путь: за пределы
    /// полосы он ячейки не раскрывал, а по цепочке считал оценку расстояния. Полоса взята
    /// у самого пути, поэтому показывается она для каждого выводимого юнита, а не для
    /// последнего посчитанного запроса.
    ///
    /// Пустая полоса означает, что поиск шёл по всему растру: цель была видна напрямую,
    /// слоя областей ещё нет либо полоса пути не дала и поиск повторился без ограничения.
    /// </summary>
    private void DrawBand(PathHandle handle)
    {
        if (!DebugFlags.PathMacro || handle.Band.Count == 0)
            return;

        var area = DrawTheme.Filled(VizKind.PathBand, 0.10f, 0.45f, 1.5f, WidthMode.MinScreen);
        float radius = NavGrid.TilePx * 0.22f;

        foreach (var center in handle.Band)
            ShapeDraw.Circle(this, ToLocal(center), radius, area);

        if (handle.Chain.Count == 0)
            return;

        var link = DrawTheme.Line(VizKind.PathMacro);
        var node = DrawTheme.Filled(VizKind.PathMacro, 0.55f, 1f, 2f, WidthMode.Screen);

        for (int i = 0; i < handle.Chain.Count; i++)
        {
            var at = ToLocal(handle.Chain[i]);

            if (i > 0)
                ShapeDraw.Line(this, ToLocal(handle.Chain[i - 1]), at, link);

            ShapeDraw.Circle(this, at, 5f, node);
        }
    }

    private void DrawPath(PathfindingSystem pathfinding, IMobile mobile)
    {
        var handle = pathfinding.Peek(mobile);

        if (handle == null || handle.Points.Count == 0)
            return;

        // Полоса рисуется первой: ломаная пути должна лежать поверх неё, а не под
        DrawBand(handle);

        if (!DebugFlags.Paths && !DebugFlags.PathsAll)
            return;

        var kind = handle.Status == PathStatus.Unreachable || mobile.Movement.Blocked
            ? VizKind.PathFailed
            : VizKind.PathAhead;

        var behind = DrawTheme.Line(VizKind.PathBehind);
        var ahead = DrawTheme.Line(kind);
        var waypoint = DrawTheme.Filled(kind, 0.55f, 1f, 1.5f, WidthMode.MinScreen);
        var goal = DrawTheme.Filled(kind, 0.35f, 0.7f, 2f, WidthMode.Screen);

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
