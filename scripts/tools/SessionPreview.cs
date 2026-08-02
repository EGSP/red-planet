using System.Collections.Generic;
using Godot;

/// <summary>
/// Предпросмотр мира в редакторе: границы поля, заполненные пояса размещения,
/// раскладка точек метала и заполненная область наступления выбранной волны.
///
/// ЗАЧЕМ ОН НУЖЕН. Числа, задающие геометрию партии, подбираются глазом по карте, а до сих
/// пор проверялись запуском: правишь радиус — запускаешь — ждёшь две минуты первой волны —
/// смотришь. Здесь то же самое видно сразу и, что важнее, видно ВМЕСТЕ: подвинув поле точек
/// метала, вы двигаете и пояс появления противника, который от него отсчитывается,
/// и кольцевой сектор волны, который отсчитывается уже от внешней границы пояса. Такие
/// зависимости между системами по отдельным полям инспектора не читаются.
///
/// ЧЕМ ОН НЕ ЯВЛЯЕТСЯ. Это не редактор карты и не отдельная модель мира: он ничего не хранит
/// и ничего не создаёт. Геометрию считают те же <see cref="MetalSpotLayout"/>,
/// <see cref="WaveComposer"/> и <see cref="WaveFormation"/>, которые работают в партии,
/// поэтому показанное совпадает с тем, что будет в игре. Единственное расхождение — точки
/// метала: в редакторе стартовой базы нет, и занятость проверяется по одним границам мира,
/// поэтому вблизи центра раскладка может отличаться на одну-две точки.
/// </summary>
[Tool]
public partial class SessionPreview : Node2D
{
    private static readonly Color TextColor = new(0.85f, 0.9f, 1f);

    // ── Что показывать ────────────────────────────────────────────────────────────

    [ExportGroup("Показывать")]
    [Export] public bool ShowGrid = true;
    [Export] public bool ShowRings = true;
    [Export] public bool ShowMetalSpots = true;
    [Export] public bool ShowWave = true;
    [Export] public bool ShowLegend = true;

    // ── Настройки, которые правятся ───────────────────────────────────────────────

    [ExportGroup("Настройки")]

    /// <summary>
    /// Настройки мира. Тот же ресурс назначен сессии, поэтому правка здесь меняет
    /// и предпросмотр, и партию — в этом и смысл: подбирать надо то, во что играешь.
    ///
    /// Класс <see cref="WorldSettings"/> помечен атрибутом Tool именно ради этого поля:
    /// без него редактор подставляет вместо ресурса заглушку базового класса Resource,
    /// и типизированное свойство падало бы с InvalidCastException.
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

    /// <summary>
    /// Перерисовка каждый кадр редактора. Дешевле, чем следить за изменениями: ресурс
    /// правят прямо в инспекторе, и сигнал Changed пришлось бы переподписывать при каждой
    /// смене ссылки, тогда как отрисовка стоит нескольких сотен линий.
    /// </summary>
    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            QueueRedraw();
    }

    public override void _Draw()
    {
        var settings = WorldTuning ?? World.Settings;

        // Показанное должно совпадать с тем, что посчитает игра, поэтому границы мира
        // берутся не из полей ноды, а из действующих настроек
        World.Settings = settings;

        if (ShowGrid)
            DrawGrid();

        if (ShowRings)
            DrawRings(settings);

        // Заливка зон и волны — снизу; точки метала и силуэты юнитов — поверх неё,
        // иначе полупрозрачный сектор закрывает маркеры в своей полосе.
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

        if (ShowMetalSpots)
            DrawSpots(settings);

        if (haveWave)
            DrawWaveUnits(in wave);

        // Подпись волны важнее предупреждения о точках: при включённой волне она
        // должна остаться и после DrawSpots.
        if (waveNote != null)
            _note = waveNote;

        if (ShowLegend)
            DrawLegend(settings);
    }

    // ── Поле ──────────────────────────────────────────────────────────────────────

    private void DrawGrid()
    {
        int r = World.RadiusCells;
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
    /// Четыре непересекающихся пояса, показывающие связь настроек друг с другом: зачистка
    /// у базы, стартовые точки, край поля точек метала и появление противника. Последний
    /// выведен из поля точек множителем, поэтому двигается вместе с ним — увидеть это
    /// иначе, чем рядом, нельзя. Пояса не накладываются, чтобы alpha в центре не складывалась.
    /// </summary>
    private void DrawRings(WorldSettings settings)
    {
        var center = Const.LandingPoint;
        NormalizedRingBounds(settings, out float clearance, out float start, out float field,
            out float spawn);

        // Диск [0, clearance], затем кольца между соседними границами.
        // Пояс нулевой толщины не рисуем: иначе при совпадении границ Ring даёт
        // второй контур поверх уже нарисованного.
        ShapeDraw.Circle(this, center, clearance,
            DrawTheme.Radius(VizKind.PreviewClearance));
        if (start > clearance)
            ShapeDraw.Ring(this, center, clearance, start,
                DrawTheme.Radius(VizKind.PreviewStart));
        if (field > start)
            ShapeDraw.Ring(this, center, start, field,
                DrawTheme.Radius(VizKind.PreviewField));
        if (spawn > field)
            ShapeDraw.Ring(this, center, field, spawn,
                DrawTheme.Radius(VizKind.PreviewSpawn));
    }

    /// <summary>
    /// Монотонные границы поясов: каждый следующий радиус не меньше предыдущего.
    /// При перепутанном порядке в настройках нулевая толщина пояса просто не рисуется,
    /// а геометрия не инвертируется.
    /// </summary>
    private static void NormalizedRingBounds(
        WorldSettings settings,
        out float clearance,
        out float start,
        out float field,
        out float spawn)
    {
        clearance = Mathf.Max(settings.BaseClearancePx, 0f);
        start = Mathf.Max(settings.StartRadiusPx, clearance);
        field = Mathf.Max(settings.FieldRadiusPx, start);
        spawn = Mathf.Max(settings.SpawnRadiusPx, field);
    }

    private void DrawSpots(WorldSettings settings)
    {
        var placed = MetalSpotLayout.Build(settings, Seed(settings), MetalSpotLayout.InsideWorld);
        float half = Const.Unit * 0.35f;

        // Заливкой, а не контуром: поле целиком помещается в вид только при сильном
        // уменьшении, и линия в два пикселя на нём попросту пропадает
        foreach (var position in placed)
            ShapeDraw.Rect(this, new Rect2(position - new Vector2(half, half), half * 2f, half * 2f),
                DrawTheme.Fill(VizKind.Metal, 0.9f));

        if (placed.Count < settings.SpotCount)
            _note = $"точек размещено {placed.Count} из {settings.SpotCount}: " +
                    "поле тесное для их числа и расстояния между ними";
    }

    /// <summary>
    /// Сид раскладки. Ноль в настройках означает «на каждую партию свой», но предпросмотр
    /// при этом не должен мерцать каждый кадр, поэтому нулевое значение заменяется единицей.
    /// Кнопка «новый сид руды» пишет в настройки конкретное число.
    /// </summary>
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

    /// <summary>Заполненный кольцевой сектор формы волны.</summary>
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

    /// <summary>
    /// Числа под полем: то, что иначе пришлось бы считать в уме, сопоставляя поля инспектора.
    /// Рисуется в мировых координатах, поэтому вместе с полем и масштабируется.
    /// </summary>
    private void DrawLegend(WorldSettings settings)
    {
        var font = ThemeDB.FallbackFont;
        float y = (World.RadiusCells + 2) * Const.Unit;
        float x = -World.RadiusCells * Const.Unit;
        // Подпись рисуется в мировых координатах и уменьшается вместе с полем, поэтому
        // кегль задан заметно крупнее обычного: на общем плане иначе не прочесть
        int size = 64;

        string[] lines =
        {
            $"поле {World.Cells}×{World.Cells} клеток, растр " +
            $"{World.NavWidth}×{World.NavWidth} ячеек",

            $"точек метала {settings.SpotCount}, поле до {settings.FieldRadiusCells} клеток, " +
            $"появление на {settings.FieldRadiusCells * settings.RingFactor:0.0} клетках " +
            $"(×{settings.RingFactor:0.00})",

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

    /// <summary>Взять следующую волну справочника. По кругу, чтобы перебрать все подряд.</summary>
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

        // Состав тоже пересобираем: одна и та же волна при разных сидах набирается
        // по-разному, и увиденное на одном сиде за правило принимать нельзя
        WaveSeed = GD.Randi() | 1;

        QueueRedraw();
    }

    private void RollSpots()
    {
        var settings = WorldTuning ?? World.Settings;

        settings.Seed = GD.Randi() | 1;
        QueueRedraw();
    }

    /// <summary>
    /// Перечитать .toml. Нужно потому, что справочник читается один раз за запуск редактора:
    /// правку файла волны иначе было бы видно только после перезапуска.
    /// </summary>
    private void Reload()
    {
        _catalog = new Catalog();
        _catalog.LoadAll();
        QueueRedraw();
    }
}
