using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Tomlyn.Model;

/// <summary>
/// Соответствие между ключом схемы и полем собранного определения — в обе стороны:
/// чтение итогового значения для показа и запись значения виджета в текст TOML.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ Store. Это чистое соответствие имён и полей, без состояния и без
/// обращения к диску. Пока оно лежало внутри Store, файл состояния наполовину состоял
/// из перечислений ключей, и правка одного ключа требовала чтения двух тысяч строк.
///
/// ПОЧЕМУ ЧИТАЕМ ИЗ ОПРЕДЕЛЕНИЯ, А НЕ ИЗ ТЕКСТА. Форма показывает действующее значение,
/// то есть итог наследования и подстановки переменных. В тексте файла его нет; оно
/// появляется только после сборки каталога с подставленными черновиками.
/// </summary>
public static class ContentValueReader
{
    /// <summary>Действующее значение поля юнита либо постройки.</summary>
    public static object Unit(UnitDefinition def, ContentFieldSpec field, TagRegistry tags)
    {
        if (def == null || field == null)
            return null;

        if (field.RootOnly)
            return field.Key switch
            {
                "id" => def.Id,
                "name" => def.DisplayName,
                "class" => def.Class.ToString(),
                "color" => def.Color,
                "tags" => TagNames(def, tags),
                "tools" => def.ToolIds,
                "buildbar" => def.Buildbar,
                _ => null,
            };

        return (field.Section ?? "") switch
        {
            "body" => field.Key switch
            {
                "max_health" => def.MaxHealth,
                "radius" => def.Radius,
                "vision_range" => def.VisionRange,
                "model" => def.Model,
                _ => null,
            },
            "movement" => field.Key switch
            {
                "speed" => def.Speed,
                "turn_speed" => def.TurnSpeedDegrees,
                "acceleration" => def.Acceleration,
                "brake" => def.Brake,
                _ => null,
            },
            "footprint" => field.Key switch
            {
                "rows" => def.Rows,
                "facing_degrees" => def.FacingDegrees,
                "requires_metal_spot" => def.RequiresMetalSpot,
                "pattern" => def.Pattern.ToString(),
                "pattern_alt" => def.PatternAlt.ToString(),
                "pattern_step" => def.PatternStep,
                _ => null,
            },
            "assembly" => field.Key switch
            {
                "cost_metal" => def.Assembly?.CostMetal,
                "frame_health" => def.Assembly?.FrameHealth,
                _ => null,
            },
            "conversion" => field.Key switch
            {
                "energy_drain" => def.Conversion?.EnergyDrain,
                "metal_output" => def.Conversion?.MetalOutput,
                _ => null,
            },
            "production" => field.Key switch
            {
                "energy" => def.EnergyProduction,
                "metal" => def.MetalProduction,
                _ => null,
            },
            "storage" => field.Key switch
            {
                "metal" => def.MetalStorage,
                "energy" => def.EnergyStorage,
                _ => null,
            },
            "terror" => field.Key switch
            {
                "expansion_power" => def.ExpansionPowerWeight,
                "army_power" => def.ArmyPowerWeight,
                "ignore_modifiers" => def.IgnoreTerrorModifiers,
                _ => null,
            },
            "battle" => field.Key switch
            {
                "approach_hold" => def.ApproachHoldFraction,
                _ => null,
            },
            "plant" => field.Key switch
            {
                "factory_cooldown" => def.Plant?.FactoryCooldown,
                "build_power" => def.Plant?.BuildPower,
                "energy_per_power" => def.Plant?.EnergyPerPower,
                "rolloff_directions" => def.Plant?.RolloffDirections,
                "rolloff_clearance" => def.Plant?.RolloffClearance,
                _ => null,
            },
            "orders" => field.Key switch
            {
                "allow" => OrderNames(def.DeclaredOrders),
                "deny" => OrderNames(def.DeniedOrders),
                _ => null,
            },
            _ => null,
        };
    }

    /// <summary>Действующее значение поля оружия либо рабочего инструмента.</summary>
    public static object Tool(ToolDefinition tool, ContentFieldSpec field)
    {
        if (tool == null || field == null)
            return null;

        return field.Key switch
        {
            "id" => tool.Id,
            "name" => tool.DisplayName,
            "kind" => tool is WeaponDefinition ? "weapon" : "work",
            "range" => tool.Range,
            "aim_arc_degrees" => tool.AimArcDegrees,
            "aim_rate_degrees" => tool.AimRateDegrees,
            "aim_idle_delay" => tool.AimIdleDelay,
            "aim_priority" => tool.AimPriority,
            "body_assist" => tool.BodyAssist,
            "damage" => (tool as WeaponDefinition)?.Damage,
            "fire_interval" => (tool as WeaponDefinition)?.FireInterval,
            "projectile_speed" => (tool as WeaponDefinition)?.ProjectileSpeed,
            "spread_degrees" => (tool as WeaponDefinition)?.SpreadDegrees,
            "fire_arc_degrees" => (tool as WeaponDefinition)?.FireArcDegrees,
            "projectile_radius" => (tool as WeaponDefinition)?.ProjectileRadius,
            "projectile_color" => (tool as WeaponDefinition)?.ProjectileColor,
            "power" => (tool as WorkToolDefinition)?.Power,
            "energy_per_power" => (tool as WorkToolDefinition)?.EnergyPerPower,
            "works" => WorkNames(tool as WorkToolDefinition),
            "repairs_units" => (tool as WorkToolDefinition)?.RepairsUnits,
            _ => null,
        };
    }

    /// <summary>
    /// Действующее значение поля волны. Волна, не прошедшая проверку, в каталог не попадает;
    /// тогда значение читается прямо из черновика — иначе поля инспектора опустели бы
    /// на время правки.
    /// </summary>
    public static object Wave(WaveDefinition wave, string draftText, ContentFieldSpec field)
    {
        if (field == null)
            return null;

        if (wave == null)
            return TomlText.RawValue(draftText, field.RootOnly ? null : field.Section, field.Key);

        if (field.RootOnly || string.IsNullOrEmpty(field.Section))
            return field.Key switch
            {
                "id" => wave.Id,
                "name" => wave.DisplayName,
                "tags" => wave.Tags.ToList(),
                "prefer_next" => wave.PreferNext.ToList(),
                "terror_range" => new List<float> { wave.TerrorMin, wave.TerrorMax },
                "army_power_budget" => wave.ArmyPowerBudget,
                "army_power_per_terror" => wave.ArmyPowerPerTerror,
                "chill_interval_multiplier" => wave.ChillIntervalMultiplier,
                "chill_interval_offset" => wave.ChillIntervalOffset,
                _ => null,
            };

        if (field.Section != "spawn")
            return null;

        var shape = wave.Shape;
        return field.Key switch
        {
            "near_arc_degrees" => shape.NearArcDegrees,
            "far_arc_degrees" => shape.FarArcDegrees,
            "wave_start" => shape.WaveStart,
            "radius_depth_multiplier" => shape.RadiusDepthMultiplier,
            "spacing_cells" => shape.SpacingCells,
            "groups" => shape.Groups,
            "groups_arc_degrees" => shape.GroupsArcDegrees,
            "group_delay_seconds" => shape.GroupDelaySeconds,
            _ => null,
        };
    }

    /// <summary>
    /// Представление значения виджета в синтаксисе TOML. Возвращает <c>null</c>, когда
    /// значение непригодно для этого типа поля: записывать неразобранный ввод нельзя.
    /// </summary>
    public static string Encode(ContentFieldSpec field, object value)
    {
        if (value == null || field == null)
            return null;

        return field.Type switch
        {
            ContentFieldType.Bool => TomlPatchWriter.FormatBool((bool)value),
            ContentFieldType.Float or ContentFieldType.NullableFloat =>
                TomlPatchWriter.FormatFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Int =>
                TomlPatchWriter.FormatInt(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ContentFieldType.String or ContentFieldType.RequiredString or ContentFieldType.Path =>
                TomlPatchWriter.FormatString(Convert.ToString(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Enum =>
                TomlPatchWriter.FormatEnum(Convert.ToString(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Color when value is Color color =>
                TomlPatchWriter.FormatColor(color.R, color.G, color.B, color.A),
            ContentFieldType.StringList when value is IList<string> list =>
                TomlPatchWriter.FormatStringList(list.ToList()),
            ContentFieldType.FloatList when value is IList<float> floats =>
                TomlPatchWriter.FormatFloatList(floats.ToList()),
            ContentFieldType.Scale when value is string scaleName =>
                TomlPatchWriter.FormatString(scaleName),
            ContentFieldType.Scale =>
                TomlPatchWriter.FormatFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Vector2List when value is IList<Vector2> vectors => Vectors(vectors),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Представление значения, прочитанного из TOML, в том же синтаксисе. Нужно затем,
    /// чтобы сравнить кандидата с унаследованным значением текстом, а не по типам.
    /// </summary>
    public static string EncodeRaw(object value) => value switch
    {
        null => "",
        bool flag => TomlPatchWriter.FormatBool(flag),
        double number => TomlPatchWriter.FormatFloat((float)number),
        long number => TomlPatchWriter.FormatInt((int)number),
        float number => TomlPatchWriter.FormatFloat(number),
        int number => TomlPatchWriter.FormatInt(number),
        string text => TomlPatchWriter.FormatString(text),
        TomlArray array => "[" + string.Join(", ", array.Select(EncodeRaw)) + "]",
        _ => value.ToString(),
    };

    /// <summary>
    /// Сравнение представлений без учёта пробелов, кавычек и регистра: один и тот же
    /// смысл может быть записан по-разному, а лишний локальный ключ создавать не нужно.
    /// </summary>
    public static bool SameToml(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string text) =>
        (text ?? "").Replace(" ", "").Replace("\"", "").ToLowerInvariant();

    private static string Vectors(IList<Vector2> vectors) =>
        "[" + string.Join(", ", vectors.Select(v =>
            $"[{TomlPatchWriter.FormatFloat(v.X)}, {TomlPatchWriter.FormatFloat(v.Y)}]")) + "]";

    /// <summary>Имена тегов, действующих у определения: набор битов обратно в имена.</summary>
    private static string[] TagNames(UnitDefinition def, TagRegistry tags)
    {
        if (def == null || tags == null)
            return Array.Empty<string>();

        var names = new List<string>();
        foreach (string name in tags.Names)
        {
            if (tags.TryParse(new[] { name }, "editor", out var single) && def.Tags.Has(single))
                names.Add(name);
        }

        return names.ToArray();
    }

    /// <summary>Имена разрешённых приказов в змеином регистре, как они пишутся в файле.</summary>
    private static string[] OrderNames(OrderSet set)
    {
        var names = new List<string>();
        foreach (OrderKind kind in Enum.GetValues<OrderKind>())
        {
            if (set.Allows(kind))
                names.Add(TomlPatchWriter.ToSnake(kind.ToString()));
        }

        return names.ToArray();
    }

    /// <summary>
    /// Виды работ инструмента. Стройка уже подразумевает ремонт, поэтому явное
    /// <c>repair</c> выводится только для инструмента, который строить не умеет.
    /// </summary>
    private static string[] WorkNames(WorkToolDefinition work)
    {
        if (work == null)
            return Array.Empty<string>();

        if (work.Kinds.HasFlag(WorkKinds.Build))
            return new[] { "build" };

        return work.Kinds.HasFlag(WorkKinds.Repair)
            ? new[] { "repair" }
            : Array.Empty<string>();
    }
}
