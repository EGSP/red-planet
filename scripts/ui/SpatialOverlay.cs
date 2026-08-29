using Godot;

/// <summary>
/// Раскладка мира по корзинам поверх карты: занятые корзины и численность в них.
///
/// ЗАЧЕМ ОТДЕЛЬНЫМ СЛОЕМ. Сетка перестала принадлежать системе движения и обслуживает
/// теперь и выбор цели, поэтому показывать её из слоя локального обхода означало бы,
/// что раскладка целей видна под чужим именем. Слоёв два, признаки разные, а сама
/// структура одна — см. <see cref="WorldSpace"/>.
///
/// ЧТО ЭТИМ ВИДНО. Во-первых, покрытие: корзины должны идти сплошным пятном вокруг скоплений,
/// а редкие одиночные корзины вдалеке означают, что кто-то забрёл за пределы поля.
/// Во-вторых, плотность: корзина с десятками сущностей означает, что размер корзины для этого
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
            Buckets(gm.Space.Targets);
        else
            Buckets(gm.Space.Mobiles);
    }

    private void Buckets<T>(SpatialGrid<T> grid) where T : class
    {
        float side = grid.BucketPx;
        var style = DrawTheme.Line(VizKind.SpatialBuckets);
        var font = ThemeDB.FallbackFont;

        foreach (var at in grid.FilledAt)
        {
            var bucket = grid.At(at);

            if (bucket == null || bucket.Items.Count == 0)
                continue;

            var corner = ToLocal(grid.Corner(at));

            if (DebugFlags.SpatialBuckets)
                ShapeDraw.Rect(this, new Rect2(corner, side, side), style);

            if (!DebugFlags.SpatialCounts || font == null)
                continue;

            // Число ставится в угол корзины, а не в середину: середину занимают сами
            // сущности, и подпись поверх них не читается
            DrawString(font, corner + new Vector2(4f, 14f), bucket.Items.Count.ToString(),
                HorizontalAlignment.Left, -1f, 12,
                new Color(DrawTheme.Hue(VizKind.SpatialBuckets), 0.7f));
        }
    }
}
