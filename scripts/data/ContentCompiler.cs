using System.Collections.Generic;
using Godot;

/// <summary>Вид инструмента. Определяет, какие ключи у него читаются.</summary>
public enum ToolKind
{
    Weapon,
    Work,
}

/// <summary>
/// Сборка содержимого игры из .toml в готовые определения.
///
/// ЧТО ЗДЕСЬ ПРОИСХОДИТ, ПОМИМО ЧТЕНИЯ. Сборка — не просто разбор файлов подряд, у неё
/// четыре обязанности, и ни одна не сводится к чтению одного файла:
///
/// 1. РАЗВЁРТЫВАНИЕ НАСЛЕДОВАНИЯ. Определение с ключом extends берёт запасные значения
///    не из констант кода, а из указанного предка. Так виды противника перестают повторять
///    одни и те же числа, как это сделано в PA через base_spec.
/// 2. РАЗРЕШЕНИЕ ССЫЛОК. В файле лежат строки: tools = ["commander_gun"], tags = ["mobile"].
///    Здесь они однократно превращаются в объекты и маски, и дальше игра работает
///    со ссылками, а не ищет по словарю каждый кадр.
/// 3. ПРОВЕРКА СВЯЗНОСТИ. Ячейка панели, ссылающаяся на несуществующую постройку,
///    раньше обнаруживалась при попытке нарисовать кнопку — то есть могла не обнаружиться
///    вовсе. Теперь это ошибка на первом кадре, с именем файла и секции.
/// 4. ВЫВОД СЛЕДСТВИЙ. Род при выделении, ствол и рабочие инструменты вычисляются один раз
///    и ложатся полями определения, чтобы игра не перебирала список инструментов в цикле.
///
/// ПОЧЕМУ СБОРКА В ПАМЯТИ, А НЕ ГЕНЕРАЦИЯ ФАЙЛОВ. Генерация дала бы два представления одних
/// и тех же данных, и на вопрос, какое из них главное, пришлось бы отвечать соглашением,
/// которое рано или поздно нарушат. Файлов десятки, чтение занимает миллисекунды —
/// экономить тут нечего.
/// </summary>
public static class ContentCompiler
{
    private const string TagsPath = "res://resources/content/tags.toml";
    private const string ToolsDir = "res://resources/tools/";
    private const string BuildbarsDir = "res://resources/buildbars/";
    private const string WavesDir = "res://resources/waves/";

    /// <summary>Каталоги с определениями сущностей. Вид задаётся ключом class, а не папкой.</summary>
    private static readonly string[] UnitDirs =
    {
        "res://resources/units/",
        "res://resources/units/enemies/",
        "res://resources/buildings/",
    };

    /// <summary>Собрать содержимое. Ошибки печатаются в журнал, итог — сколько их было.</summary>
    public static int Compile(Catalog catalog)
    {
        int errors = 0;

        errors += LoadTags(catalog.Tags);
        errors += LoadTools(catalog);
        errors += LoadUnits(catalog);
        errors += LoadBuildbars(catalog);
        errors += LoadWaves(catalog);
        errors += Link(catalog);

        return errors;
    }

    // ── Теги ──────────────────────────────────────────────────────────────────────

    private static int LoadTags(TagRegistry registry)
    {
        var document = TomlDocument.Load(TagsPath);

        if (document == null)
            return 1;

        string[] names = document.Strings("tags");
        document.Done();

        if (document.Failed)
            return document.Errors;

        return registry.Declare(names, TagsPath) ? 0 : 1;
    }

    // ── Инструменты ───────────────────────────────────────────────────────────────

    private static int LoadTools(Catalog catalog)
    {
        int errors = 0;

        foreach (string path in Files(ToolsDir))
        {
            var document = TomlDocument.Load(path);

            if (document == null)
            {
                errors++;
                continue;
            }

            var tool = ReadTool(document);
            document.Done();
            errors += document.Errors;

            if (document.Failed || string.IsNullOrEmpty(tool?.Id))
                continue;

            if (!catalog.AddTool(tool))
            {
                GD.PushError($"[Контент] инструмент «{tool.Id}» объявлен дважды: {path}");
                errors++;
            }
        }

        return errors;
    }

    private static ToolDefinition ReadTool(TomlDocument document)
    {
        string id = document.RequiredString("id");
        string name = document.String("name");
        var kind = document.Enum("kind", ToolKind.Work);
        float range = document.Float("range", 3f);

        if (kind == ToolKind.Weapon)
            return new WeaponDefinition
            {
                Id = id,
                DisplayName = name,
                Range = range,
                Damage = document.Float("damage", 10f),
                FireInterval = document.Float("fire_interval", 1f),
                ProjectileSpeed = document.Float("projectile_speed", 14f),
                SpreadDegrees = document.Float("spread_degrees", 1.5f),
                AimConeDegrees = document.Float("aim_cone_degrees", 8f),
                ProjectileRadius = document.Float("projectile_radius", 0.08f),
                ProjectileColor = document.Color("projectile_color", new Color(1f, 0.85f, 0.4f)),
            };

        return new WorkToolDefinition
        {
            Id = id,
            DisplayName = name,
            Range = range,
            Power = document.Float("power", 1f),
            EnergyPerPower = document.Float("energy_per_power", 5f),
            Kinds = ReadWorkKinds(document),
            RepairsUnits = document.Bool("repairs_units"),
        };
    }

    private static WorkKinds ReadWorkKinds(TomlDocument document)
    {
        var kinds = WorkKinds.None;

        foreach (string name in document.Strings("works"))
            switch (name)
            {
                case "build":
                    kinds |= WorkKinds.Build;
                    break;

                case "repair":
                    kinds |= WorkKinds.Repair;
                    break;

                default:
                    document.Error($"неизвестный вид работы «{name}». Допустимые: build, repair");
                    break;
            }

        if (kinds == WorkKinds.None)
            document.Error("рабочий инструмент ничего не умеет: заполните works");

        return kinds;
    }

    // ── Определения сущностей ─────────────────────────────────────────────────────

    /// <summary>Разобранный, но ещё не связанный файл определения.</summary>
    private sealed class Source
    {
        public string Id;
        public string Extends;
        public bool Abstract;
        public TomlDocument Document;
        public string Path;
    }

    private static int LoadUnits(Catalog catalog)
    {
        var sources = new Dictionary<string, Source>();
        int errors = 0;

        foreach (string dir in UnitDirs)
        foreach (string path in Files(dir))
        {
            var document = TomlDocument.Load(path);

            if (document == null)
            {
                errors++;
                continue;
            }

            var source = new Source
            {
                Id = document.RequiredString("id"),
                Extends = document.String("extends"),
                Abstract = document.Bool("abstract"),
                Document = document,
                Path = path,
            };

            if (string.IsNullOrEmpty(source.Id))
            {
                errors += document.Errors;
                continue;
            }

            if (!sources.TryAdd(source.Id, source))
            {
                GD.PushError($"[Контент] определение «{source.Id}» объявлено дважды: {path}");
                errors++;
            }
        }

        return errors + Resolve(catalog, sources);
    }

    /// <summary>
    /// Разобрать в порядке наследования: предок раньше потомка. Порядок файлов в каталоге
    /// произволен, поэтому идём проходами, пока хоть что-то разрешается. Остаток —
    /// это либо ссылка в никуда, либо кольцо, и то и другое надо назвать вслух.
    /// </summary>
    private static int Resolve(Catalog catalog, Dictionary<string, Source> sources)
    {
        var ready = new Dictionary<string, UnitDefinition>();
        var pending = new List<Source>(sources.Values);
        int errors = 0;

        while (pending.Count > 0)
        {
            int before = pending.Count;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var source = pending[i];
                UnitDefinition parent = null;

                if (!string.IsNullOrEmpty(source.Extends))
                {
                    if (!ready.TryGetValue(source.Extends, out parent))
                        continue;
                }

                var definition = ReadUnit(source, parent, catalog.Tags);
                source.Document.Done();
                errors += source.Document.Errors;

                ready[source.Id] = definition;
                pending.RemoveAt(i);

                if (!source.Abstract && !source.Document.Failed)
                    catalog.AddUnit(definition);
            }

            if (pending.Count == before)
                break;
        }

        foreach (var source in pending)
        {
            GD.PushError(sources.ContainsKey(source.Extends)
                ? $"[Контент] кольцо наследования вокруг «{source.Id}»: {source.Path}"
                : $"[Контент] «{source.Id}» наследует несуществующее «{source.Extends}»: {source.Path}");

            errors++;
        }

        return errors;
    }

    /// <summary>
    /// Прочитать определение. Запасные значения берутся у предка, а если предка нет —
    /// из чистого определения с его умолчаниями. Поэтому наследование не требует
    /// отдельного кода слияния: незаполненный ключ просто остаётся предковым.
    /// </summary>
    private static UnitDefinition ReadUnit(Source source, UnitDefinition parent, TagRegistry tags)
    {
        var document = source.Document;
        var basis = parent ?? new UnitDefinition();

        var definition = new UnitDefinition
        {
            Id = source.Id,
            DisplayName = document.String("name", basis.DisplayName),
            Class = document.Enum("class", basis.Class),
            Color = document.Color("color", basis.Color),
            Buildbar = document.String("buildbar", basis.Buildbar),
            Tags = basis.Tags,
            ToolIds = document.Has("tools") ? document.Strings("tools") : basis.ToolIds,
            Assembly = basis.Assembly,
            Conversion = basis.Conversion,
            Rows = basis.Rows,
            FacingDegrees = basis.FacingDegrees,
            RequiresMetalSpot = basis.RequiresMetalSpot,
            MaxHealth = basis.MaxHealth,
            Radius = basis.Radius,
            VisionRange = basis.VisionRange,
            Speed = basis.Speed,
            TurnSpeedDegrees = basis.TurnSpeedDegrees,
            Acceleration = basis.Acceleration,
            Brake = basis.Brake,
            EnergyProduction = basis.EnergyProduction,
            MetalProduction = basis.MetalProduction,
            MetalStorage = basis.MetalStorage,
            EnergyStorage = basis.EnergyStorage,
            StandoffFraction = basis.StandoffFraction,
            ExpansionWeight = basis.ExpansionWeight,
            ArmyPowerWeight = basis.ArmyPowerWeight,
            Hull = basis.Hull,
            HullAspect = basis.HullAspect,
            ArmorRings = basis.ArmorRings,
            FrontPlate = basis.FrontPlate,
            Plant = basis.Plant,
        };

        if (document.Has("tags"))
        {
            string[] names = document.Strings("tags");

            if (!tags.TryParse(names, $"«{source.Id}» ({source.Path})", out var parsed))
                document.Error("набор тегов не собран");

            definition.Tags = parsed;
        }

        if (document.Section("body") is { } body)
        {
            definition.MaxHealth = body.Float("max_health", basis.MaxHealth);
            definition.Radius = body.Float("radius", basis.Radius);
            definition.VisionRange = body.Float("vision_range", basis.VisionRange);
            definition.Hull = body.Enum("hull", basis.Hull);
            definition.HullAspect = body.Float("hull_aspect", basis.HullAspect);
            definition.ArmorRings = body.Int("armor_rings", basis.ArmorRings);
            definition.FrontPlate = body.Bool("front_plate", basis.FrontPlate);
        }

        if (document.Section("movement") is { } movement)
        {
            definition.Speed = movement.Float("speed", basis.Speed);
            definition.TurnSpeedDegrees = movement.Float("turn_speed", basis.TurnSpeedDegrees);
            definition.Acceleration = movement.Float("acceleration", basis.Acceleration);
            definition.Brake = movement.Float("brake", basis.Brake);
        }

        if (document.Section("footprint") is { } footprint)
        {
            definition.Rows = footprint.Strings("rows");
            definition.FacingDegrees = footprint.Float("facing_degrees", basis.FacingDegrees);
            definition.RequiresMetalSpot =
                footprint.Bool("requires_metal_spot", basis.RequiresMetalSpot);

            definition.Pattern = footprint.Enum("pattern", basis.Pattern);
            definition.PatternAlt = footprint.Enum("pattern_alt", basis.PatternAlt);

            // Ступени названы по тому, что получается на месте: narrow — сплошной ряд,
            // margin — ряд с проходом. Между ними и дальше значения задаются числом
            definition.PatternStep = footprint.Scale("pattern_step", basis.PatternStep,
                ("narrow", 1f), ("margin", 2f));

            if (definition.Rows.Length == 0)
                footprint.Error("форма пуста: задайте rows или уберите секцию");
        }

        if (document.Section("assembly") is { } assembly)
            definition.Assembly = new AssemblyDefinition
            {
                CostMetal = assembly.Float("cost_metal", basis.Assembly?.CostMetal ?? 0f),
                FrameHealth = assembly.Float("frame_health", basis.Assembly?.FrameHealth ?? 0f),
            };

        // Каркас той же прочности, что и готовая сущность, — случай обычный, и повторять
        // это число в каждом файле постройки незачем
        if (definition.Assembly is { FrameHealth: <= 0f })
            definition.Assembly.FrameHealth = definition.MaxHealth;

        if (document.Section("conversion") is { } conversion)
            definition.Conversion = new ConversionDefinition
            {
                EnergyDrain = conversion.Float("energy_drain", basis.Conversion?.EnergyDrain ?? 0f),
                MetalOutput = conversion.Float("metal_output", basis.Conversion?.MetalOutput ?? 0f),
            };

        if (document.Section("production") is { } production)
        {
            definition.EnergyProduction = production.Float("energy", basis.EnergyProduction);
            definition.MetalProduction = production.Float("metal", basis.MetalProduction);
        }

        if (document.Section("storage") is { } storage)
        {
            definition.MetalStorage = storage.Float("metal", basis.MetalStorage);
            definition.EnergyStorage = storage.Float("energy", basis.EnergyStorage);
        }

        // Незаданный вес и вес, заданный нулём, — разные вещи: первый означает «возьми
        // умолчание по роду», второй — «эта сущность в показатель не входит». Поэтому
        // ключи читаются только при наличии, а не через Float с запасным значением
        if (document.Section("terror") is { } terror)
        {
            if (terror.Has("expansion"))
                definition.ExpansionWeight = terror.Float("expansion");

            if (terror.Has("army_power"))
                definition.ArmyPowerWeight = terror.Float("army_power");
        }

        // Раздел боя описывает то, как сущность ведёт себя в схватке. Раньше доля подхода
        // лежала в разделе spawn вместе с весом появления, хотя к появлению отношения
        // не имеет: определение обязано описывать сам юнит, а не то, как его выставляют
        if (document.Section("battle") is { } battle)
            definition.StandoffFraction = battle.Float("standoff", basis.StandoffFraction);

        if (document.Section("plant") is { } plant)
        {
            var prior = basis.Plant ?? new PlantDefinition();
            var dirs = plant.Has("rolloff_directions")
                ? plant.Vector2List("rolloff_directions")
                : prior.RolloffDirections;

            if (dirs.Length == 0)
                dirs = new[] { Vector2.Right, Vector2.Left };

            definition.Plant = new PlantDefinition
            {
                FactoryCooldown = plant.Float("factory_cooldown", prior.FactoryCooldown),
                BuildPower = plant.Float("build_power", prior.BuildPower),
                EnergyPerPower = plant.Float("energy_per_power", prior.EnergyPerPower),
                RolloffDirections = dirs,
                RolloffClearance = plant.Float("rolloff_clearance", prior.RolloffClearance),
            };
        }

        return definition;
    }

    // ── Строительные панели ───────────────────────────────────────────────────────

    private static int LoadBuildbars(Catalog catalog)
    {
        int errors = 0;

        foreach (string path in Files(BuildbarsDir))
        {
            var document = TomlDocument.Load(path);

            if (document == null)
            {
                errors++;
                continue;
            }

            var bar = ReadBuildbar(document);
            document.Done();
            errors += document.Errors;

            if (document.Failed || string.IsNullOrEmpty(bar.Id))
                continue;

            if (!catalog.AddBuildbar(bar))
            {
                GD.PushError($"[Контент] панель «{bar.Id}» объявлена дважды: {path}");
                errors++;
            }
        }

        return errors;
    }

    private static BuildbarDefinition ReadBuildbar(TomlDocument document)
    {
        var bar = new BuildbarDefinition
        {
            Id = document.RequiredString("id"),
            Priority = document.Int("priority", 10),
            Kind = document.Enum("kind", BuildbarKind.Placement),
        };

        var sections = document.Sections("sections");

        if (sections.Count == 0)
            document.Error("нет ни одной секции");

        foreach (var source in sections)
        {
            var section = new BuildbarSectionDefinition
            {
                Id = source.RequiredString("id"),
                Title = source.String("title"),
                Order = source.Int("order"),
            };

            foreach (var cell in source.Sections("cells"))
            {
                string buildableId = cell.RequiredString("build");
                var position = new Vector2I(cell.Int("x"), cell.Int("y"));

                if (position.X < 0 || position.Y < 0)
                {
                    cell.Error($"отрицательная координата у «{buildableId}»");
                    continue;
                }

                section.Cells.Add(new BuildbarCell(position, buildableId));
            }

            bar.Sections.Add(section);
        }

        return bar;
    }

    // ── Волны ─────────────────────────────────────────────────────────────────────

    private static int LoadWaves(Catalog catalog)
    {
        int errors = 0;

        foreach (string path in Files(WavesDir))
        {
            var document = TomlDocument.Load(path);

            if (document == null)
            {
                errors++;
                continue;
            }

            var wave = ReadWave(document);
            document.Done();
            errors += document.Errors;

            if (document.Failed || string.IsNullOrEmpty(wave.Id))
                continue;

            if (!catalog.AddWave(wave))
            {
                GD.PushError($"[Контент] волна «{wave.Id}» объявлена дважды: {path}");
                errors++;
            }
        }

        return errors;
    }

    private static WaveDefinition ReadWave(TomlDocument document)
    {
        var wave = new WaveDefinition
        {
            Id = document.RequiredString("id"),
            DisplayName = document.String("name"),
            Tags = document.Strings("tags"),
            PreferNext = document.Strings("prefer_next"),
            ArmyPowerBudget = document.Float("army_power_budget", 10f),
            ArmyPowerPerTerror = document.Float("army_power_per_terror"),
            ChillIntervalMultiplier = document.Float("chill_interval_multiplier", 1f),
        };

        if (document.Has("chill_interval_offset"))
            wave.ChillIntervalOffset = document.Float("chill_interval_offset");

        ReadTerrorRange(document, wave);

        foreach (var source in document.Sections("unit_list"))
        {
            var list = new WaveUnitList
            {
                Mode = source.Enum("mode", UnitListMode.Allow),
                UnitIds = source.Strings("units"),
                TargetBudgetShare = source.Float("target_budget_share"),
            };

            if (list.UnitIds.Length == 0)
                source.Error("список видов пуст: заполните units или уберите секцию");

            if (list.TargetBudgetShare is < 0f or > 1f)
                source.Error($"доля бюджета «{list.TargetBudgetShare}» вне пределов от 0 до 1");

            if (list.Mode == UnitListMode.Limit && list.TargetBudgetShare <= 0f)
                source.Error("режим limit без target_budget_share ничего не делает: " +
                             "на отбор он не влияет, а доли у него нет");

            wave.UnitLists.Add(list);
        }

        if (document.Section("spawn") is { } spawn)
        {
            var over = wave.Shape;

            if (spawn.Has("near_arc_degrees"))
                over.NearArcDegrees = spawn.Float("near_arc_degrees");

            if (spawn.Has("far_arc_degrees"))
                over.FarArcDegrees = spawn.Float("far_arc_degrees");

            if (spawn.Has("radius_offset_multiplier"))
                over.RadiusOffsetMultiplier = spawn.Float("radius_offset_multiplier");

            if (spawn.Has("radius_depth_multiplier"))
                over.RadiusDepthMultiplier = spawn.Float("radius_depth_multiplier");

            if (spawn.Has("spacing_cells"))
                over.SpacingCells = spawn.Float("spacing_cells");

            if (spawn.Has("groups"))
                over.Groups = spawn.Int("groups");

            if (spawn.Has("groups_arc_degrees"))
                over.GroupsArcDegrees = spawn.Float("groups_arc_degrees");
        }

        return wave;
    }

    /// <summary>
    /// Границы применимости: terror_range = [min, max]. Отрицательное значение в любой
    /// позиции означает «безразлично», и это соглашение выбрано вместо отсутствующего ключа
    /// затем, что оно читается в файле явно: [20.0, -1] прямо говорит, что верхней границы
    /// нет, тогда как неполный список требовал бы помнить, какая из двух границ пропущена.
    /// </summary>
    private static void ReadTerrorRange(TomlDocument document, WaveDefinition wave)
    {
        if (!document.Has("terror_range"))
            return;

        float[] range = document.Floats("terror_range");

        if (range.Length != 2)
        {
            document.Error($"terror_range должен быть списком из двух чисел, а не из {range.Length}. " +
                           "Отрицательное значение означает «безразлично»");
            return;
        }

        wave.TerrorMin = range[0];
        wave.TerrorMax = range[1];

        if (wave.TerrorMin >= 0f && wave.TerrorMax >= 0f && wave.TerrorMin > wave.TerrorMax)
            document.Error($"terror_range задан наоборот: нижняя граница {wave.TerrorMin} " +
                           $"больше верхней {wave.TerrorMax}, волна не подойдёт никогда");
    }

    // ── Связывание и проверка ─────────────────────────────────────────────────────

    /// <summary>
    /// Превратить строковые ссылки в объекты и убедиться, что всё сходится. Именно здесь
    /// ловится ячейка панели, ссылающаяся в пустоту, и юнит, которому назначена
    /// несуществующая панель.
    /// </summary>
    private static int Link(Catalog catalog)
    {
        int errors = 0;

        foreach (var definition in catalog.Units)
        {
            errors += LinkTools(catalog, definition);

            definition.SelectionGroup = definition.Tags.Has(catalog.Tags.Structure)
                ? SelectionGroup.Structures
                : SelectionGroup.Bots;

            if (definition.Buildbar.Length > 0 && catalog.Buildbar(definition.Buildbar) == null)
            {
                GD.PushError($"[Контент] «{definition.Id}» ссылается на несуществующую " +
                             $"панель «{definition.Buildbar}»");
                errors++;
            }

            if (definition.Class == UnitClass.Plant && definition.Plant == null)
            {
                GD.PushError($"[Контент] «{definition.Id}»: class = plant требует секцию [plant]");
                errors++;
            }
        }

        errors += LinkWaves(catalog);

        foreach (var bar in catalog.Buildbars)
        foreach (var section in bar.Sections)
        foreach (var cell in section.Cells)
        {
            var target = catalog.Unit(cell.BuildableId);

            if (target == null)
            {
                GD.PushError($"[Контент] панель «{bar.Id}», секция «{section.Id}»: " +
                             $"нет определения «{cell.BuildableId}»");
                errors++;
                continue;
            }

            if (target.Assembly == null)
            {
                GD.PushError($"[Контент] панель «{bar.Id}», секция «{section.Id}»: " +
                             $"«{cell.BuildableId}» не строится — нет секции [assembly]");
                errors++;
            }
        }

        return errors;
    }

    /// <summary>
    /// Превратить ссылки волн в объекты и убедиться, что набор состава вообще возможен.
    /// Ошибка здесь стоит дёшево, а не найденная здесь обнаружилась бы через полчаса игры,
    /// когда волна впервые выпадет при отборе, — и выглядела бы как пустая волна без причины.
    /// </summary>
    private static int LinkWaves(Catalog catalog)
    {
        int errors = 0;

        // Теги волн живут строками и своего реестра не имеют: tags.toml описывает сущности
        // мира, а не события давления. Защиту от опечатки даёт этот перечень — тег, которого
        // нет ни у одной волны, никому перевеса не даст, и молчать об этом нельзя
        var declared = new HashSet<string>();

        foreach (var wave in catalog.Waves)
        foreach (string tag in wave.Tags)
            declared.Add(tag);

        foreach (var wave in catalog.Waves)
        {
            foreach (string tag in wave.PreferNext)
                if (!declared.Contains(tag))
                {
                    GD.PushError($"[Контент] волна «{wave.Id}»: тега «{tag}» нет ни у одной " +
                                 "волны, перевес не сработает. Проверьте написание");
                    errors++;
                }

            bool narrowed = false;

            foreach (var list in wave.UnitLists)
            {
                errors += LinkWaveUnits(catalog, wave, list);
                narrowed |= list.Mode == UnitListMode.Allow;
            }

            errors += CheckWaveLimits(wave, narrowed);
        }

        return errors;
    }

    private static int LinkWaveUnits(Catalog catalog, WaveDefinition wave, WaveUnitList list)
    {
        int errors = 0;

        foreach (string id in list.UnitIds)
        {
            var definition = catalog.Unit(id);

            if (definition == null)
            {
                GD.PushError($"[Контент] волна «{wave.Id}»: нет определения «{id}»");
                errors++;
                continue;
            }

            if (definition.Class != UnitClass.Enemy)
            {
                GD.PushError($"[Контент] волна «{wave.Id}»: «{id}» не относится к противнику " +
                             "и выставлен волной быть не может");
                errors++;
                continue;
            }

            // Нулевая мощь означает «в бюджете не участвует»: такой вид занимал бы нулевое
            // место и потому набирался бы бесконечно
            if (definition.ArmyPower <= 0f)
            {
                GD.PushError($"[Контент] волна «{wave.Id}»: у «{id}» нулевая боевая мощь, " +
                             "набрать его в бюджет нельзя");
                errors++;
                continue;
            }

            list.Units.Add(definition);
        }

        return errors;
    }

    /// <summary>
    /// Целевая доля имеет смысл лишь тогда, когда виды списка вообще допустимы. Список
    /// limit, чьи виды отсечены сужением через allow, — это доля, которая не сработает
    /// никогда, то есть настройка, молчаливо не имеющая последствий.
    /// </summary>
    private static int CheckWaveLimits(WaveDefinition wave, bool narrowed)
    {
        if (!narrowed)
            return 0;

        var allowed = new HashSet<string>();

        foreach (var list in wave.UnitLists)
            if (list.Mode == UnitListMode.Allow)
                foreach (var definition in list.Units)
                    allowed.Add(definition.Id);

        foreach (var list in wave.UnitLists)
            if (list.Mode == UnitListMode.Deny)
                foreach (var definition in list.Units)
                    allowed.Remove(definition.Id);

        int errors = 0;

        foreach (var list in wave.UnitLists)
        {
            if (list.Mode != UnitListMode.Limit)
                continue;

            foreach (var definition in list.Units)
                if (!allowed.Contains(definition.Id))
                {
                    GD.PushError($"[Контент] волна «{wave.Id}»: доля отведена виду «{definition.Id}», " +
                                 "которого нет среди допустимых. Добавьте его в список allow " +
                                 "либо уберите из limit");
                    errors++;
                }
        }

        return errors;
    }

    private static int LinkTools(Catalog catalog, UnitDefinition definition)
    {
        int errors = 0;

        // Список инструментов лежит на определении строками, пока их не разрешит эта строка.
        // Дальше игра работает готовыми ссылками и по словарю не ходит
        var resolved = new List<ToolDefinition>();

        foreach (string id in definition.ToolIds)
        {
            var tool = catalog.Tool(id);

            if (tool == null)
            {
                GD.PushError($"[Контент] «{definition.Id}»: нет инструмента «{id}»");
                errors++;
                continue;
            }

            resolved.Add(tool);
        }

        definition.Tools = resolved.ToArray();

        foreach (var tool in definition.Tools)
            switch (tool)
            {
                case WeaponDefinition weapon when definition.Weapon == null:
                    definition.Weapon = weapon;
                    break;

                case WeaponDefinition:
                    GD.PushError($"[Контент] «{definition.Id}»: второй ствол «{tool.Id}». " +
                                 "Носить можно один — система стрельбы знает про одну перезарядку");
                    errors++;
                    break;

                // Рука годится и строителю, и ремонтнику: перечислять оба умения незачем,
                // потому что CanRepair истинно и у чистого строителя
                case WorkToolDefinition work:
                    if (work.CanWork)
                        definition.BuildTool ??= work;

                    break;
            }

        return errors;
    }

    // ── Общее ─────────────────────────────────────────────────────────────────────

    /// <summary>Пути ко всем .toml каталога. Соседние .md в выборку не попадают.</summary>
    private static IEnumerable<string> Files(string dir)
    {
        using var access = DirAccess.Open(dir);

        if (access == null)
        {
            GD.PushWarning($"[Контент] каталог не найден: {dir}");
            yield break;
        }

        foreach (string file in access.GetFiles())
            if (file.EndsWith(".toml"))
                yield return dir + file;
    }
}
