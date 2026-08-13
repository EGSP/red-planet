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

    public static IReadOnlyList<ContentFieldSpec> FieldsFor(ContentEntityKind kind) => kind switch
    {
        ContentEntityKind.Weapon => WeaponFields,
        ContentEntityKind.WorkTool => WorkToolFields,
        _ => UnitFields,
    };

    private static List<ContentFieldSpec> BuildUnitFields()
    {
        var fields = new List<ContentFieldSpec>
        {
            Root("id", "Идентификатор", ContentFieldType.RequiredString),
            Root("name", "Имя", ContentFieldType.String),
            // class выбирает поведение Spawner, а не тир или отображаемый род.
            // Например, титан Ares наследует bot, а признак titan находится в tags.
            RootEnum("class", "Класс поведения", typeof(UnitClass)),
            Root("color", "Цвет", ContentFieldType.Color),
            Root("tags", "Теги", ContentFieldType.StringList),
            Root("tools", "Инструменты", ContentFieldType.StringList),
            Root("buildbar", "Панель", ContentFieldType.String),

            Body("max_health", "Прочность", ContentFieldType.Float),
            Body("radius", "Радиус", ContentFieldType.Float),
            Body("vision_range", "Обзор", ContentFieldType.Float),
            BodyEnum("hull", "Силуэт", typeof(HullShape)),
            BodyEnum("hull_trim", "Надстройка", typeof(HullTrim)),
            Body("hull_aspect", "Отношение сторон", ContentFieldType.Float),
            Body("sprite", "Спрайт", ContentFieldType.Path),
            Body("sprite_scale", "Масштаб спрайта", ContentFieldType.Float),
            Body("sprite_rotation", "Поворот спрайта", ContentFieldType.Float),
            Body("ao_inner", "Затемнение корпуса", ContentFieldType.Float),
            Body("ao_outer", "Контактная тень", ContentFieldType.Float),
            Body("armor_rings", "Кольца брони", ContentFieldType.Int),
            Body("front_plate", "Передняя плита", ContentFieldType.Bool),

            Section("movement", "speed", "Скорость", ContentFieldType.Float),
            Section("movement", "turn_speed", "Поворот", ContentFieldType.Float),
            Section("movement", "acceleration", "Разгон", ContentFieldType.Float),
            Section("movement", "brake", "Торможение", ContentFieldType.Float),

            Section("footprint", "rows", "Форма", ContentFieldType.StringList),
            Section("footprint", "facing_degrees", "Направление", ContentFieldType.Float),
            Section("footprint", "requires_metal_spot", "Точка метала", ContentFieldType.Bool),
            SectionEnum("footprint", "pattern", "Раскладка", typeof(BuildPattern)),
            SectionEnum("footprint", "pattern_alt", "Раскладка Alt", typeof(BuildPattern)),
            SectionScale("footprint", "pattern_step", "Шаг раскладки",
                ("narrow", 1f), ("margin", 2f)),

            Section("assembly", "cost_metal", "Стоимость", ContentFieldType.Float),
            Section("assembly", "frame_health", "Прочность каркаса", ContentFieldType.Float),

            Section("conversion", "energy_drain", "Расход энергии", ContentFieldType.Float),
            Section("conversion", "metal_output", "Выход металла", ContentFieldType.Float),

            Section("production", "energy", "Производство энергии", ContentFieldType.Float),
            Section("production", "metal", "Производство металла", ContentFieldType.Float),

            Section("storage", "metal", "Склад металла", ContentFieldType.Float),
            Section("storage", "energy", "Склад энергии", ContentFieldType.Float),

            Section("terror", "expansion_power", "Мощь экспансии", ContentFieldType.NullableFloat),
            Section("terror", "army_power", "Боевая мощь", ContentFieldType.NullableFloat),
            Section("terror", "ignore_modifiers", "Без модификаторов зоны", ContentFieldType.Bool),

            Section("battle", "approach_hold", "Доля подхода", ContentFieldType.Float),

            Section("plant", "factory_cooldown", "Пауза выпуска", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "build_power", "Мощность сборки", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "energy_per_power", "Энергия на мощность", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "rolloff_directions", "Направления выезда", ContentFieldType.Vector2List,
                def => def.Class == UnitClass.Plant || def.Plant != null),
            Section("plant", "rolloff_clearance", "Проверка выезда", ContentFieldType.Float,
                def => def.Class == UnitClass.Plant || def.Plant != null),

            Section("orders", "allow", "Разрешённые приказы", ContentFieldType.StringList),
            Section("orders", "deny", "Запрещённые приказы", ContentFieldType.StringList),
        };

        return fields;
    }

    private static List<ContentFieldSpec> BuildWeaponFields() =>
    [
        Root("id", "Идентификатор", ContentFieldType.RequiredString),
        Root("name", "Имя", ContentFieldType.String),
        Root("kind", "Вид", ContentFieldType.String),
        Root("range", "Дальность", ContentFieldType.Float),
        Root("aim_while_moving", "Наводка на ходу", ContentFieldType.Bool),
        Root("sprite", "Спрайт", ContentFieldType.Path),
        Root("sprite_rotation", "Поворот спрайта", ContentFieldType.Float),
        Root("damage", "Урон", ContentFieldType.Float),
        Root("fire_interval", "Интервал выстрела", ContentFieldType.Float),
        Root("projectile_speed", "Скорость снаряда", ContentFieldType.Float),
        Root("spread_degrees", "Разброс", ContentFieldType.Float),
        Root("aim_cone_degrees", "Конус прицеливания", ContentFieldType.Float),
        Root("projectile_radius", "Радиус снаряда", ContentFieldType.Float),
        Root("projectile_color", "Цвет снаряда", ContentFieldType.Color),
    ];

    private static List<ContentFieldSpec> BuildWorkToolFields() =>
    [
        Root("id", "Идентификатор", ContentFieldType.RequiredString),
        Root("name", "Имя", ContentFieldType.String),
        Root("kind", "Вид", ContentFieldType.String),
        Root("range", "Дальность", ContentFieldType.Float),
        Root("aim_while_moving", "Наводка на ходу", ContentFieldType.Bool),
        Root("sprite", "Спрайт", ContentFieldType.Path),
        Root("sprite_rotation", "Поворот спрайта", ContentFieldType.Float),
        Root("power", "Мощность", ContentFieldType.Float),
        Root("energy_per_power", "Энергия на мощность", ContentFieldType.Float),
        Root("works", "Виды работ", ContentFieldType.StringList),
        Root("repairs_units", "Чинит юнитов", ContentFieldType.Bool),
    ];

    private static ContentFieldSpec Root(string key, string label, ContentFieldType type) =>
        new() { Key = key, Label = label, Type = type, RootOnly = true };

    private static ContentFieldSpec RootEnum(string key, string label, Type enumType) =>
        new() { Key = key, Label = label, Type = ContentFieldType.Enum, EnumType = enumType, RootOnly = true };

    private static ContentFieldSpec Body(string key, string label, ContentFieldType type) =>
        Section("body", key, label, type);

    private static ContentFieldSpec BodyEnum(string key, string label, Type enumType) =>
        SectionEnum("body", key, label, enumType);

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
