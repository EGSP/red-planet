using System;
using System.Collections.Generic;

/// <summary>
/// Тип значения поля в форме редактора контента.
/// </summary>
public enum ContentFieldType
{
    String,
    RequiredString,
    Float,
    Int,
    Bool,
    Color,
    Enum,
    StringList,
    FloatList,
    Vector2List,
    Scale,
    Path,
    NullableFloat,
}

/// <summary>
/// Вид документа, который редактор умеет открывать.
/// </summary>
public enum ContentEntityKind
{
    Unit,
    Building,
    Weapon,
    WorkTool,
    Wave,
}

/// <summary>
/// Описание одного ключа TOML для формы редактора.
/// </summary>
public sealed class ContentFieldSpec
{
    public string Section;
    public string Key;
    public string Label;
    public ContentFieldType Type;
    public Type EnumType;
    public (string Name, float Value)[] ScaleSteps;
    public string[] AllowedStrings;
    public Func<UnitDefinition, bool> VisibleForUnit;
    public Func<ToolDefinition, bool> VisibleForTool;
    public bool RootOnly;

    /// <summary>
    /// Маски файлов для выбора ресурса в поле типа <see cref="ContentFieldType.Path"/>,
    /// вида <c>"*.tscn ; Scenes"</c>. Пусто — диалог показывает все файлы.
    /// </summary>
    public string[] PathFilters;

    /// <summary>
    /// Пояснение к смыслу ключа. Показывается подсказкой у названия поля: соглашения
    /// вроде «-1 означает отсутствие границы» иначе приходится держать в памяти.
    /// </summary>
    public string Hint;
}

/// <summary>
/// Декларативная схема полей юнитов, построек и инструментов.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ ContentCompiler. Компилятор знает, как ЧИТАТЬ ключи в определения.
/// Форма должна знать, какие ключи ПОКАЗАТЬ, как их подписать и каким виджетом править.
/// Дублировать перечень в UI-коде означало бы, что новый ключ в компиляторе забывают
/// добавить в форму. Схема — единый список для формы; чтение по-прежнему в компиляторе.
///
/// VisibleForUnit скрывает секции вроде [plant], когда у сущности нет завода: иначе
/// форма предлагала бы поля, которых в файле не должно появиться без смены class.
/// </summary>
public static class ContentSchema
{
    public static IReadOnlyList<ContentFieldSpec> UnitFields { get; } = BuildUnitFields();

    public static IReadOnlyList<ContentFieldSpec> WeaponFields { get; } = BuildWeaponFields();

    public static IReadOnlyList<ContentFieldSpec> WorkToolFields { get; } = BuildWorkToolFields();

    public static IReadOnlyList<ContentFieldSpec> WaveFields { get; } = BuildWaveFields();

    /// <summary>
    /// Поля одного блока [[unit_list]]. Список отдельный, поскольку блоков в файле
    /// несколько и обращение к ним идёт по номеру, а не по имени секции.
    /// </summary>
    public static IReadOnlyList<ContentFieldSpec> WaveUnitListFields { get; } =
        BuildWaveUnitListFields();

    public static IReadOnlyList<ContentFieldSpec> FieldsFor(ContentEntityKind kind) => kind switch
    {
        ContentEntityKind.Weapon => WeaponFields,
        ContentEntityKind.WorkTool => WorkToolFields,
        ContentEntityKind.Wave => WaveFields,
        _ => UnitFields,
    };

    private static List<ContentFieldSpec> BuildUnitFields()
    {
        var fields = new List<ContentFieldSpec>
        {
            Root("id", "Id", ContentFieldType.RequiredString),
            Root("name", "Name", ContentFieldType.String),
            // class выбирает поведение Spawner, а не тир или отображаемый род.
            // Например, титан Ares наследует bot, а признак titan находится в tags.
            RootEnum("class", "Class", typeof(UnitClass)),
            Root("color", "Color", ContentFieldType.Color),
            Root("tags", "Tags", ContentFieldType.StringList),
            Root("tools", "Tools", ContentFieldType.StringList),
            Root("buildbar", "Buildbar", ContentFieldType.String),

            Body("max_health", "Health", ContentFieldType.Float),
            Body("radius", "Radius", ContentFieldType.Float),
            Body("vision_range", "Vision", ContentFieldType.Float),
            Path("body", "model", "Model scene", SceneFilters),

            Section("movement", "speed", "Speed", ContentFieldType.Float),
            Hint(Section("movement", "turn_speed", "Body turn speed", ContentFieldType.Float),
                "Degrees per second the HULL rotates. The tools turn at their own rate — "
                + "see aim_rate_degrees in the tool file. Together with speed it sets the "
                + "turning radius: speed divided by turn_speed."),
            Hint(Section("movement", "turn_free_angle", "Full speed up to",
                    ContentFieldType.Float),
                "Degrees. While the course differs from the hull axis by less than this, "
                + "the unit keeps full speed — bends of the path cost nothing. "
                + "Works as a pair with turn_stop_angle and must not exceed it."),
            Hint(Section("movement", "turn_stop_angle", "Stop from", ContentFieldType.Float),
                "Degrees. From this difference between course and hull axis the unit does "
                + "not move at all and turns on the spot. Between the two angles the speed "
                + "falls linearly. 180 disables stopping: such a unit can only change "
                + "direction along an arc, like a ship."),
            Section("movement", "acceleration", "Acceleration", ContentFieldType.Float),
            Section("movement", "brake", "Brake", ContentFieldType.Float),

            Section("footprint", "rows", "Shape", ContentFieldType.StringList),
            Section("footprint", "facing_degrees", "Facing", ContentFieldType.Float),
            Section("footprint", "requires_metal_spot", "Metal spot", ContentFieldType.Bool),
            SectionEnum("footprint", "pattern", "Pattern", typeof(BuildPattern)),
            SectionEnum("footprint", "pattern_alt", "Alt pattern", typeof(BuildPattern)),
            SectionScale("footprint", "pattern_step", "Pattern step",
                ("narrow", 1f), ("margin", 2f)),

            Section("assembly", "cost_metal", "Cost", ContentFieldType.Float),
            Section("assembly", "frame_health", "Frame health", ContentFieldType.Float),

            Section("conversion", "energy_drain", "Energy drain", ContentFieldType.Float),
            Section("conversion", "metal_output", "Metal output", ContentFieldType.Float),

            Section("production", "energy", "Energy production", ContentFieldType.Float),
            Section("production", "metal", "Metal production", ContentFieldType.Float),

            Section("storage", "metal", "Metal storage", ContentFieldType.Float),
            Section("storage", "energy", "Energy storage", ContentFieldType.Float),

            Section("terror", "expansion_power", "Expansion power", ContentFieldType.NullableFloat),
            Section("terror", "army_power", "Army power", ContentFieldType.NullableFloat),
            Section("terror", "ignore_modifiers", "Ignore zone modifiers", ContentFieldType.Bool),

            Section("battle", "approach_hold", "Approach hold", ContentFieldType.Float),
            Hint(SectionEnum("battle", "engagement", "Engagement", typeof(Engagement)),
                "How far the unit goes in a fight it started on its own, without an order. "
                + "Hold — it only takes targets it can reach without leaving the attention "
                + "radius around its anchor, so the line is kept. Pursue — no limit on how "
                + "far it follows, and it returns to the anchor once no targets are left; "
                + "this is what a short-ranged unit needs to reach anything at all. "
                + "A player's order ignores the mode and leads the unit any distance."),

            Section("plant", "factory_cooldown", "Factory cooldown", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "build_power", "Build power", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "energy_per_power", "Energy per power", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "rolloff_directions", "Rolloff directions", ContentFieldType.Vector2List,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "rolloff_clearance", "Rolloff clearance", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),

            Section("orders", "allow", "Allow orders", ContentFieldType.StringList),
            Section("orders", "deny", "Deny orders", ContentFieldType.StringList),
        };

        return fields;
    }

    private static List<ContentFieldSpec> BuildWeaponFields()
    {
        var fields = new List<ContentFieldSpec>
        {
            Root("id", "Id", ContentFieldType.RequiredString),
            Root("name", "Name", ContentFieldType.String),
            Root("kind", "Kind", ContentFieldType.String),
            Root("range", "Range", ContentFieldType.Float),
        };

        fields.AddRange(AimFields());

        fields.AddRange([
            Root("damage", "Damage", ContentFieldType.Float),
            Root("fire_interval", "Fire interval", ContentFieldType.Float),
            Root("projectile_speed", "Projectile speed", ContentFieldType.Float),
            Root("spread_degrees", "Spread", ContentFieldType.Float),
            Hint(Root("fire_arc_degrees", "Fire arc", ContentFieldType.Float),
                "Degrees to either side of the BARREL axis within which firing is allowed. "
                + "Do not confuse with aim_arc_degrees: that one says where the barrel may "
                + "turn and is measured from the hull, this one says how precisely it is "
                + "already aimed and is measured from the barrel. Usually single digits."),
            Root("projectile_radius", "Projectile radius", ContentFieldType.Float),
            Root("projectile_color", "Projectile color", ContentFieldType.Color),
        ]);

        return fields;
    }

    private static List<ContentFieldSpec> BuildWorkToolFields()
    {
        var fields = new List<ContentFieldSpec>
        {
            Root("id", "Id", ContentFieldType.RequiredString),
            Root("name", "Name", ContentFieldType.String),
            Root("kind", "Kind", ContentFieldType.String),
            Root("range", "Range", ContentFieldType.Float),
        };

        fields.AddRange(AimFields());

        fields.AddRange([
            Root("power", "Power", ContentFieldType.Float),
            Root("energy_per_power", "Energy per power", ContentFieldType.Float),
            Root("works", "Works", ContentFieldType.StringList),
            Root("repairs_units", "Repairs units", ContentFieldType.Bool),
        ]);

        return fields;
    }

    /// <summary>
    /// Ключи поворота, общие для ствола и рабочей руки: инструмент любого рода сидит
    /// на поворотной опоре и описывается одними и теми же числами. Список строится заново
    /// на каждый вызов — описания полей принадлежат своей форме и общими быть не должны.
    /// </summary>
    private static List<ContentFieldSpec> AimFields() =>
    [
        Hint(Root("aim_arc_degrees", "Aim arc", ContentFieldType.Float),
            "Degrees to either side of the HULL axis the tool may turn. 180 is a full "
            + "circle with no limit; 0 is a rigidly mounted tool that can only be aimed by "
            + "turning the carrier. Governs body_assist: with a full circle the hull is "
            + "never asked to turn, with 0 it is always asked."),
        Hint(Root("aim_rate_degrees", "Aim rate", ContentFieldType.Float),
            "Degrees per second the tool turns. Independent of the carrier's turn_speed: "
            + "a turret is usually nimbler than the chassis. Meaningless when "
            + "aim_arc_degrees is 0 — there is nothing to turn."),
        Hint(Root("aim_idle_delay", "Idle delay", ContentFieldType.Float),
            "Seconds the tool holds its last angle after the target is gone, before "
            + "returning to the hull axis. Guards against jitter when a rapid-fire weapon "
            + "switches targets. 0 returns immediately."),
        Hint(RootEnum("body_assist", "Body assist", typeof(BodyAssist)),
            "Whether the tool asks the hull to turn when the target is outside its "
            + "aim_arc_degrees. Never — a build arm with a full circle, so the hull stays "
            + "free for the gun. WhenBlocked — the default. Always — a rigidly mounted "
            + "weapon, which has no reserve of its own."),
        Hint(Root("aim_priority", "Aim priority", ContentFieldType.Int),
            "Whose demand for a hull turn wins when several tools cannot be satisfied at "
            + "once. Higher wins; equal is decided by order in the unit's tools list. "
            + "A fallback only: first the carrier looks for a hull angle that puts the "
            + "target inside every tool's arc at once."),
    ];

    private static List<ContentFieldSpec> BuildWaveFields() =>
    [
        Root("id", "Id", ContentFieldType.RequiredString),
        Root("name", "Name", ContentFieldType.String),
        Hint(Root("terror_range", "Terror range", ContentFieldType.FloatList),
            "Two numbers: lower and upper bound of applicability. "
            + "−1 in either position means the bound is absent."),
        Hint(Root("army_power_budget", "Army power budget", ContentFieldType.Float),
            "Size of the wave in army power units."),
        Hint(Root("army_power_per_terror", "Budget per terror", ContentFieldType.Float),
            "Addition to the budget for every point of smoothed terror. "
            + "Zero means the size does not depend on terror."),
        Hint(Root("chill_interval_multiplier", "Chill multiplier", ContentFieldType.Float),
            "Multiplier of the subsystem rest interval before the next wave."),
        Hint(Root("chill_interval_offset", "Chill offset", ContentFieldType.NullableFloat),
            "Share of random spread of the rest interval. Empty means the subsystem constant."),
        Root("tags", "Tags", ContentFieldType.StringList),
        Hint(Root("prefer_next", "Prefer next", ContentFieldType.StringList),
            "Tags given extra weight when the next wave is chosen."),

        Hint(Section("spawn", "near_arc_degrees", "Near arc", ContentFieldType.NullableFloat),
            "Width of the front edge of the formation, in degrees."),
        Section("spawn", "far_arc_degrees", "Far arc", ContentFieldType.NullableFloat),
        Section("spawn", "wave_start", "Wave start", ContentFieldType.NullableFloat),
        Section("spawn", "radius_depth_multiplier", "Depth multiplier", ContentFieldType.NullableFloat),
        Section("spawn", "spacing_cells", "Spacing", ContentFieldType.NullableFloat),
        Section("spawn", "groups", "Groups", ContentFieldType.Int),
        Section("spawn", "groups_arc_degrees", "Groups arc", ContentFieldType.NullableFloat),
        Hint(Section("spawn", "group_delay_seconds", "Group delay", ContentFieldType.NullableFloat),
            "Delay between the appearance of groups: the first is already visible, "
            + "the second is not yet."),
    ];

    private static List<ContentFieldSpec> BuildWaveUnitListFields() =>
    [
        Hint(RootEnum("mode", "Mode", typeof(UnitListMode)),
            "allow narrows what is admissible, deny subtracts from it, "
            + "limit only assigns a budget share."),
        Root("units", "Units", ContentFieldType.StringList),
        Hint(Root("target_budget_share", "Budget share", ContentFieldType.Float),
            "Target share of the wave budget for these unit kinds, from 0 to 1."),
    ];

    private static ContentFieldSpec Hint(ContentFieldSpec field, string hint)
    {
        field.Hint = hint;
        return field;
    }

    private static ContentFieldSpec Root(string key, string label, ContentFieldType type) =>
        new() { Key = key, Label = label, Type = type, RootOnly = true };

    private static ContentFieldSpec RootEnum(string key, string label, Type enumType) =>
        new() { Key = key, Label = label, Type = ContentFieldType.Enum, EnumType = enumType, RootOnly = true };

    private static ContentFieldSpec Body(string key, string label, ContentFieldType type) =>
        Section("body", key, label, type);

    /// <summary>Поле пути с масками для диалога выбора ресурса.</summary>
    private static ContentFieldSpec Path(
        string section, string key, string label, params string[] filters)
    {
        var spec = section == null
            ? Root(key, label, ContentFieldType.Path)
            : Section(section, key, label, ContentFieldType.Path);

        spec.PathFilters = filters;
        return spec;
    }

    /// <summary>Маска сцены модели.</summary>
    private static readonly string[] SceneFilters = { "*.tscn ; Сцены" };

    private static ContentFieldSpec Section(
        string section, string key, string label, ContentFieldType type,
        Func<UnitDefinition, bool> visible = null) =>
        new()
        {
            Section = section,
            Key = key,
            Label = label,
            Type = type,
            VisibleForUnit = visible,
        };

    private static ContentFieldSpec SectionEnum(
        string section, string key, string label, Type enumType) =>
        new()
        {
            Section = section,
            Key = key,
            Label = label,
            Type = ContentFieldType.Enum,
            EnumType = enumType,
        };

    private static ContentFieldSpec SectionScale(
        string section, string key, string label, params (string Name, float Value)[] steps) =>
        new()
        {
            Section = section,
            Key = key,
            Label = label,
            Type = ContentFieldType.Scale,
            ScaleSteps = steps,
        };
}
