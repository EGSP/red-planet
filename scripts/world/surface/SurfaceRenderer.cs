using Godot;

/// <summary>
/// Отрисовка поверхности: базовый слой одним прямоугольником и статические декали
/// множественными сетками поверх него.
///
/// ПОЧЕМУ ОДИН УЗЕЛ НА ТО И ДРУГОЕ. Оба слоя питаются одними и теми же полями: шумом,
/// температурой и высотой, посчитанными от зерна партии. Разведённые по двум узлам, они
/// считали бы эти поля дважды и разошлись бы при первом же расхождении настроек.
/// Порядок отрисовки при этом соблюдается сам собой: холст рисует сначала себя, потом
/// потомков, поэтому декали ложатся поверх базового слоя.
///
/// ПОЧЕМУ БАЗА НЕ ЗАПЕКАЕТСЯ. При <see cref="CameraRig.ZoomMax"/> равном 2.5 запечённая в
/// разрешении мира текстура растягивается в два с половиной раза и заметно мылится, а
/// запекание с запасом по резкости заняло бы около 170 МБ. Живой шейдер выбирает из тайла
/// в его собственном разрешении при любом приближении и памяти сверх самих тайлов не
/// требует.
///
/// ПОЧЕМУ ДЕКАЛИ РИСУЮТСЯ КАЖДЫЙ КАДР, А НЕ ШТАМПУЮТСЯ В БУФЕР. Их число ограничено сверху
/// правилами размещения и составляет тысячи, а закраска ограничена площадью экрана,
/// помноженной на среднюю глубину наложения, и от приближения почти не зависит: при
/// отдалении отпечатки мелкие и многочисленные, при приближении крупные и редкие.
/// Накопительные следы боя, число которых ничем не ограничено, — другое дело, и для них
/// буфер оправдан.
///
/// РАБОТАЕТ В РЕДАКТОРЕ. Узел помечен атрибутом Tool, зерно берёт из собственного поля либо
/// из настроек мира, и сессии для работы не требует. Отсюда предпросмотр местности тем же
/// узлом, что рисует её в партии.
/// </summary>
[Tool]
public partial class SurfaceRenderer : Node2D
{
    private const string ShaderPath = "res://resources/shaders/surface.gdshader";

    /// <summary>Имя узла отпечатков: по нему они находятся и убираются при пересборке.</summary>
    private const string DecalPrefix = "Decals_";

    [ExportGroup("Местность")]

    /// <summary>Описание местности. Не назначено — поверхность не рисуется вовсе.</summary>
    [Export] public SurfaceSettings Settings;

    /// <summary>
    /// Зерно поверхности. Ноль означает «взять из настроек мира»: тогда поверхность,
    /// расположение руды и состав волн выводятся из одного числа, и партия воспроизводится
    /// целиком.
    /// </summary>
    [Export] public ulong Seed;

    [ExportGroup("Показывать")]

    [Export] public bool ShowBase = true;
    [Export] public bool ShowDecals = true;

    [ExportGroup("Действия")]

    [ExportToolButton("Новое зерно поверхности")]
    public Callable RollSeedButton => Callable.From(RollSeed);

    [ExportToolButton("Пересобрать")]
    public Callable RebuildButton => Callable.From(Rebuild);

    /// <summary>Поля местности. Нужны предпросмотру для подписи и разбора.</summary>
    public SurfaceFields Fields { get; private set; }

    /// <summary>Раскладка декалей. Нужна предпросмотру для подписи.</summary>
    public SurfacePlan Plan { get; private set; }

    private ShaderMaterial _material;
    private ImageTexture _white;
    private Rect2 _area;
    private int _fieldsSignature;
    private int _planSignature;

    public override void _Ready()
    {
        // Назначенное в сцене вещество берётся как есть: узел с атрибутом Tool иначе
        // создавал бы новое при каждой загрузке, а редактор сохранял бы его в сцену
        if (Material is ShaderMaterial assigned)
            _material = assigned;
        else
            Material = _material = new ShaderMaterial();

        _material.Shader ??= GD.Load<Shader>(ShaderPath);
    }

    public override void _Process(double delta)
    {
        Visible = Settings != null && (ShowBase || ShowDecals);

        if (!Visible)
        {
            Clear();
            return;
        }

        _material ??= Material as ShaderMaterial;

        var area = World.ArenaBounds;
        ulong seed = Seed != 0 ? Seed : World.Settings.Seed != 0 ? World.Settings.Seed : 1UL;

        Refresh(area, seed);
        Apply(area);

        if (_area != area)
        {
            _area = area;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!ShowBase || _material == null || Settings == null)
            return;

        // Прямоугольник рисуется белой точкой: сам цвет берёт шейдер, а текстура нужна лишь
        // затем, чтобы холст выдал UV, растянутое по области мира
        _white ??= White();
        DrawTextureRect(_white, _area, false);
    }

    /// <summary>
    /// Пересобрать поля и раскладку, если изменилось то, от чего они зависят. Сравнение
    /// идёт по отпечатку настроек, поэтому правка числа в инспекторе отзывается сразу,
    /// а неизменные настройки не стоят ничего.
    /// </summary>
    private void Refresh(Rect2 area, ulong seed)
    {
        int wanted = SurfaceFields.SignatureOf(Settings, seed, area);

        if (Fields == null || _fieldsSignature != wanted)
        {
            Fields = new SurfaceFields(Settings, seed, area);
            _fieldsSignature = wanted;
            _planSignature = 0;
            QueueRedraw();
        }

        if (!ShowDecals)
        {
            ClearDecals();
            _planSignature = 0;
            return;
        }

        int plan = SurfaceLayout.SignatureOf(Settings, Fields);

        if (Plan == null || _planSignature != plan)
        {
            Plan = SurfaceLayout.Build(Settings, Fields);
            _planSignature = plan;
            Present(Plan);
        }
    }

    /// <summary>Передать шейдеру поля, покрытия и настройки смешивания.</summary>
    private void Apply(Rect2 area)
    {
        if (_material == null)
            return;

        _material.Shader ??= GD.Load<Shader>(ShaderPath);

        _material.SetShaderParameter("rect_origin", area.Position);
        _material.SetShaderParameter("rect_size", area.Size);
        _material.SetShaderParameter("field_noise", Fields?.BaseTexture);
        _material.SetShaderParameter("field_height", Fields?.HeightTexture);
        _material.SetShaderParameter("use_height", Fields?.HasHeight ?? false);
        _material.SetShaderParameter("hex_tiling", Settings.HexTiling);
        _material.SetShaderParameter("sharpness", Mathf.Max(Settings.Sharpness, 1f));
        _material.SetShaderParameter("ambient", Settings.Ambient);

        var tiles = Settings.Tiles;
        int count = Mathf.Min(tiles?.Length ?? 0, SurfaceSettings.MaxTiles);

        _material.SetShaderParameter("tile_count", count);

        for (int i = 0; i < SurfaceSettings.MaxTiles; i++)
        {
            var tile = i < count ? tiles[i] : null;

            _material.SetShaderParameter($"tile_{i}", tile?.Texture);

            _material.SetShaderParameter($"tile_range_{i}", new Vector4(
                tile?.Range.X ?? 0f,
                tile?.Range.Y ?? 0f,
                Mathf.Max(tile?.Falloff ?? 0f, 0f),
                Mathf.Max(tile?.SizePx ?? 512f, 1f)));

            _material.SetShaderParameter($"tile_height_{i}", new Vector4(
                tile?.HeightRange.X ?? 0f, tile?.HeightRange.Y ?? 1f, 0f, 0f));

            _material.SetShaderParameter($"tile_tint_{i}", tile?.Tint ?? Colors.White);
        }
    }

    // ── Отпечатки ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Разложить раскладку по узлам: одна декаль — одна множественная сетка, порядок
    /// потомков и есть порядок наложения.
    ///
    /// АТЛАС НЕ НУЖЕН. Декалей на местность около десятка, значит и вызовов отрисовки
    /// столько же; собирать их в один массив текстур пришлось бы ценой приведения всех
    /// изображений к одному размеру, чего исходное содержимое не допускает.
    /// </summary>
    private void Present(SurfacePlan plan)
    {
        ClearDecals();

        if (plan == null)
            return;

        var quad = new QuadMesh { Size = Vector2.One };

        for (int i = 0; i < plan.Groups.Count; i++)
        {
            var group = plan.Groups[i];
            var items = group.Items;

            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = quad,
                InstanceCount = items.Count,
            };

            for (int k = 0; k < items.Count; k++)
            {
                var item = items[k];

                mesh.SetInstanceTransform2D(k, new Transform2D(
                    item.Rotation, new Vector2(item.Size, item.Size), 0f, item.Position));

                mesh.SetInstanceColor(k, item.Modulate);
            }

            var node = new MultiMeshInstance2D
            {
                Name = $"{DecalPrefix}{i}_{group.Decal.Id}",
                Multimesh = mesh,
                Texture = group.Decal.Texture,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };

            // Owner не назначается намеренно: узлы собраны по описанию местности и
            // сохраняться в сцену не должны
            AddChild(node);
        }
    }

    private void ClearDecals()
    {
        foreach (var child in GetChildren())
        {
            if (child is MultiMeshInstance2D node && node.Name.ToString().StartsWith(DecalPrefix))
            {
                RemoveChild(node);
                node.QueueFree();
            }
        }
    }

    private void Clear()
    {
        ClearDecals();
        Plan = null;
        _planSignature = 0;
    }

    // ── Кнопки ────────────────────────────────────────────────────────────────────

    private void RollSeed()
    {
        Seed = GD.Randi() | 1UL;
        Rebuild();
    }

    private void Rebuild()
    {
        _fieldsSignature = 0;
        _planSignature = 0;
        Fields = null;
        QueueRedraw();
    }

    /// <summary>Белая точка: холсту нужна текстура, чтобы выдать шейдеру UV прямоугольника.</summary>
    private static ImageTexture White()
    {
        var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.SetPixel(0, 0, Colors.White);

        return ImageTexture.CreateFromImage(image);
    }
}
