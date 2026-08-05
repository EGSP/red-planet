using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Предпросмотр застройки в редакторе: случайный набор построек, поле chamfer-расстояний
/// и тень, которая из этого поля выводится.
///
/// ЗАЧЕМ ОН НУЖЕН. Плотность тени и ширина полосы подбираются глазом, а увидеть их в партии
/// можно только после того, как база построена, — то есть спустя минуты после каждой правки
/// числа. Здесь набор построек создаётся кнопкой, и рядом видны обе картинки сразу: растр,
/// по которому навигация судит о проходимости, и тень, которая должна о том же говорить игроку.
///
/// ЧЕМ ОН НЕ ЯВЛЯЕТСЯ. Это не редактор карты и не сессия без интерфейса. Постройки здесь —
/// прямоугольники, а не сущности: ни узлов, ни здоровья, ни работы у них нет, и правило
/// постановки соблюдается ровно в одном пункте — обязательный зазор между соседями.
/// Растр собирается тем же <see cref="NavBuilder"/>, что и в партии, но целиком и в главном
/// потоке: инкрементальность и фоновое задание принадлежат <see cref="NavGrid"/>,
/// а его здесь нет, поскольку нет и сессии.
/// </summary>
[Tool]
public partial class BuildingsPreview : Node2D
{
    private static readonly Color TextColor = new(0.85f, 0.9f, 1f);

    /// <summary>Радиус, по которому считается признак тесноты на картинке растра.</summary>
    private const float SampleRadius = Const.Unit * 0.35f;

    /// <summary>Сколько попыток постановки приходится на одну постройку набора.</summary>
    private const int AttemptsPerBuilding = 24;

    // ── Что показывать ────────────────────────────────────────────────────────────

    [ExportGroup("Показывать")]
    [Export] public bool ShowGrid = true;
    [Export] public bool ShowBuildings = true;

    /// <summary>Растр chamfer-расстояний: непроходимость, теснота и запас свободного места.</summary>
    [Export] public bool ShowClearance;

    /// <summary>
    /// Тень препятствий. Рисует её узел <see cref="Shadow"/>, то есть ровно тот же класс,
    /// что и в партии; выключение снимает у него источник расстояний, и он гаснет сам.
    /// </summary>
    [Export] public bool ShowShadow = true;

    [Export] public bool ShowLegend = true;

    // ── Настройки, которые правятся ───────────────────────────────────────────────

    [ExportGroup("Настройки")]

    /// <summary>Настройки мира. Тот же ресурс назначен сессии: правка меняет обе картины.</summary>
    [Export] public WorldSettings WorldTuning;

    /// <summary>Настройки навигации: от <c>MaxClearance</c> зависит предел ширины тени.</summary>
    [Export] public NavSettings NavTuning;

    /// <summary>
    /// Настройки тени. Их правка — и есть основная работа в этой сцене; поле
    /// <see cref="ShadowSettings.Enabled"/> здесь не дублируется признаком показа,
    /// поскольку значит ровно то же самое.
    /// </summary>
    [Export] public ShadowSettings ShadowTuning;

    /// <summary>
    /// Узел отрисовки тени. Лежит ниже по <c>z_index</c>: в партии эту роль исполняет слой
    /// <see cref="WorldLayer.Shadows"/>, а у предпросмотра слоёв нет.
    ///
    /// Не назначен — берётся потомок с тем же именем, как это делает
    /// <see cref="Playground.Layer"/>. Ссылка на узел разрешается при загрузке сцены, и если
    /// сборка в тот миг ещё не поднята, редактор теряет её при первом же сохранении;
    /// поиск по имени эту зависимость от порядка снимает.
    /// </summary>
    [Export] public ShadowRenderer Shadow;

    // ── Застройка ─────────────────────────────────────────────────────────────────

    [ExportGroup("Застройка")]

    /// <summary>Сколько построек ставить. Столько же и получится, если места хватило.</summary>
    [Export(PropertyHint.Range, "0,400,1")] public int Buildings = 60;

    /// <summary>
    /// Сколько сгущений образует набор. Смысл в том, что равномерная россыпь узких проходов
    /// не даёт вовсе, а ради них предпросмотр и заведён: тесные места возникают там,
    /// где постройки жмутся друг к другу.
    /// </summary>
    [Export(PropertyHint.Range, "1,16,1")] public int Clusters = 5;

    /// <summary>Разброс построек вокруг центра сгущения, клеток карты.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public float ClusterSpread = 5f;

    /// <summary>Сид набора: с ним застройка воспроизводится, а не скачет при каждой правке.</summary>
    [Export] public ulong Seed = 1;

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    [ExportGroup("Действия")]

    [ExportToolButton("Случайная застройка")]
    public Callable RollButton => Callable.From(Roll);

    [ExportToolButton("Пересобрать")]
    public Callable RebuildButton => Callable.From(Invalidate);

    [ExportToolButton("Перечитать содержимое")]
    public Callable ReloadButton => Callable.From(Reload);

    private readonly List<Obb> _shapes = new();
    private readonly List<Color> _colors = new();

    private Catalog _catalog;
    private NavSnapshot _snapshot;
    private Image _image;
    private ImageTexture _texture;
    private byte[] _pixels;

    /// <summary>Отпечаток входных величин: по нему видно, что набор надо разложить заново.</summary>
    private long _built = long.MinValue;

    /// <summary>Ревизия набора. Растёт при пересборке — по ней отрисовка тени видит правку.</summary>
    private int _revision;

    private string _note = "";

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        // Признак редактора здесь не спрашивается намеренно: сцена одинаково работает
        // и в редакторе, и запущенной, а второй способ нужен для проверки без щелчков
        if (WorldTuning != null)
            World.Settings = WorldTuning;

        if (NavTuning != null)
            NavGrid.Settings = NavTuning;

        Ensure();
        Publish();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (ShowGrid)
            DrawGrid();

        if (ShowClearance && _texture != null)
            DrawTextureRect(_texture, World.Bounds, false);

        if (ShowBuildings)
            DrawBuildings();

        if (ShowLegend)
            DrawLegend();
    }

    // ── Сборка набора ─────────────────────────────────────────────────────────────

    /// <summary>Разложить постройки и собрать растр, если входные величины изменились.</summary>
    private void Ensure()
    {
        long signature = Signature();

        if (_built == signature && _snapshot != null)
            return;

        _built = signature;
        _revision++;

        Layout();
        Build();
        Repaint();
    }

    private long Signature()
    {
        int maxClearance = Mathf.Max(NavGrid.Settings?.MaxClearance ?? 12, NavGrid.Straight);

        long signature = (long)(Seed != 0 ? Seed : 1);
        signature = signature * 31 + Buildings;
        signature = signature * 31 + Clusters;
        signature = signature * 31 + Mathf.RoundToInt(ClusterSpread * 100f);
        signature = signature * 31 + World.Radius;
        signature = signature * 31 + maxClearance;
        return signature;
    }

    /// <summary>
    /// Разложить постройки по сгущениям. Место занимается по тому же правилу, что и в партии:
    /// прямоугольник не пересекает чужой, раздутый на обязательный зазор. Прочие поправки
    /// постановки — прилипание, привязка экстрактора к руде — здесь не воспроизводятся:
    /// растру всё равно, откуда взялся прямоугольник.
    /// </summary>
    private void Layout()
    {
        _shapes.Clear();
        _colors.Clear();

        var definitions = Structures();

        if (definitions.Count == 0)
        {
            _note = "в справочнике нет строений с формой: ставить нечего";
            return;
        }

        var rng = new RandomNumberGenerator { Seed = Seed != 0 ? Seed : 1 };

        int clusters = Mathf.Max(Clusters, 1);
        var centers = new Vector2[clusters];
        float reach = World.Radius * Const.Unit;

        for (int i = 0; i < clusters; i++)
            centers[i] = new Vector2(rng.RandfRange(-reach, reach), rng.RandfRange(-reach, reach));

        float spread = Mathf.Max(ClusterSpread, 0.5f) * Const.Unit;
        int attempts = Mathf.Max(Buildings, 0) * AttemptsPerBuilding;

        for (int attempt = 0; attempt < attempts && _shapes.Count < Buildings; attempt++)
        {
            var definition = definitions[rng.RandiRange(0, definitions.Count - 1)];
            var anchor = centers[rng.RandiRange(0, clusters - 1)];

            var point = anchor + new Vector2(
                rng.Randfn(0f, spread),
                rng.Randfn(0f, spread));

            if (!TryPlace(definition, point, out var shape))
                continue;

            _shapes.Add(shape);
            _colors.Add(definition.Color);
        }

        _note = _shapes.Count < Buildings
            ? $"размещено {_shapes.Count} из {Buildings}: сгущения тесны для остального"
            : "";
    }

    /// <summary>
    /// Поставить прямоугольник строения так, чтобы он занял целые клетки карты, целиком
    /// уместился в поле и не задел соседей с зазором.
    /// </summary>
    private bool TryPlace(UnitDefinition definition, Vector2 point, out Obb shape)
    {
        var size = new Vector2(definition.Size.X, definition.Size.Y) * Const.Unit;

        // Выравнивание по клетке карты: без него прямоугольники встают вразнобой,
        // и растр показывал бы щели там, где их создало округление, а не застройка
        var cell = new Vector2(
            Mathf.Floor(point.X / Const.Unit),
            Mathf.Floor(point.Y / Const.Unit));

        var center = (cell + new Vector2(definition.Size.X, definition.Size.Y) * 0.5f) * Const.Unit;

        shape = new Obb(center, size);

        if (!World.Bounds.Encloses(shape.Bounds))
            return false;

        foreach (var placed in _shapes)
        {
            if (placed.Grow(Const.BuildMarginPx).Intersects(shape))
                return false;
        }

        return true;
    }

    /// <summary>Строения справочника, у которых задана форма: только они занимают место.</summary>
    private List<UnitDefinition> Structures()
    {
        _catalog ??= Content.Catalog;

        var result = new List<UnitDefinition>();

        foreach (var definition in _catalog.Units)
        {
            if (definition.IsStructure && definition.Occupies)
                result.Add(definition);
        }

        return result;
    }

    /// <summary>
    /// Собрать растр целиком. Инкрементальность здесь не нужна: набор меняется кнопкой,
    /// а не по ходу партии, и полная сборка поля в полтораста ячеек по стороне занимает
    /// доли миллисекунды.
    /// </summary>
    private void Build()
    {
        int width = World.NavWidth;

        if (width <= 0)
        {
            _snapshot = null;
            return;
        }

        _snapshot = NavBuilder.Build(new NavBuilder.Request
        {
            SourceRevision = _revision,
            Width = width,
            WorldMin = World.Min,
            Cell = NavGrid.Cell,
            MaxClearance = Mathf.Max(NavGrid.Settings?.MaxClearance ?? 12, NavGrid.Straight),
            Shapes = _shapes.ToArray(),
            DirtyWorld = World.Bounds,
            RebuildAll = true,
            Previous = null,
            ComponentThresholds = Array.Empty<int>(),
        });
    }

    /// <summary>Отдать собранное отрисовщику тени — тому же классу, что работает в партии.</summary>
    private void Publish()
    {
        if (Shadow == null || !IsInstanceValid(Shadow))
            Shadow = GetNodeOrNull<ShadowRenderer>(nameof(Shadow));

        if (Shadow == null)
            return;

        // Пустое назначение заставило бы отрисовщик создавать умолчания каждый кадр
        if (ShadowTuning != null)
            Shadow.Settings = ShadowTuning;

        // Снятый источник гасит отрисовщик: растра сессии в редакторе нет, и подставить
        // вместо назначенного поля ему нечего
        Shadow.Field = ShowShadow ? _snapshot : null;
    }

    // ── Растр ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Перерисовать картинку растра. Цветовой язык тот же, что у отладочной выкладки
    /// в партии (<see cref="NavGridOverlay"/>): непроходимое, тесное и открытое с запасом.
    ///
    /// Цвета складываются в буфер, а не пишутся вызовом на ячейку: поле в полтысячи ячеек
    /// по стороне — это четверть миллиона обращений, и на каждую перекладку набора они
    /// были бы заметны рывком в редакторе.
    /// </summary>
    private void Repaint()
    {
        if (_snapshot == null)
            return;

        int width = _snapshot.Width;
        int area = width * width;

        if (_pixels == null || _pixels.Length != area * 4)
            _pixels = new byte[area * 4];

        int required = NavGrid.Required(SampleRadius);
        int saturation = Mathf.Max(NavGrid.Settings?.MaxClearance ?? 12, NavGrid.Straight);

        for (int i = 0; i < area; i++)
        {
            var color = Tint(_snapshot.DistanceAt(i), required, saturation);
            int at = i * 4;

            _pixels[at] = (byte)(color.R * 255f);
            _pixels[at + 1] = (byte)(color.G * 255f);
            _pixels[at + 2] = (byte)(color.B * 255f);
            _pixels[at + 3] = (byte)(color.A * 255f);
        }

        if (_image == null || _image.GetWidth() != width)
        {
            _image = Image.CreateFromData(width, width, false, Image.Format.Rgba8, _pixels);
            _texture = ImageTexture.CreateFromImage(_image);
            return;
        }

        _image.SetData(width, width, false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
    }

    private static Color Tint(int distance, int required, int saturation)
    {
        if (distance <= 0)
            return new Color(DrawTheme.Hue(VizKind.NavBlocked), 0.55f);

        if (distance < required)
            return new Color(DrawTheme.Hue(VizKind.NavTight), 0.4f);

        float depth = Mathf.Clamp(distance / (float)saturation, 0f, 1f);
        var open = DrawTheme.Hue(VizKind.NavOpen);
        return new Color(open.R, 0.55f + depth * 0.4f, open.B, 0.08f + depth * 0.22f);
    }

    // ── Отрисовка ─────────────────────────────────────────────────────────────────

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

    private void DrawBuildings()
    {
        for (int i = 0; i < _shapes.Count; i++)
        {
            var shape = _shapes[i];

            ShapeDraw.Obb(this, shape, ShapeStyle.Solid(_colors[i]));
            ShapeDraw.Obb(this, shape, DrawTheme.Line(VizKind.Footprint));
        }
    }

    private void DrawLegend()
    {
        var font = ThemeDB.FallbackFont;
        float y = (World.Radius + 2) * Const.Unit;
        float x = -World.Radius * Const.Unit;
        int size = 64;

        var tuning = ShadowTuning;
        float width = tuning != null
            ? Mathf.Min(tuning.WidthPx, ShadowSettings.MaxWidthPx)
            : 0f;

        string[] lines =
        {
            $"поле {World.Cells}×{World.Cells} клеток, растр {World.NavWidth}×{World.NavWidth} ячеек, " +
            $"построек {_shapes.Count} в {Mathf.Max(Clusters, 1)} сгущениях",

            $"насыщение растра {Mathf.Max(NavGrid.Settings?.MaxClearance ?? 12, NavGrid.Straight)} " +
            $"третей ячейки, предел ширины тени {ShadowSettings.MaxWidthPx:0} px, " +
            $"сейчас {width:0} px",

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

    private void Roll()
    {
        Seed = GD.Randi() | 1;
        Invalidate();
    }

    private void Invalidate()
    {
        _built = long.MinValue;
        QueueRedraw();
    }

    private void Reload()
    {
        _catalog = new Catalog();
        _catalog.LoadAll();
        Invalidate();
    }
}
