using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Снисходительное чтение файлов волн из черновиков.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ СБОРКИ. <see cref="ContentCompiler"/> отбрасывает волну целиком при
/// любой ошибке проверки, а правка проходит через недопустимые промежуточные состояния:
/// пустой список видов, доля вне пределов, ссылка на ещё не переименованный вид. Если бы
/// карта волн читала только собранный каталог, строка исчезала бы посреди правки. Здесь
/// файл читается как есть, а признаком годности служит <see cref="WaveOverview.Compiled"/>.
/// </summary>
public static class WaveDraftReader
{
    /// <summary>Имя массива таблиц со списками видов: <c>[[unit_list]]</c>.</summary>
    public const string UnitListArray = "unit_list";

    /// <summary>
    /// Обзор всех волн по текстам черновиков. <paramref name="catalog"/> нужен только
    /// затем, чтобы отметить волны, прошедшие проверку.
    /// </summary>
    public static List<WaveOverview> Overviews(
        IEnumerable<ContentEditorEntry> entries,
        IReadOnlyDictionary<string, string> texts,
        Catalog catalog)
    {
        var result = new List<WaveOverview>();

        foreach (var entry in entries.Where(candidate => candidate.Kind == ContentEntityKind.Wave))
        {
            if (!texts.TryGetValue(entry.Path, out string text))
                continue;

            var table = TomlText.Parse(text);
            if (table == null)
                continue;

            float[] range = TomlText.Numbers(table, "terror_range");
            result.Add(new WaveOverview
            {
                Id = entry.Id,
                Path = entry.Path,
                FileName = entry.FileName,
                DisplayName = entry.DisplayName,
                Tags = TomlText.Strings(table, "tags"),
                TerrorMin = range.Length > 0 ? range[0] : -1f,
                TerrorMax = range.Length > 1 ? range[1] : -1f,
                Budget = TomlText.Number(table, "army_power_budget", 10f),
                BudgetPerTerror = TomlText.Number(table, "army_power_per_terror", 0f),
                ChillMultiplier = TomlText.Number(table, "chill_interval_multiplier", 1f),
                ListCount = table.TryGetValue(UnitListArray, out object lists)
                    ? TomlText.TableItems(lists).Count()
                    : 0,
                Compiled = catalog?.Wave(entry.Id) != null,
            });
        }

        return result;
    }

    /// <summary>Блоки <c>[[unit_list]]</c> черновика в порядке файла.</summary>
    public static List<WaveUnitListView> UnitLists(string draftText)
    {
        var result = new List<WaveUnitListView>();
        var table = TomlText.Parse(draftText);
        if (table == null || !table.TryGetValue(UnitListArray, out object raw))
            return result;

        int index = 0;
        foreach (var item in TomlText.TableItems(raw))
        {
            result.Add(new WaveUnitListView
            {
                Index = index++,
                Mode = item.TryGetValue("mode", out object mode) && mode is string name
                    ? name
                    : "allow",
                UnitIds = TomlText.Strings(item, "units"),
                Share = TomlText.Number(item, "target_budget_share", 0f),
                HasShare = item.ContainsKey("target_budget_share"),
            });
        }

        return result;
    }

    /// <summary>Значение ключа внутри блока массива таблиц по его номеру.</summary>
    public static object ArrayItemValue(string draftText, string array, int index, string key)
    {
        var table = TomlText.Parse(draftText);
        if (table == null || !table.TryGetValue(array, out object raw))
            return null;

        var items = TomlText.TableItems(raw).ToList();
        if (index < 0 || index >= items.Count)
            return null;

        return items[index].TryGetValue(key, out object value) ? TomlText.Convert(value) : null;
    }

    /// <summary>
    /// Переопределения формы появления, прочитанные из черновика напрямую.
    ///
    /// Панель формы должна показывать построение и для волны, не прошедшей проверку:
    /// именно тогда числа и подбираются. Собранное определение для этого непригодно —
    /// его просто нет.
    /// </summary>
    public static WaveShapeOverrides ShapeOverrides(string draftText)
    {
        var over = new WaveShapeOverrides();
        var spawn = TomlText.SectionOf(TomlText.Parse(draftText), "spawn");
        if (spawn == null)
            return over;

        over.NearArcDegrees = Optional(spawn, "near_arc_degrees");
        over.FarArcDegrees = Optional(spawn, "far_arc_degrees");
        over.WaveStart = Optional(spawn, "wave_start");
        over.RadiusDepthMultiplier = Optional(spawn, "radius_depth_multiplier");
        over.SpacingCells = Optional(spawn, "spacing_cells");
        over.GroupsArcDegrees = Optional(spawn, "groups_arc_degrees");
        over.GroupDelaySeconds = Optional(spawn, "group_delay_seconds");

        if (Optional(spawn, "groups") is { } groups)
            over.Groups = (int)groups;

        return over;
    }

    /// <summary>
    /// Число по ключу либо отсутствие значения. Именно отсутствие, а не ноль: незаданный
    /// ключ означает «действует умолчание подсистемы», и ноль вместо него исказил бы форму.
    /// </summary>
    private static float? Optional(Tomlyn.Model.TomlTable table, string key)
    {
        if (table == null || !table.TryGetValue(key, out object value))
            return null;

        try
        {
            return System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
