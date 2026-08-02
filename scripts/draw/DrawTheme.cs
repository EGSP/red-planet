using Godot;

/// <summary>
/// Смысл визуализации: один ключ — один базовый оттенок и набор фабрик стиля.
/// Не зависит от доменных типов приказов и от <c>GameManager</c>.
/// </summary>
public enum VizKind
{
    Vision,
    Attack,
    Work,
    Selection,
    Band,

    OrderMove,
    OrderBuild,
    OrderAttack,
    OrderRepair,
    OrderFollow,

    WorkBeamBuild,
    WorkBeamRepair,

    PlacementValid,
    PlacementInvalid,
    Footprint,
    FootprintMargin,

    PathAhead,
    PathBehind,
    PathFailed,
    PathVisited,

    Metal,

    BoidSeek,
    BoidAvoid,
    BoidAlign,
    BoidSpeed,
    BoidSense,
    BoidBody,
    BoidLink,
    BoidCells,

    NavBlocked,
    NavTight,
    NavOpen,

    GridLine,
    GridAxis,
    WorldBorder,

    PreviewClearance,
    PreviewStart,
    PreviewField,
    PreviewSpawn,
    PreviewWave,
}

/// <summary>
/// Централизованная семантическая палитра поверх <see cref="ShapeStyle"/>.
/// <see cref="Hue"/> всегда возвращает базовый RGB с полной непрозрачностью;
/// alpha и толщина задаются фабриками и могут зависеть от смысла.
/// </summary>
public static class DrawTheme
{
    // Общие базовые оттенки: одинаковый смысл — один цвет.
    private static readonly Color VisionHue = new(0.78f, 0.42f, 1.00f);        // фиолетовый
    private static readonly Color AttackHue = new(1.00f, 0.30f, 0.28f);        // красный
    private static readonly Color ConstructionHue = new(0.28f, 0.92f, 0.38f); // зелёный
    private static readonly Color RepairHue = new(0.28f, 0.55f, 1.00f);         // синий
    private static readonly Color SelectionHue = new(1.00f, 0.90f, 0.22f);     // жёлтый
    private static readonly Color PathAheadHue = new(0.05f, 0.95f, 0.88f);     // cyan

    /// <summary>Базовый RGB смысла (A = 1).</summary>
    public static Color Hue(VizKind kind) => kind switch
    {
        VizKind.Vision => VisionHue,
        VizKind.Attack or VizKind.OrderAttack => AttackHue,
        VizKind.Work or VizKind.OrderBuild or VizKind.WorkBeamBuild
            or VizKind.PlacementValid => ConstructionHue,
        VizKind.Selection or VizKind.Band => SelectionHue,

        VizKind.OrderMove => new(0.88f, 0.92f, 1.00f),
        VizKind.OrderRepair or VizKind.WorkBeamRepair => RepairHue,
        VizKind.OrderFollow => new(0.62f, 0.68f, 0.80f),

        VizKind.PlacementInvalid => AttackHue,
        VizKind.Footprint => new(0.55f, 0.68f, 1.00f),
        VizKind.FootprintMargin => new(1.00f, 0.82f, 0.28f),

        VizKind.PathAhead => PathAheadHue,
        VizKind.PathBehind => new(0.42f, 0.52f, 0.60f),
        VizKind.PathFailed => new(1.00f, 0.48f, 0.22f),
        VizKind.PathVisited => new(0.55f, 0.65f, 1.00f),

        VizKind.Metal => new(0.92f, 0.48f, 0.18f),

        VizKind.BoidSeek => new(0.45f, 1.00f, 0.40f),
        VizKind.BoidAvoid => new(1.00f, 0.52f, 0.15f),
        VizKind.BoidAlign => new(0.55f, 0.58f, 1.00f),
        VizKind.BoidSpeed => new(1.00f, 1.00f, 1.00f),
        VizKind.BoidSense => new(0.42f, 0.48f, 1.00f),
        VizKind.BoidBody => new(1.00f, 0.82f, 0.28f),
        VizKind.BoidLink => new(1.00f, 0.55f, 0.35f),
        VizKind.BoidCells => new(1.00f, 1.00f, 1.00f),

        VizKind.NavBlocked => new(1.00f, 0.25f, 0.20f),
        VizKind.NavTight => new(1.00f, 0.55f, 0.10f),
        VizKind.NavOpen => new(0.20f, 0.75f, 1.00f),

        VizKind.GridLine => new(1.00f, 1.00f, 1.00f),
        VizKind.GridAxis => new(1.00f, 1.00f, 1.00f),
        VizKind.WorldBorder => new(1.00f, 0.60f, 0.40f),

        // Preview-пояса: красный / оранжевый / синий / пурпурный / жёлтый.
        VizKind.PreviewClearance => new(1.00f, 0.28f, 0.28f),
        VizKind.PreviewStart => new(1.00f, 0.58f, 0.18f),
        VizKind.PreviewField => new(0.30f, 0.52f, 1.00f),
        VizKind.PreviewSpawn => new(0.92f, 0.28f, 0.88f),
        VizKind.PreviewWave => new(1.00f, 0.85f, 0.22f),

        _ => Colors.White,
    };

    /// <summary>
    /// Стиль радиуса: слабая заливка и устойчивый экранный контур.
    /// Применять к кругам и кольцам зон (обзор, атака, работа, preview, boids).
    /// </summary>
    public static ShapeStyle Radius(VizKind kind)
    {
        var hue = Hue(kind);
        var (fillAlpha, strokeAlpha, width, mode) = RadiusParams(kind);
        return ShapeStyle.Filled(
            new Color(hue, fillAlpha),
            new Color(hue, strokeAlpha),
            width,
            mode);
    }

    /// <summary>Контур линии/стрелки/луча с параметрами по смыслу, если не заданы явно.</summary>
    public static ShapeStyle Line(
        VizKind kind,
        float? alpha = null,
        float? width = null,
        WidthMode? mode = null)
    {
        var (defaultAlpha, defaultWidth, defaultMode) = LineParams(kind);
        return ShapeStyle.Outline(
            new Color(Hue(kind), alpha ?? defaultAlpha),
            width ?? defaultWidth,
            mode ?? defaultMode);
    }

    /// <summary>Только заливка заданной прозрачности.</summary>
    public static ShapeStyle Fill(VizKind kind, float alpha) =>
        ShapeStyle.Solid(new Color(Hue(kind), alpha));

    /// <summary>Только контур.</summary>
    public static ShapeStyle Outline(
        VizKind kind,
        float width = 2f,
        WidthMode mode = WidthMode.Screen,
        float alpha = 1f) =>
        ShapeStyle.Outline(new Color(Hue(kind), alpha), width, mode);

    /// <summary>Заливка и контур с явными alpha; толщина и режим — по умолчанию экранные.</summary>
    public static ShapeStyle Filled(
        VizKind kind,
        float fillAlpha,
        float strokeAlpha = 1f,
        float width = 2f,
        WidthMode mode = WidthMode.Screen) =>
        ShapeStyle.Filled(
            new Color(Hue(kind), fillAlpha),
            new Color(Hue(kind), strokeAlpha),
            width,
            mode);

    private static (float FillAlpha, float StrokeAlpha, float Width, WidthMode Mode) RadiusParams(
        VizKind kind) => kind switch
    {
        VizKind.Vision => (0.03f, 0.30f, 1.5f, WidthMode.Screen),
        VizKind.Attack => (0.05f, 0.70f, 2.0f, WidthMode.Screen),
        VizKind.Work => (0.05f, 0.70f, 1.5f, WidthMode.Screen),
        VizKind.Selection => (0.10f, 0.90f, 2.0f, WidthMode.Screen),
        VizKind.Band => (0.08f, 0.70f, 1.5f, WidthMode.MinScreen),
        VizKind.BoidBody => (0.05f, 0.85f, 2.0f, WidthMode.Screen),
        VizKind.BoidSense => (0.04f, 0.55f, 1.5f, WidthMode.MinScreen),
        VizKind.PreviewClearance or VizKind.PreviewStart or VizKind.PreviewField
            or VizKind.PreviewSpawn or VizKind.PreviewWave =>
            (0.08f, 0.70f, 2.0f, WidthMode.Screen),
        _ => (0.05f, 0.70f, 1.5f, WidthMode.Screen),
    };

    private static (float Alpha, float Width, WidthMode Mode) LineParams(VizKind kind) => kind switch
    {
        VizKind.WorkBeamBuild => (0.65f, 2.0f, WidthMode.Screen),
        VizKind.WorkBeamRepair => (0.55f, 2.0f, WidthMode.Screen),
        VizKind.PathAhead => (0.85f, 2.0f, WidthMode.Screen),
        VizKind.PathBehind => (0.50f, 1.5f, WidthMode.MinScreen),
        VizKind.PathFailed => (0.90f, 2.0f, WidthMode.Screen),
        VizKind.BoidLink => (0.35f, 1.5f, WidthMode.MinScreen),
        VizKind.BoidCells => (0.08f, 1.0f, WidthMode.MinScreen),
        VizKind.BoidSpeed => (0.80f, 2.0f, WidthMode.Screen),
        VizKind.GridLine => (0.06f, 1.0f, WidthMode.Screen),
        VizKind.GridAxis => (0.16f, 1.0f, WidthMode.Screen),
        VizKind.WorldBorder => (0.25f, 2.0f, WidthMode.Screen),
        VizKind.Footprint => (0.85f, 2.0f, WidthMode.Screen),
        VizKind.FootprintMargin => (0.50f, 1.5f, WidthMode.MinScreen),
        _ => (1.0f, 2.0f, WidthMode.Screen),
    };
}
