using Godot;

/// <summary>
/// Локальный слой движения поверх мира: силы, радиусы, учитываемые соседи и раскладка
/// по ячейкам.
///
/// Отрисовка здесь не роскошь. Коэффициенты обхода настраиваются подбором, и без стрелок,
/// длина которых пропорциональна величине силы, подбор ведётся вслепую: по одному лишь
/// поведению юнита нельзя отличить слабый обход от сильного стремления к цели.
/// </summary>
public partial class BoidsOverlay : Node2D
{
    /// <summary>Во что превращается единичная сила на экране, пикселей.</summary>
    private const float ForceScale = 48f;

    private bool _shown;

    public override void _Process(double delta)
    {
        bool shown = DebugFlags.AnyBoid;

        if (shown || _shown)
            QueueRedraw();

        _shown = shown;
    }

    public override void _Draw()
    {
        var gm = GameManager.I;

        if (gm == null || !DebugFlags.AnyBoid)
            return;

        var movement = gm.System<MovementSystem>();

        if (DebugFlags.BoidCells && movement != null)
            DrawCells(movement);

        foreach (var mobile in Watched(gm))
            DrawActor(mobile, movement);
    }

    /// <summary>
    /// За кем следим. Выделение — то же, что у путей: смотреть силы у полусотни сущностей
    /// разом бессмысленно, разбирают всегда одну-две.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<IMobile> Watched(GameManager gm)
    {
        if (gm.Command == null)
            yield break;

        foreach (var actor in gm.Command.Selected)
            if (actor is IMobile mobile)
                yield return mobile;
    }

    private void DrawActor(IMobile mobile, MovementSystem movement)
    {
        var at = ToLocal(mobile.GlobalPosition);
        var state = mobile.Movement;
        float radius = mobile.HitRadius;

        if (DebugFlags.BoidRadii)
        {
            ShapeDraw.Circle(this, at, radius, DrawTheme.Radius(VizKind.BoidBody), 20);
            ShapeDraw.Circle(this, at, radius * (movement?.SenseFactor ?? 3.5f),
                DrawTheme.Radius(VizKind.BoidSense), 40);
        }

        if (DebugFlags.BoidNeighbours)
            DrawNeighbours(mobile, at, radius * (movement?.SenseFactor ?? 3.5f));

        if (!DebugFlags.BoidForces)
            return;

        Arrow(at, state.SeekForce * state.SeekScale, VizKind.BoidSeek);
        Arrow(at, state.AvoidForce, VizKind.BoidAvoid);
        Arrow(at, state.AlignForce, VizKind.BoidAlign);

        // Скорость рисуется в тех же единицах, что и силы: иначе не видно,
        // насколько результат разошёлся со стремлением к цели
        if (mobile.Definition is { SpeedPx: > 0f })
            Arrow(at, state.Velocity / mobile.Definition.SpeedPx, VizKind.BoidSpeed);
    }

    private void DrawNeighbours(IMobile mobile, Vector2 at, float sense)
    {
        var gm = GameManager.I;
        var style = DrawTheme.Line(VizKind.BoidLink);

        foreach (var other in gm.Index.All<IMobile>())
        {
            if (ReferenceEquals(other, mobile))
                continue;

            if (mobile.GlobalPosition.DistanceTo(other.GlobalPosition) > sense)
                continue;

            ShapeDraw.Line(this, at, ToLocal(other.GlobalPosition), style);
        }
    }

    private void DrawCells(MovementSystem movement)
    {
        float side = movement.BucketSize;
        var style = DrawTheme.Line(VizKind.BoidCells);

        foreach (var pair in movement.Buckets)
        {
            if (pair.Value.Count == 0)
                continue;

            var corner = new Vector2(pair.Key.X * side, pair.Key.Y * side);
            ShapeDraw.Rect(this, new Rect2(ToLocal(corner), side, side), style);
        }
    }

    /// <summary>Стрелка силы: длина по величине, наконечник двумя штрихами.</summary>
    private void Arrow(Vector2 from, Vector2 force, VizKind kind)
    {
        if (force.LengthSquared() < 0.0004f)
            return;

        ShapeDraw.Arrow(this, from, from + force * ForceScale, DrawTheme.Line(kind));
    }
}
