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
/// ПОЧЕМУ БАЗА НЕ ЗАПЕКАЕТСЯ. При <see cref="CameraSettings.ZoomMax"/> порядка 2.5 запечённая в
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

    /// <summary>
    /// Группа узлов отпечатков: по ней они находятся и убираются при пересборке.
    /// Группа переживает и перезагрузку сборки, и переименование узла движком, поэтому
    /// служит приметой надёжнее имени.
    /// </summary>
    private const string DecalGroup = "SurfaceDecals";

    [ExportGroup("Местность")]

    /// <summary>Описание местности. Не назначено — поверхность не рисуется вовсе.</summary>
    [Export] public SurfaceSettings Settings;

    /// <summary>
    /// Кандидаты на случайный выбор при старте партии. Пусто — остаётся
    /// <see cref="Settings"/>. В редакторе не выбирается: иначе предпросмотр и сцена
    /// менялись бы при каждом открытии.
    /// </summary>
    [Export] public SurfaceSettings[] Terrains;

    /// <summary>
    /// Зерно поверхности. Ноль означает «взять из настроек мира»: тогда поверхность,
    /// расположение руды и состав волн выводятся из одного числа, и партия воспроизводится
    /// целиком.
    /// </summary>
    [Export] public ulong Seed;

    [ExportGroup("Облака")]

    /// <summary>
    /// Отрисовщик облаков. При смене <see cref="Settings"/> получает
    /// <see cref="SurfaceSettings.Clouds"/>, если они заданы. На стенде предпросмотра
    /// облаков ссылка не нужна: там настройки назначаются напрямую.
    /// </summary>
    [Export] public CloudRenderer Clouds;

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

    /// <summary>Включённые покрытия по порядку. Поле, а не местный массив: заполняется каждый кадр.</summary>
    private readonly SurfaceTile[] _active = new SurfaceTile[SurfaceSettings.MaxTiles];

    private ShaderMaterial _material;
    private ImageTexture _white;
    private Rect2 _area;
    private int _fieldsSignature;
    private int _planSignature;

    public override void _Ready()
    {
        // Отпечатки, оставшиеся от прошлой жизни узла, снимаются при входе в дерево.
        // Обычно их нет, но если скрипт перестал исполняться (в редакторе это случается
        // после перезагрузки сборки), созданные им узлы остаются в дереве и продолжают
        // рисоваться, не подчиняясь уже никаким переключателям. Открытие сцены заново
        // должно давать чистое состояние.
        ClearDecals();

        if (!Engine.IsEditorHint())
            BeginParty();

        Attach();
    }

    /// <summary>
    /// Случайная местность и зерно шумов на партию. В редакторе не вызывается.
    /// </summary>
    private void BeginParty()
    {
        PickTerrain();
        ApplyClouds();

        // Ноль означает «ещё не задано»: иначе поверхность брала бы запасное зерно 1
        // из правила в _Process, и партии отличались бы только типом местности
        if (Seed == 0)
            Seed = GD.Randi() | 1UL;
    }

    /// <summary>Выбрать местность из кандидатов. Один раз на партию, до сборки полей.</summary>
    private void PickTerrain()
    {
        if (Terrains == null || Terrains.Length == 0)
            return;

        int count = 0;

        foreach (var candidate in Terrains)
        {
            if (candidate != null)
                count++;
        }

        if (count == 0)
            return;

        int pick = (int)(GD.Randi() % (uint)count);

        foreach (var candidate in Terrains)
        {
            if (candidate == null)
                continue;

            if (pick == 0)
            {
                Settings = candidate;
                return;
            }

            pick--;
        }
    }

    /// <summary>
    /// Передать отрисовщику облаков вид из текущей местности. Пустые Clouds у местности
    /// не затирают уже назначенные настройки: стенд предпросмотра и запасной ресурс в
    /// сцене остаются в силе.
    /// </summary>
    private void ApplyClouds()
    {
        if (Clouds == null || Settings?.Clouds == null)
            return;

        if (Clouds.Settings != Settings.Clouds)
            Clouds.Settings = Settings.Clouds;
    }

    /// <summary>
    /// Снять вещество перед сохранением сцены и собрать заново после.
    ///
    /// ЗАЧЕМ ЭТО НУЖНО. Редактор сохраняет всё, что лежит в свойствах узла, а в веществе
    /// поверхности лежат запечённые поля — две текстуры по мегабайту. Без этой развязки
    /// они попадают в саму сцену встроенными ресурсами, и файл сцены разрастается до
    /// десятка мегабайт, причём с устаревшими на момент сохранения данными. Поля считаются
    /// из зерна за доли секунды, поэтому хранить их незачем.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationEditorPreSave)
            Detach();
        else if (what == NotificationEditorPostSave)
            Attach();
    }

    public override void _Process(double delta)
    {
        ApplyClouds();

        Visible = Settings != null && (ShowBase || ShowDecals);

        if (!Visible)
        {
            Clear();
            return;
        }

        Attach();

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
            return;
        }

        // Сверка дерева с раскладкой. Отпечаток говорит лишь о том, что настройки не
        // менялись, но узлы отпечатков живут в дереве и могут разойтись с раскладкой по
        // причинам вне этого кода: в редакторе узлы переживают перезагрузку сборки, при
        // которой объект C# заменяется, а созданное им остаётся. Несовпадение числа узлов
        // с числом наборов — признак именно такого расхождения, и лечится оно повторной
        // раскладкой по узлам, а не пересчётом раскладки
        if (DecalNodes() != Plan.Groups.Count)
            Present(Plan);
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
        _material.SetShaderParameter("field_temperature", Fields?.TemperatureTexture);
        _material.SetShaderParameter("use_height", Fields?.HasHeight ?? false);

        // Оверлеи полей — только подбор в редакторе. В партии флаги из .tres не читаются:
        // иначе случайно оставленная галка закрывала бы местность поверх боя.
        bool editor = Engine.IsEditorHint();

        _material.SetShaderParameter("show_noise", editor && Settings.ShowNoise);
        _material.SetShaderParameter("show_temperature", editor && Settings.ShowTemperature);
        _material.SetShaderParameter("show_height", editor && Settings.ShowHeight);
        _material.SetShaderParameter("hex_tiling", Settings.HexTiling);
        _material.SetShaderParameter("sharpness", Mathf.Max(Settings.Sharpness, 1f));
        _material.SetShaderParameter("ambient", Settings.Ambient);

        // Выключенные покрытия отбираются до передачи шейдеру, а не помечаются флагом в
        // нём: иначе выключенное занимало бы одно из четырёх мест, которые шейдер
        // разбирает развёрнутым кодом, и освободить место под примерку было бы нечем
        int count = 0;

        foreach (var candidate in Settings.Tiles ?? System.Array.Empty<SurfaceTile>())
        {
            if (candidate == null || !candidate.Enabled)
                continue;

            _active[count++] = candidate;

            if (count == SurfaceSettings.MaxTiles)
                break;
        }

        _material.SetShaderParameter("tile_count", count);

        for (int i = 0; i < SurfaceSettings.MaxTiles; i++)
        {
            var tile = i < count ? _active[i] : null;

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
                Multimesh = mesh,
                Texture = group.Decal.Texture,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };

            // Принадлежность группе — единственная примета отпечатка. Имя ею быть не может:
            // снятые узлы живут до конца кадра, и новый узел с тем же именем движок
            // переименовывает в служебное «@MultiMeshInstance2D@N». Такой узел переставал
            // опознаваться при следующей очистке и оставался на холсте навсегда, отчего
            // переключение показа копило неубираемые отпечатки. Имя здесь поэтому не
            // назначается вовсе: движок даёт заведомо неповторяющееся.
            node.AddToGroup(DecalGroup);

            // Owner не назначается намеренно: узлы собраны по описанию местности и
            // сохраняться в сцену не должны
            AddChild(node);
        }
    }

    /// <summary>
    /// Сколько узлов отпечатков сейчас в дереве. Снятые узлы не считаются: они живут до
    /// конца кадра, и без этой проверки сверка с раскладкой давала бы ложное расхождение.
    /// </summary>
    private int DecalNodes()
    {
        int count = 0;

        foreach (var child in GetChildren())
        {
            if (child is MultiMeshInstance2D node && !node.IsQueuedForDeletion() &&
                node.IsInGroup(DecalGroup))
                count++;
        }

        return count;
    }

    private void ClearDecals()
    {
        // Только QueueFree, без RemoveChild. Detach зовётся из EditorPreSave, когда
        // редактор уже блокирует дерево ради сохранения: синхронный RemoveChild тогда
        // отвергается с «Parent node is busy adding/removing children», а QueueFree
        // снимает узел в конце кадра. Owner у отпечатков не назначен, поэтому в файл
        // сцены они и так не попадают.
        foreach (var child in GetChildren())
        {
            if (child is not MultiMeshInstance2D node || !node.IsInGroup(DecalGroup))
                continue;

            // Из группы узел снимается сразу, а освобождается в конце кадра: пока он жив,
            // сверка дерева с раскладкой не должна его считать
            node.RemoveFromGroup(DecalGroup);
            node.QueueFree();
        }
    }

    private void Clear()
    {
        ClearDecals();
        Plan = null;
        _planSignature = 0;
    }

    // ── Вещество ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Завести вещество, если его нет. Оно создаётся всякий раз заново и в сцене не
    /// хранится: сохранять там нечего, а вред от сохранения описан у
    /// <see cref="_Notification"/>.
    /// </summary>
    private void Attach()
    {
        if (_material == null)
        {
            _material = Material as ShaderMaterial ?? new ShaderMaterial();
            Material = _material;
        }

        _material.Shader ??= GD.Load<Shader>(ShaderPath);
    }

    /// <summary>Снять вещество и поля: после этого узлу нечего отдать сохранению сцены.</summary>
    private void Detach()
    {
        Material = null;
        _material = null;

        Clear();
        Fields = null;
        _fieldsSignature = 0;
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
