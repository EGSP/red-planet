using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Строка таблицы баланса: величины одной сущности, уже приведённые к сравнимому виду.
///
/// ПОЧЕМУ ОТДЕЛЬНАЯ СТРУКТУРА, А НЕ САМО ОПРЕДЕЛЕНИЕ. Часть колонок является производной
/// (урон в секунду, чистая энергия, здоровье на металл), и считать их при каждой отрисовке
/// ячейки и при каждом сравнении значило бы считать одно и то же многократно.
/// </summary>
public readonly struct BalanceRow
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Class { get; init; }
    public bool IsStructure { get; init; }
    public bool IsEnemy { get; init; }
    public bool IsAttacker { get; init; }
    public bool IsBuilder { get; init; }

    /// <summary>Определение нужно колонке имени: она показывает силуэт иконкой.</summary>
    public UnitDefinition Definition { get; init; }

    public float Health { get; init; }
    public float Cost { get; init; }
    public float Speed { get; init; }
    public float Vision { get; init; }
    public string Size { get; init; }
    public float Dps { get; init; }
    public float WeaponRange { get; init; }
    public float HealthPerMetal { get; init; }
    public float EnergyNet { get; init; }
    public float MetalNet { get; init; }
    public float ArmyPower { get; init; }
    public float ExpansionPower { get; init; }
    public float BuildPower { get; init; }

    public static BalanceRow From(UnitDefinition def)
    {
        float dps = 0f;
        float range = 0f;
        if (def.Weapon != null && def.Weapon.FireInterval > 0f)
        {
            dps = def.Weapon.Damage / def.Weapon.FireInterval;
            range = def.Weapon.Range;
        }

        // Расход энергии складывается из переработки, строительного инструмента и завода:
        // «чистая энергия» иначе показывала бы одну лишь выработку.
        float energyDrain = (def.Conversion?.EnergyDrain ?? 0f)
                            + (def.BuildTool?.EnergyDrain ?? 0f)
                            + ((def.Plant?.BuildPower ?? 0f) * (def.Plant?.EnergyPerPower ?? 0f));

        return new BalanceRow
        {
            Id = def.Id,
            Name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName,
            Class = def.Class.ToString(),
            IsStructure = def.IsStructure,
            IsEnemy = def.Class == UnitClass.Enemy,
            IsAttacker = def.Weapon != null,
            IsBuilder = def.CanBuild,
            Definition = def,
            Health = def.MaxHealth,
            Cost = def.Assembly?.CostMetal ?? 0f,
            Speed = def.Speed,
            Vision = def.VisionRange,
            Size = def.IsStructure ? $"{def.Width}×{def.Height}" : $"r={def.Radius:0.##}",
            Dps = dps,
            WeaponRange = range,
            HealthPerMetal = def.HealthPerMetal,
            EnergyNet = def.EnergyProduction - energyDrain,
            MetalNet = def.MetalProduction + (def.Conversion?.MetalOutput ?? 0f),
            ArmyPower = def.ArmyPower,
            ExpansionPower = def.ExpansionPower,
            BuildPower = def.BuildTool?.Power ?? def.Plant?.BuildPower ?? 0f,
        };
    }
}

/// <summary>
/// Описание одной колонки таблицы баланса: подпись, ширина, содержимое ячейки и порядок
/// сортировки.
///
/// ЗАЧЕМ ОДНА ЗАПИСЬ НА КОЛОНКУ. Прежде колонка описывалась трижды: в перечне колонок,
/// в разборе <c>switch</c> при заполнении ячейки и ещё раз в сравнении для сортировки.
/// Новая величина требовала трёх согласованных правок, а забытая ветвь сравнения давала
/// таблицу, которая молча не сортируется. Теперь колонка объявляется один раз и сама
/// знает, что показать и как сравнить.
/// </summary>
public sealed class BalanceColumn
{
    public string Id { get; init; }
    public string Title { get; init; }

    /// <summary>Раздел в списке выбора колонок: Identity, Combat, Economy, Construction.</summary>
    public string Section { get; init; }

    public int MinWidth { get; init; }

    /// <summary>Растягивать ли колонку по остатку ширины, пока её не тянули вручную.</summary>
    public bool Expand { get; init; }

    /// <summary>Содержимое ячейки.</summary>
    public Func<BalanceRow, string> Text { get; init; }

    /// <summary>
    /// Ключ сортировки для числовой колонки. Пусто — колонка сортируется по тексту,
    /// как имя, идентификатор и класс.
    /// </summary>
    public Func<BalanceRow, float> Number { get; init; }

    /// <summary>Сравнение двух строк по этой колонке.</summary>
    public int Compare(BalanceRow left, BalanceRow right) =>
        Number != null
            ? Number(left).CompareTo(Number(right))
            : string.Compare(Text(left), Text(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// По возрастанию ли сортировать при первом щелчке. Для текста естественен алфавит,
    /// для чисел — «сначала крупные»: при подборе баланса ищут выбросы вверх.
    /// </summary>
    public bool AscendingByDefault => Number == null;
}

/// <summary>
/// Перечень колонок таблицы баланса и заготовки представлений.
///
/// Порядок перечня задаёт и порядок колонок в таблице, и порядок туглов в списке выбора:
/// собственный порядок для каждого места означал бы, что колонка, найденная в списке,
/// стоит в таблице не там, где ожидалось.
/// </summary>
public static class BalanceColumns
{
    /// <summary>Представление по умолчанию при первом запуске редактора.</summary>
    public const string DefaultViewId = "combat";

    /// <summary>Набор колонок, не совпадающий ни с одной заготовкой.</summary>
    public const string CustomViewId = "custom";

    /// <summary>Колонка сортировки по умолчанию: боевая мощь сводит виды к одной мере.</summary>
    public const string DefaultSortColumnId = "army";

    /// <summary>Колонка имени показывается всегда: строка без имени неразличима.</summary>
    public const string NameColumnId = "name";

    public static IReadOnlyList<BalanceColumn> All { get; } = new[]
    {
        Text("name", "Name", "Identity", 160, row => row.Name, expand: true),
        Text("id", "Id", "Identity", 90, row => row.Id),
        Text("class", "Class", "Identity", 80, row => row.Class),
        Number("army", "Army Power", "Combat", 88, row => row.ArmyPower),
        Number("hp", "Health", "Combat", 64, row => row.Health),
        Number("dps", "DPS", "Combat", 64, row => row.Dps),
        Number("range", "Range", "Combat", 64, row => row.WeaponRange),
        Number("vision", "Vision", "Combat", 64, row => row.Vision),
        Number("speed", "Speed", "Combat", 64, row => row.Speed),
        Text("size", "Size", "Combat", 72, row => row.Size),
        Number("cost", "Cost", "Economy", 64, row => row.Cost),
        Number("hp_per_metal", "Health/Metal", "Economy", 96, row => row.HealthPerMetal),
        Number("energy_net", "Net Energy", "Economy", 88, row => row.EnergyNet),
        Number("metal_net", "Net Metal", "Economy", 80, row => row.MetalNet),
        Number("expansion", "Expansion Power", "Economy", 112, row => row.ExpansionPower),
        Number("build_power", "Build Power", "Construction", 88, row => row.BuildPower),
    };

    /// <summary>Встроенные представления: имя вкладки и состав колонок.</summary>
    public static IReadOnlyList<(string Id, string Title, string[] Columns)> BuiltInViews { get; } = new[]
    {
        ("all", "All", All.Select(column => column.Id).ToArray()),
        ("combat", "Combat",
            new[] { "name", "class", "army", "hp", "cost", "speed", "vision", "dps", "range", "size" }),
        ("economy", "Economy",
            new[] { "name", "class", "army", "cost", "energy_net", "metal_net", "hp" }),
        ("construction", "Construction",
            new[] { "name", "class", "army", "cost", "build_power", "size", "hp" }),
    };

    /// <summary>Колонка по идентификатору либо <c>null</c>, если такой колонки больше нет.</summary>
    public static BalanceColumn ById(string id) =>
        All.FirstOrDefault(column => column.Id == id);

    /// <summary>Существует ли колонка: снимок мог быть записан прежней версией редактора.</summary>
    public static bool Exists(string id) => ById(id) != null;

    /// <summary>Колонки заготовки; для неизвестного идентификатора — набор по умолчанию.</summary>
    public static string[] ColumnsForView(string viewId)
    {
        foreach (var view in BuiltInViews)
        {
            if (view.Id == viewId)
                return view.Columns;
        }

        return BuiltInViews.First(view => view.Id == DefaultViewId).Columns;
    }

    /// <summary>Число для ячейки: ноль показывается прочерком, иначе таблица пестрит нулями.</summary>
    private static string Format(float value) =>
        value == 0f ? "—" : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static BalanceColumn Text(
        string id, string title, string section, int width,
        Func<BalanceRow, string> text, bool expand = false) => new()
    {
        Id = id,
        Title = title,
        Section = section,
        MinWidth = width,
        Expand = expand,
        Text = text,
    };

    private static BalanceColumn Number(
        string id, string title, string section, int width,
        Func<BalanceRow, float> number) => new()
    {
        Id = id,
        Title = title,
        Section = section,
        MinWidth = width,
        Text = row => Format(number(row)),
        Number = number,
    };
}
