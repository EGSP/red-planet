using Godot;

/// <summary>
/// Раскладка мира по клеткам поверх карты: занятые клетки и численность в них.
///
/// ЗАЧЕМ ОТДЕЛЬНЫМ СЛОЕМ. Сетка перестала принадлежать системе движения и обслуживает
/// теперь и выбор цели, поэтому показывать её из слоя локального обхода означало бы,
/// что раскладка целей видна под чужим именем. Слоёв два, признаки разные, а сама
/// структура одна — см. <see cref="WorldSpace"/>.
///
/// ЧТО ЭТИМ ВИДНО. Во-первых, покрытие: клетки должны идти сплошным пятном вокруг скоплений,
/// а редкие одиночные клетки вдалеке означают, что кто-то забрёл за пределы поля.
/// Во-вторых, плотность: клетка с десятками сущностей означает, что размер клетки для этого
/// боя мелок, и кольцевой обход платит за длинные списки.
/// </summary>
public partial class SpatialOverlay : Node2D
{
    private bool _shown;

    public override void _Process(double delta)
    {
        bool shown = DebugFlags.AnySpatial;

        if (shown || _shown)
            QueueRedraw();

        _shown = shown;
    }

    public override void _Draw()
    {
        var gm = GameManager.I;

        if (gm?.Space == null || !DebugFlags.AnySpatial)
            return;

        if (DebugFlags.SpatialTargets)
            Cells(gm.Space.Targets);
        else
            Cells(gm.Space.Mobiles);
    }

    private void Cells<T>(SpatialGrid<T> grid) where T : class
    {
        float side = grid.CellPx;
        var style = DrawTheme.Line(VizKind.SpatialCells);
        var font = ThemeDB.FallbackFont;

        foreach (var at in grid.FilledAt)
        {
            var cell = grid.At(at);

            if (cell == null || cell.Items.Count == 0)
                continue;

            var corner = ToLocal(grid.Corner(at));

            if (DebugFlags.SpatialCells)
                ShapeDraw.Rect(this, new Rect2(corner, side, side), style);

            if (!DebugFlags.SpatialCounts || font == null)
                continue;

            // Число ставится в угол клетки, а не в середину: середину занимают сами
            // сущности, и подпись поверх них не читается
            DrawString(font, corner + new Vector2(4f, 14f), cell.Items.Count.ToString(),
                HorizontalAlignment.Left, -1f, 12,
                new Color(DrawTheme.Hue(VizKind.SpatialCells), 0.7f));
        }
    }
}
