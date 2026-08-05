using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Стенд шумов: каждое поле из массива показано отдельным квадратом со своей подписью.
///
/// ЗАЧЕМ ОН НУЖЕН. Поля шума подбираются по виду пятен, а увидеть их до сих пор можно было
/// только через готовую местность, где рисунок поля скрыт покрытиями и декалями. Здесь
/// каждое поле показано само по себе, а несколько полей видны рядом, отчего сравнение
/// частот и контрастности перестаёт требовать переключения между ресурсами.
///
/// ПОЧЕМУ КВАДРАТ РАЗМЕРОМ С МИР. Частота задана в пикселях мира, поэтому крупность пятен
/// осмысленна только относительно арены: поле, дающее четыре пятна на карту, и поле,
/// дающее сорок, различаются лишь при показе на площади карты. Отсюда выборка ведётся по
/// <see cref="World.ArenaBounds"/> всегда, а <see cref="PanelSizePx"/> меняет только размер
/// изображения на холсте, но не область выборки.
///
/// ПОЧЕМУ ПАНЕЛИ РИСУЮТСЯ, А НЕ СОБИРАЮТСЯ УЗЛАМИ. Стенд ничего не хранит и не сохраняет:
/// изображения полей выводятся из зерна за доли секунды, а узлы пришлось бы снимать перед
/// сохранением сцены, как это делает <see cref="SurfaceRenderer"/> ради своего вещества.
/// </summary>
[Tool]
public partial class NoisePreview : Node2D
{
    private static readonly Color TextColor = new(0.85f, 0.9f, 1f);
    private static readonly Color FrameColor = new(1f, 0.55f, 0.2f, 0.85f);
    private static readonly Color EmptyColor = new(0.12f, 0.12f, 0.14f);

    /// <summary>Сколько строк отведено подписи. Лишнее обрезается, место под них занято всегда.</summary>
    private const int LegendLines = 8;

    // ── Поля ──────────────────────────────────────────────────────────────────────

    [ExportGroup("Поля")]

    /// <summary>Панели по порядку. Номер в подписи есть номер элемента массива.</summary>
    [Export] public NoisePreviewEntry[] Entries = Array.Empty<NoisePreviewEntry>();

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    [ExportGroup("Раскладка")]

    /// <summary>Сторона панели в пикселях холста. Ноль означает «со стороной арены».</summary>
    [Export(PropertyHint.Range, "0,16384,64")] public float PanelSizePx;

    /// <summary>Панелей в строке.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int Columns = 3;

    /// <summary>Промежуток между панелями, долей стороны панели.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float GapFactor = 0.08f;

    /// <summary>
    /// Сторона изображения поля в текселях. На значения поля не влияет: они считаются той
    /// же выборкой, что и в партии, а число решает лишь, насколько часто эта выборка взята
    /// для картинки. Умолчание равно <see cref="SurfaceFields.Resolution"/>, то есть
    /// стороне текстуры, которую получает шейдер поверхности: при нём панель показывает
    /// ровно те данные, по которым рисуется базовый слой в игре. Меньшие значения только
    /// ускоряют пересборку, большие — ничего не добавляют.
    /// </summary>
    [Export(PropertyHint.Range, "128,2048,64")] public int Resolution = SurfaceFields.Resolution;

    /// <summary>Высота строки подписи, долей стороны панели.</summary>
    [Export(PropertyHint.Range, "0.005,0.1,0.001")] public float TextScale = 0.026f;

    // ── Настройки ─────────────────────────────────────────────────────────────────

    [ExportGroup("Настройки")]

    /// <summary>
    /// Настройки мира. Нужны ради стороны арены: она задаёт область выборки, поэтому
    /// крупность пятен на панели совпадает с крупностью на карте.
    /// </summary>
    [Export] public WorldSettings WorldTuning;

    /// <summary>
    /// Зерно, на котором строятся все поля стенда. Ноль означает «взять из настроек мира»
    /// — то же правило, что у <see cref="SurfaceRenderer.Seed"/>, поэтому при нуле панели
    /// показывают ту самую картину, которая получится в партии на этом зерне.
    /// </summary>
    [Export] public ulong Seed;

    // ── Показывать ────────────────────────────────────────────────────────────────

    [ExportGroup("Показывать")]

    [Export] public bool ShowLegend = true;
    [Export] public bool ShowTiles = true;
    [Export] public bool ShowDecals = true;

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    [ExportGroup("Действия")]

    [ExportToolButton("Новое зерно")]
    public Callable RollSeedButton => Callable.From(RollSeed);

    [ExportToolButton("Зерно мира")]
    public Callable WorldSeedButton => Callable.From(TakeWorldSeed);

    [ExportToolButton("Пересобрать")]
    public Callable RebuildButton => Callable.From(Rebuild);

    private readonly List<Panel> _panels = new();
    private int _signature;

    /// <summary>Сторона панели на холсте: заданная числом либо равная стороне арены.</summary>
    private float PanelSide =>
        PanelSizePx > 0f ? PanelSizePx : Mathf.Max(World.ArenaBounds.Size.X, 1f);

    /// <summary>
    /// Действующее зерно. Правило выведения повторяет <see cref="SurfaceRenderer"/>
    /// дословно: собственное зерно стенда, иначе зерно партии, иначе единица.
    /// </summary>
    private ulong Grain =>
        Seed != 0 ? Seed : World.Settings.Seed != 0 ? World.Settings.Seed : 1UL;

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (WorldTuning != null)
            World.Settings = WorldTuning;

        var bounds = World.ArenaBounds;

        Refresh(bounds);
        Present(bounds);
    }

    // ── Сборка ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Пересобрать панели, если изменилось то, от чего они зависят. Сравнение идёт по
    /// отпечатку настроек: правка числа в инспекторе отзывается сразу, а неизменные
    /// настройки не стоят ничего.
    /// </summary>
    private void Refresh(Rect2 bounds)
    {
        int wanted = SignatureOf(bounds);

        if (_panels.Count > 0 && _signature == wanted)
            return;

        _panels.Clear();
        _signature = wanted;

        if (Entries == null)
            return;

        for (int i = 0; i < Entries.Length; i++)
        {
            var entry = Entries[i];

            if (entry == null || !entry.Enabled)
                continue;

            _panels.Add(Build(entry, i, bounds));
        }
    }

    private Panel Build(NoisePreviewEntry entry, int index, Rect2 bounds)
    {
        var panel = new Panel();

        if (entry.Noise == null)
        {
            panel.Legend = new[] { $"#{index} · {Title(entry)}", "поле не назначено" };
            return panel;
        }

        // Выключенные покрытие и декаль не показываются: стенд обязан совпадать с партией,
        // а там они выключены тем же флагом
        var tile = ShowTiles && (entry.Tile?.Enabled ?? false) ? entry.Tile : null;

        panel.Field = Bake(entry.Noise, tile, bounds);

        if (ShowDecals && entry.Decal is { Enabled: true, Texture: not null })
            Scatter(entry, panel, bounds);

        panel.Legend = Describe(entry, index, panel.DecalCount);
        return panel;
    }

    /// <summary>
    /// Изображение поля: серый уровень есть значение шума, а покрытие, если назначено,
    /// наложено поверх по своему отрезку значений с тем же размытием краёв, что и в
    /// шейдере поверхности.
    /// </summary>
    private ImageTexture Bake(NoiseSettings settings, SurfaceTile tile, Rect2 bounds)
    {
        var source = settings.Build(Grain);
        var image = Image.CreateEmpty(Resolution, Resolution, false, Image.Format.Rgb8);

        Image texture = tile?.Texture != null ? Readable(tile.Texture) : null;

        float stepX = bounds.Size.X / Resolution;
        float stepY = bounds.Size.Y / Resolution;

        for (int y = 0; y < Resolution; y++)
        {
            float wy = bounds.Position.Y + (y + 0.5f) * stepY;

            for (int x = 0; x < Resolution; x++)
            {
                float wx = bounds.Position.X + (x + 0.5f) * stepX;
                var world = new Vector2(wx, wy);

                float value = settings.Sample(source, world);
                var color = new Color(value, value, value);

                if (texture != null)
                {
                    float weight = Band(value, tile.Range, tile.Falloff);

                    if (weight > 0f)
                        color = color.Lerp(Sample(texture, world, tile) * tile.Tint, weight);
                }

                image.SetPixel(x, y, color);
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Разложить декаль по этому же полю. Раскладку считает <see cref="SurfaceLayout"/> —
    /// тот же класс, что работает в партии; ради этого собирается описание местности из
    /// одного биома во всю ширину температуры, а у копии декали отрезки температуры и
    /// высоты раскрыты полностью, чтобы отбор шёл только по полю шума.
    /// </summary>
    private void Scatter(NoisePreviewEntry entry, Panel panel, Rect2 bounds)
    {
        var decal = (SurfaceDecal)entry.Decal.Duplicate();

        decal.Noise = entry.Noise;
        decal.TemperatureRange = new Vector2(0f, 1f);
        decal.HeightRange = new Vector2(0f, 1f);

        var settings = new SurfaceSettings
        {
            BaseNoise = entry.Noise,
            Temperature = TemperatureSource.Noise,
            TemperatureNoise = entry.Noise,
            TemperatureJitter = 0f,
            Biomes = new[]
            {
                new SurfaceBiome
                {
                    TemperatureRange = new Vector2(0f, 1f),
                    Falloff = 0f,
                    Decals = new[] { decal },
                },
            },
        };

        var fields = new SurfaceFields(settings, Grain, bounds);
        var plan = SurfaceLayout.Build(settings, fields);

        if (plan.Groups.Count == 0)
            return;

        var items = plan.Groups[0].Items;

        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = items.Count,
        };

        for (int k = 0; k < items.Count; k++)
        {
            var item = items[k];

            mesh.SetInstanceTransform2D(k, new Transform2D(
                item.Rotation, new Vector2(item.Size, item.Size), 0f, item.Position));

            mesh.SetInstanceColor(k, item.Modulate);
        }

        panel.Decals = mesh;
        panel.DecalTexture = decal.Texture;
        panel.DecalCount = items.Count;
    }

    // ── Отрисовка ─────────────────────────────────────────────────────────────────

    private void Present(Rect2 bounds)
    {
        if (_panels.Count == 0)
            return;

        var font = ThemeDB.FallbackFont;

        float side = PanelSide;
        float gap = side * GapFactor;
        int size = Mathf.Max(Mathf.RoundToInt(side * TextScale), 1);
        // Место под подпись отводится с запасом: после переноса по ширине панели строк
        // выходит больше, чем их записано в Describe
        float legend = ShowLegend ? size * 1.4f * LegendLines : 0f;
        float scale = side / Mathf.Max(bounds.Size.X, 1f);
        int columns = Mathf.Max(Columns, 1);

        // Раскладка начинается с левого верхнего угла арены: первая панель тогда лежит
        // ровно на области мира, и её видно без поиска камерой
        var start = bounds.Position;

        for (int i = 0; i < _panels.Count; i++)
        {
            var panel = _panels[i];

            var origin = start + new Vector2(
                (i % columns) * (side + gap),
                (i / columns) * (side + gap + legend));

            var rect = new Rect2(origin, new Vector2(side, side));

            if (panel.Field != null)
                DrawTextureRect(panel.Field, rect, false);
            else
                DrawRect(rect, EmptyColor);

            if (panel.Decals != null)
            {
                DrawSetTransform(origin - bounds.Position * scale, 0f, new Vector2(scale, scale));
                DrawMultimesh(panel.Decals, panel.DecalTexture);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            DrawRect(rect, FrameColor, false, Mathf.Max(side * 0.006f, 1f));

            if (!ShowLegend)
                continue;

            // Перенос по ширине панели: строка настроек длиннее панели, а без переноса
            // она наезжает на подпись соседней панели
            DrawMultilineString(font, new Vector2(origin.X, origin.Y + side + size * 1.3f),
                string.Join("\n", panel.Legend), HorizontalAlignment.Left, side, size,
                LegendLines, TextColor);
        }
    }

    // ── Подпись ───────────────────────────────────────────────────────────────────

    private string[] Describe(NoisePreviewEntry entry, int index, int decals)
    {
        var noise = entry.Noise;

        string kind = noise.Kind == NoiseKind.Cellular
            ? $"клеточный · {noise.CellularDistance} · {noise.CellularReturn} · " +
              $"смещение узлов {noise.CellularJitter:0.##}"
            : $"{KindName(noise.Kind)} · октав {noise.Octaves} · " +
              $"лакунарность {noise.Lacunarity:0.##} · стойкость {noise.Persistence:0.##}";

        var lines = new List<string>
        {
            $"#{index} · {Title(entry)} · зерно {Grain}" +
            (Seed == 0 ? " (из настроек мира)" : ""),

            $"частота {noise.Frequency:0.0000} · приближение {noise.Zoom:0.##} · " +
            $"контрастность {noise.Contrast:0.##} · смещение зерна {noise.SeedOffset} · {kind}",
        };

        if (entry.Tile != null)
            lines.Add($"покрытие «{entry.Tile.Id}» на " +
                $"[{entry.Tile.Range.X:0.##}; {entry.Tile.Range.Y:0.##}], " +
                $"размытие {entry.Tile.Falloff:0.###}, сторона {entry.Tile.SizePx:0} px" +
                (entry.Tile.Enabled ? "" : " — выключено"));

        if (entry.Decal != null)
        {
            string bounds = entry.Decal.CountMin != entry.Decal.CountMax
                ? $" при границах [{entry.Decal.CountMin}; " +
                  (entry.Decal.CountMax > 0 ? $"{entry.Decal.CountMax}]" : "без верхней]")
                : "";

            lines.Add($"декаль «{entry.Decal.Id}» на " +
                $"[{entry.Decal.NoiseRange.X:0.##}; {entry.Decal.NoiseRange.Y:0.##}], " +
                $"отпечатков {decals}{bounds}" +
                (entry.Decal.Enabled ? "" : " — выключена"));
        }

        return lines.ToArray();
    }

    /// <summary>Имя панели: заданное в ресурсе либо имя файла поля.</summary>
    private static string Title(NoisePreviewEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Id))
            return entry.Id;

        string path = entry.Noise?.ResourcePath ?? "";
        return string.IsNullOrEmpty(path) ? "без имени" : path.GetFile().GetBaseName();
    }

    private static string KindName(NoiseKind kind) => kind switch
    {
        NoiseKind.Perlin => "Перлин",
        NoiseKind.Value => "по узлам",
        _ => "гладкий",
    };

    // ── Вспомогательное ───────────────────────────────────────────────────────────

    /// <summary>
    /// Изображение текстуры, годное для выборки на процессоре. Импортированные текстуры
    /// хранятся сжатыми под видеокарту, а <see cref="Image.GetPixel"/> со сжатым форматом
    /// не работает, поэтому изображение распаковывается и приводится к RGBA8.
    /// </summary>
    private static Image Readable(Texture2D texture)
    {
        var image = texture.GetImage();

        if (image == null)
            return null;

        if (image.IsCompressed() && image.Decompress() != Error.Ok)
            return null;

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);

        return image;
    }

    /// <summary>Цвет покрытия в точке мира с укладкой по стороне <see cref="SurfaceTile.SizePx"/>.</summary>
    private static Color Sample(Image image, Vector2 world, SurfaceTile tile)
    {
        float step = Mathf.Max(tile.SizePx, 1f);
        int width = image.GetWidth();
        int height = image.GetHeight();

        int x = Mathf.PosMod(Mathf.FloorToInt(world.X / step * width), width);
        int y = Mathf.PosMod(Mathf.FloorToInt(world.Y / step * height), height);

        return image.GetPixel(x, y);
    }

    /// <summary>
    /// Принадлежность отрезку с размытыми краями. Повторяет правило шейдера поверхности,
    /// поэтому граница покрытия на панели проходит там же, где на карте.
    /// </summary>
    private static float Band(float value, Vector2 range, float falloff)
    {
        float low = Mathf.Min(range.X, range.Y);
        float high = Mathf.Max(range.X, range.Y);

        if (falloff <= 0f)
            return value >= low && value <= high ? 1f : 0f;

        float outside = Mathf.Max(low - value, value - high);
        return Mathf.Clamp(1f - outside / falloff, 0f, 1f);
    }

    /// <summary>Отпечаток всего, от чего зависят панели.</summary>
    private int SignatureOf(Rect2 bounds)
    {
        int hash = HashCode.Combine(Grain, bounds.Position, bounds.Size, Resolution,
            ShowTiles, ShowDecals, Entries?.Length ?? 0);

        if (Entries == null)
            return hash;

        foreach (var entry in Entries)
        {
            if (entry == null)
            {
                hash = HashCode.Combine(hash, 0);
                continue;
            }

            hash = HashCode.Combine(hash, entry.Id, entry.Enabled,
                SurfaceFields.SignatureOf(entry.Noise));

            if (entry.Tile != null)
                hash = HashCode.Combine(hash,
                    entry.Tile.Texture?.GetInstanceId() ?? 0UL, entry.Tile.Enabled,
                    entry.Tile.Range, entry.Tile.Falloff, entry.Tile.SizePx, entry.Tile.Tint);

            if (entry.Decal != null)
                hash = HashCode.Combine(hash,
                    entry.Decal.Texture?.GetInstanceId() ?? 0UL,
                    HashCode.Combine(entry.Decal.SpacingPx, entry.Decal.Chance,
                        entry.Decal.Jitter, entry.Decal.NoiseRange,
                        entry.Decal.CountMin, entry.Decal.CountMax),
                    HashCode.Combine(entry.Decal.Enabled, entry.Decal.SizePx,
                        entry.Decal.SizeVariation, entry.Decal.RandomRotation),
                    HashCode.Combine(entry.Decal.Tint, entry.Decal.TintVariation,
                        entry.Decal.Opacity));
        }

        return hash;
    }

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    private void RollSeed()
    {
        Seed = GD.Randi() | 1UL;
        Rebuild();
    }

    /// <summary>
    /// Вернуть стенд к зерну партии. Обнуление собственного зерна и означает «брать из
    /// настроек мира», поэтому отдельного переключателя не нужно.
    /// </summary>
    private void TakeWorldSeed()
    {
        Seed = 0;
        Rebuild();
    }

    private void Rebuild()
    {
        _panels.Clear();
        _signature = 0;
        QueueRedraw();
    }

    /// <summary>Собранная панель: изображение поля, отпечатки декали и строки подписи.</summary>
    private sealed class Panel
    {
        public ImageTexture Field;
        public MultiMesh Decals;
        public Texture2D DecalTexture;
        public int DecalCount;
        public string[] Legend = Array.Empty<string>();
    }
}
