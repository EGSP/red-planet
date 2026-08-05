using System.Collections.Generic;
using Godot;

/// <summary>
/// Предпросмотр мира в редакторе: границы поля, кольца руды, кластеры, окружность
/// появления противника и заполненная область наступления выбранной волны.
///
/// ЗАЧЕМ ОН НУЖЕН. Числа, задающие геометрию партии, подбираются глазом по карте, а до сих
/// пор проверялись запуском. Здесь то же самое видно сразу и, что важнее, видно ВМЕСТЕ:
/// кольца руды, круг появления и WaveStart, от которого отсчитывается форма волны.
///
/// ЧЕМ ОН НЕ ЯВЛЯЕТСЯ. Это не редактор карты и не отдельная модель мира: он ничего не хранит
/// и ничего не создаёт. Геометрию считают те же <see cref="MetalSpotLayout"/>,
/// <see cref="WaveComposer"/> и <see cref="WaveFormation"/>, которые работают в партии.
/// </summary>
[Tool]
public partial class SessionPreview : Node2D
{
    private static readonly Color TextColor = new(0.85f, 0.9f, 1f);

    private static readonly VizKind[] RingKinds =
    {
        VizKind.PreviewRing0,
        VizKind.PreviewRing1,
        VizKind.PreviewRing2,
    };

    private static readonly string[] RingNames =
    {
        "старт",
        "середина",
        "даль",
    };

    // ── Что показывать ────────────────────────────────────────────────────────────

    [ExportGroup("Показывать")]
    [Export] public bool ShowGrid = true;
    [Export] public bool ShowRings = true;
    [Export] public bool ShowMetalSpots = true;
    [Export] public bool ShowClusters = true;
    [Export] public bool ShowWave = true;
    [Export] public bool ShowLegend = true;

    // ── Настройки, которые правятся ───────────────────────────────────────────────

    [ExportGroup("Настройки")]

    /// <summary>
    /// Настройки мира. Тот же ресурс назначен сессии, поэтому правка здесь меняет
    /// и предпросмотр, и партию.
    ///
    /// Класс <see cref="WorldSettings"/> помечен атрибутом Tool именно ради этого поля:
    /// без него редактор подставляет вместо ресурса заглушку базового класса Resource.
    /// </summary>
    [Export] public WorldSettings WorldTuning;

    [Export] public WaveSettings Waves;

    // ── Волна ─────────────────────────────────────────────────────────────────────

    [ExportGroup("Волна")]

    /// <summary>Какую волну показывать. Пусто — первая из справочника.</summary>
    [Export] public string WaveId = "";

    /// <summary>
    /// Показатель террора, для которого считается бюджет. Им же проверяется применимость:
    /// волна, не подходящая по своему terror_range, отмечается в подписи.
    /// </summary>
    [Export(PropertyHint.Range, "0,400,1")] public float Terror = 40f;

    /// <summary>Направление первого очага, градусов. В игре берётся случайным.</summary>
    [Export(PropertyHint.Range, "0,360,1")] public float DirectionDegrees = 200f;

    /// <summary>Сид набора состава: с ним состав волны воспроизводится, а не скачет.</summary>
    [Export] public ulong WaveSeed = 1;

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    [ExportGroup("Действия")]

    [ExportToolButton("Случайная волна")]
    public Callable RollWaveButton => Callable.From(RollWave);

    [ExportToolButton("Новый сид руды")]
    public Callable RollSpotsButton => Callable.From(RollSpots);

    [ExportToolButton("Перечитать содержимое")]
    public Callable ReloadButton => Callable.From(Reload);

    private readonly WaveComposer _composer = new();
    private readonly RandomNumberGenerator _rng = new();

    private Catalog _catalog;
    private string _note = "";

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            QueueRedraw();
    }

    public override void _Draw()
    {
        var settings = WorldTuning ?? World.Settings;

        World.Settings = settings;

        if (ShowGrid)
            DrawGrid();

        if (ShowRings)
            DrawRings(settings);

        WavePreview wave = default;
        string waveNote = null;
        bool haveWave = false;

        if (ShowWave)
        {
            haveWave = TryBuildWavePreview(settings, out wave);
            waveNote = haveWave ? wave.Note : _note;
        }

        if (haveWave)
            DrawWaveShapes(in wave);

        MetalSpotPlan plan = null;

        if (ShowMetalSpots || ShowClusters)
            plan = MetalSpotLayout.Build(settings, Seed(settings), MetalSpotLayout.InsideWorld);

        if (ShowClusters && plan != null)
            DrawClusters(plan);

        if (ShowMetalSpots && plan != null)
            DrawSpots(plan);

        if (haveWave)
            DrawWaveUnits(in wave);

        if (waveNote != null)
            _note = waveNote;

        if (ShowLegend)
            DrawLegend(settings, plan);
    }

    // ── Поле ──────────────────────────────────────────────────────────────────────

    private void DrawGrid()
    {
        int r = World.Radius;
        float min = -r * Const.Unit;
        float max = (r + 1) * Const.Unit;
        var grid = DrawTheme.Line(VizKind.GridLine);

        for (int i = -r; i <= r + 1; i++)
        {
            float p = i * Const.Unit;

            ShapeDraw.Line(this, new Vector2(p, min), new Vector2(p, max), grid);
            ShapeDraw.Line(this, new Vector2(min, p), new Vector2(max, p), grid);
        }

        ShapeDraw.Rect(this, new Rect2(min, min, max - min, max - min),
            DrawTheme.Line(VizKind.WorldBorder, width: 3f));
    }

    /// <summary>
    /// Зачистка у базы, пояса колец руды, окружность появления и отметка WaveStart.
    /// Подписи колец — на оси X у внешнего края каждого пояса, цветом этого пояса.
    /// </summary>
    private void DrawRings(WorldSettings settings)
    {
        var center = Const.LandingPoint;
        float clearance = Mathf.Max(settings.BaseClearancePx, 0f);
        float spawn = settings.SpawnRadiusPx;
        float waveStart = spawn * Mathf.Max(settings.WaveStart, 0.01f);

        ShapeDraw.Circle(this, center, clearance,
            DrawTheme.Radius(VizKind.PreviewClearance));

        float previous = clearance;
        var rings = settings.Rings;

        if (rings != null)
        {
            for (int i = 0; i < rings.Length; i++)
            {
                var ring = rings[i];

                if (ring == null)
                    continue;

                settings.RingBounds(i, out float innerCells, out float outerCells);
                float inner = Mathf.Max(innerCells * Const.Unit, previous);
                float outer = Mathf.Max(outerCells * Const.Unit, inner);
                var kind = RingKind(i);

                if (outer > inner)
                    ShapeDraw.Ring(this, center, inner, outer, DrawTheme.Radius(kind));

                DrawRingLabel(outer, i, ring.Radius, kind);
                previous = outer;
            }
        }

        if (spawn > previous)
            ShapeDraw.Ring(this, center, previous, spawn,
                DrawTheme.Radius(VizKind.PreviewSpawn));

        if (Mathf.Abs(waveStart - spawn) > 1f)
            ShapeDraw.Circle(this, center, waveStart,
                DrawTheme.Outline(VizKind.PreviewWaveStart, width: 2f, alpha: 0.85f));
    }

    private void DrawRingLabel(float outerPx, int index, int thickness, VizKind kind)
    {
        var font = ThemeDB.FallbackFont;
        string name = index < RingNames.Length ? RingNames[index] : $"кольцо {index + 1}";
        string text = $"{name} · {thickness}";
        int size = 48;
        var color = DrawTheme.Hue(kind);

        DrawString(font, new Vector2(outerPx + Const.Unit * 0.25f, size * 0.35f), text,
            HorizontalAlignment.Left, -1f, size, color);
    }

    private static VizKind RingKind(int index) =>
        RingKinds[Mathf.Clamp(index, 0, RingKinds.Length - 1)];

    private void DrawClusters(MetalSpotPlan plan)
    {
        var font = ThemeDB.FallbackFont;
        int size = 36;

        foreach (var cluster in plan.Clusters)
        {
            ShapeDraw.Circle(this, cluster.Center, cluster.RadiusPx,
                DrawTheme.Radius(VizKind.PreviewCluster));

            string label = cluster.Shape.ToString();
            DrawString(font, cluster.Center + new Vector2(cluster.RadiusPx * 0.15f, -size * 0.2f),
                label, HorizontalAlignment.Left, -1f, size,
                new Color(DrawTheme.Hue(VizKind.PreviewCluster), 0.95f));
        }
    }

    private void DrawSpots(MetalSpotPlan plan)
    {
        float half = Const.Unit * 0.35f;
        float alpha = ShowClusters ? 0.75f : 0.9f;

        foreach (var position in plan.Spots)
            ShapeDraw.Rect(this, new Rect2(position - new Vector2(half, half), half * 2f, half * 2f),
                DrawTheme.Fill(VizKind.Metal, alpha));

        if (plan.Spots.Count == 0)
            _note = "точек метала не размещено: поле тесное или кольца пусты";
    }

    private static ulong Seed(WorldSettings settings) =>
        settings.Seed != 0 ? settings.Seed : 1;

    // ── Волна ─────────────────────────────────────────────────────────────────────

    private readonly struct WavePreview
    {
        public readonly WaveShape Shape;
        public readonly List<UnitDefinition> Ordered;
        public readonly float Center;
        public readonly int Groups;
        public readonly string Note;

        public WavePreview(
            WaveShape shape,
            List<UnitDefinition> ordered,
            float center,
            int groups,
            string note)
        {
            Shape = shape;
            Ordered = ordered;
            Center = center;
            Groups = groups;
            Note = note;
        }
    }

    private bool TryBuildWavePreview(WorldSettings settings, out WavePreview preview)
    {
        preview = default;
        var wave = Wave();

        if (wave == null)
        {
            _note = "волн в справочнике нет";
            return false;
        }

        var waveSettings = Waves ?? new WaveSettings();
        var shape = waveSettings.ShapeOf(wave.Shape, settings.SpawnRadiusPx);

        float budget = wave.Budget(Terror);

        _rng.Seed = WaveSeed != 0 ? WaveSeed : 1;
        _composer.Compose(_catalog, wave, budget, _rng);

        var ordered = new List<UnitDefinition>(_composer.Composition);
        ordered.Sort((a, b) => b.ArmyPower.CompareTo(a.ArmyPower));

        float center = Mathf.DegToRad(DirectionDegrees);
        int groups = Mathf.Max(Mathf.Min(shape.Groups, ordered.Count), 1);

        string note = $"{wave.Id}: бюджет {budget:0.0}, потрачено {_composer.Spent:0.0} — " +
                      $"{_composer.Describe()}";

        if (!wave.Fits(Terror))
            note += $". При терроре {Terror:0} эта волна не выпадет";

        preview = new WavePreview(shape, ordered, center, groups, note);
        return true;
    }

    private void DrawWaveShapes(in WavePreview wave)
    {
        for (int g = 0; g < wave.Groups; g++)
        {
            float angle = WaveFormation.GroupAngle(wave.Shape, wave.Center, g);
            DrawWaveSector(wave.Shape, angle);
        }
    }

    private void DrawWaveUnits(in WavePreview wave)
    {
        for (int g = 0; g < wave.Groups; g++)
        {
            float angle = WaveFormation.GroupAngle(wave.Shape, wave.Center, g);
            int index = 0;

            for (int i = g; i < wave.Ordered.Count; i += wave.Groups)
            {
                var (position, _) = WaveFormation.Slot(wave.Shape, angle, index++);
                var definition = wave.Ordered[i];

                DrawUnitSilhouette(position, definition);
            }
        }
    }

    private void DrawUnitSilhouette(Vector2 position, UnitDefinition definition)
    {
        float radius = definition.RadiusPx;
        var style = ShapeStyle.Solid(definition.Color);

        switch (definition.Hull)
        {
            case HullShape.Rect:
            {
                float aspect = Mathf.Max(definition.HullAspect, 0.5f);
                float length = radius * 2f * Mathf.Sqrt(aspect);
                float width = radius * 2f / Mathf.Sqrt(aspect);
                ShapeDraw.Rect(this, new Rect2(position.X - length * 0.5f,
                    position.Y - width * 0.5f, length, width), style);
                break;
            }

            case HullShape.Hex:
            {
                var points = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Tau * i / 6f;
                    points[i] = position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                }

                ShapeDraw.Polygon(this, points, style);
                break;
            }

            default:
                ShapeDraw.Circle(this, position, radius, style);
                break;
        }
    }

    private void DrawWaveSector(WaveShape shape, float center)
    {
        float near = shape.NearRadiusPx;
        float far = near + shape.DepthPx;
        float nearArc = shape.ArcAt(0f);
        float farArc = shape.ArcAt(1f);

        ShapeDraw.RingSector(
            this,
            Vector2.Zero,
            near,
            far,
            center,
            nearArc,
            farArc,
            DrawTheme.Radius(VizKind.PreviewWave));
    }

    private WaveDefinition Wave()
    {
        _catalog ??= Content.Catalog;

        if (!string.IsNullOrEmpty(WaveId))
            return _catalog.Wave(WaveId);

        foreach (var wave in _catalog.Waves)
            return wave;

        return null;
    }

    // ── Подпись ───────────────────────────────────────────────────────────────────

    private void DrawLegend(WorldSettings settings, MetalSpotPlan plan)
    {
        var font = ThemeDB.FallbackFont;
        float y = (World.Radius + 2) * Const.Unit;
        float x = -World.Radius * Const.Unit;
        int size = 64;

        int spots = plan?.Spots.Count ?? 0;
        int clusters = plan?.Clusters.Count ?? 0;
        int rings = settings.Rings?.Length ?? 0;

        string[] lines =
        {
            $"поле {World.Cells}×{World.Cells} клеток, растр " +
            $"{World.NavWidth}×{World.NavWidth} ячеек",

            $"колец {rings}, кластеров {clusters}, точек метала {spots}, " +
            $"появление на {settings.EnemySpawnRadius} клетках, WaveStart {settings.WaveStart:0.00}",

            _note,
        };

        foreach (string line in lines)
        {
            if (!string.IsNullOrEmpty(line))
                DrawString(font, new Vector2(x, y), line,
                    HorizontalAlignment.Left, -1f, size, TextColor);

            y += size * 1.4f;
        }
    }

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    private void RollWave()
    {
        _catalog ??= Content.Catalog;

        var ids = new List<string>();

        foreach (var wave in _catalog.Waves)
            ids.Add(wave.Id);

        if (ids.Count == 0)
            return;

        int index = ids.IndexOf(WaveId);
        WaveId = ids[(index + 1) % ids.Count];
        WaveSeed = GD.Randi() | 1;

        QueueRedraw();
    }

    private void RollSpots()
    {
        var settings = WorldTuning ?? World.Settings;

        settings.Seed = GD.Randi() | 1;
        QueueRedraw();
    }

    private void Reload()
    {
        _catalog = new Catalog();
        _catalog.LoadAll();
        QueueRedraw();
    }
}
