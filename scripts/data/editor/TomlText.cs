using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tomlyn;
using Tomlyn.Model;

/// <summary>
/// Низкоуровневые операции над текстом TOML, общие для всего редактора контента.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ КЛАСС. Разбор текста нужен индексу файлов, вычислению происхождения
/// значения, снисходительному чтению волн и построению графа зависимостей. Пока каждый
/// из них держал собственную копию <c>ParseTable</c> и обхода секций, любое исправление
/// разбора приходилось повторять несколько раз.
///
/// ЗАЧЕМ КЭШ. Одно обновление формы вычисляет происхождение для каждого поля, а каждое
/// такое вычисление прежде разбирало черновик целиком и вдобавок обходило цепочку base.
/// Для файла в двести строк и формы в полсотни полей это давало десятки разборов на кадр.
/// Кэш хранит соответствие «текст → таблица»: ключом служит сам текст, поэтому правка
/// черновика автоматически даёт промах и повторный разбор, а устаревших данных не бывает.
/// </summary>
public static class TomlText
{
    /// <summary>
    /// Предел числа хранимых разборов. Ограничение нужно потому, что ключом служит текст
    /// черновика: при правке в форме каждый набранный символ порождает новый ключ, и без
    /// предела кэш рос бы вместе с длиной сеанса.
    /// </summary>
    private const int CacheLimit = 256;

    private static readonly Dictionary<string, TomlTable> Cache = new(StringComparer.Ordinal);

    /// <summary>Порядок поступления ключей: при переполнении удаляется самый старый.</summary>
    private static readonly Queue<string> CacheOrder = new();

    /// <summary>
    /// Разобрать текст в таблицу. Возвращает <c>null</c> для пустого текста и для текста
    /// с синтаксическими ошибками: в редакторе это обычное промежуточное состояние правки,
    /// а не причина для исключения.
    /// </summary>
    public static TomlTable Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        if (Cache.TryGetValue(text, out TomlTable cached))
            return cached;

        TomlTable table;
        try
        {
            var syntax = Toml.Parse(text);
            table = syntax.HasErrors ? null : syntax.ToModel();
        }
        catch (Exception)
        {
            // Tomlyn бросает исключение на отдельных сочетаниях недописанных конструкций,
            // тогда как для редактора недописанный файл есть норма.
            table = null;
        }

        Remember(text, table);
        return table;
    }

    /// <summary>Очистить кэш. Вызывается при полной перечитке файлов с диска.</summary>
    public static void ClearCache()
    {
        Cache.Clear();
        CacheOrder.Clear();
    }

    private static void Remember(string text, TomlTable table)
    {
        // Разбор с ошибкой тоже запоминается: повторно разбирать заведомо испорченный
        // текст незачем, а пометкой служит null.
        if (Cache.Count >= CacheLimit && CacheOrder.Count > 0)
            Cache.Remove(CacheOrder.Dequeue());

        Cache[text] = table;
        CacheOrder.Enqueue(text);
    }

    /// <summary>Таблица секции либо корень, когда имя секции пусто.</summary>
    public static TomlTable SectionOf(TomlTable table, string section)
    {
        if (table == null)
            return null;

        if (string.IsNullOrEmpty(section))
            return table;

        return table.TryGetValue(section, out object value) && value is TomlTable nested
            ? nested
            : null;
    }

    /// <summary>Есть ли ключ непосредственно в этом тексте, а не в цепочке наследования.</summary>
    public static bool HasKey(string text, string section, string key)
    {
        var table = SectionOf(Parse(text), section);
        return table != null && table.ContainsKey(key);
    }

    /// <summary>Сырое значение ключа без разрешения наследования.</summary>
    public static bool TryGetValue(TomlTable table, string section, string key, out object value)
    {
        value = null;
        var target = SectionOf(table, section);
        return target != null && target.TryGetValue(key, out value);
    }

    /// <summary>Сырое значение ключа из текста, приведённое к типам редактора.</summary>
    public static object RawValue(string text, string section, string key) =>
        TryGetValue(Parse(text), section, key, out object value) ? Convert(value) : null;

    /// <summary>
    /// Элементы массива таблиц независимо от того, чем их представил разбор: Tomlyn
    /// возвращает <see cref="TomlTableArray"/> для <c>[[name]]</c>, но встроенный массив
    /// таблиц в одну строку приходит обычным <see cref="TomlArray"/>.
    /// </summary>
    public static IEnumerable<TomlTable> TableItems(object raw) => raw switch
    {
        TomlTableArray array => array,
        TomlArray array => array.OfType<TomlTable>(),
        TomlTable single => new[] { single },
        _ => Array.Empty<TomlTable>(),
    };

    /// <summary>Число по ключу либо запасное значение, если ключа нет или он не число.</summary>
    public static float Number(TomlTable table, string key, float fallback)
    {
        if (table == null || !table.TryGetValue(key, out object value))
            return fallback;

        try
        {
            return System.Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            // В черновике на месте числа может оказаться строка или ссылка на vars.
            return fallback;
        }
    }

    /// <summary>Строки массива по ключу; иные элементы пропускаются.</summary>
    public static string[] Strings(TomlTable table, string key) =>
        table != null && table.TryGetValue(key, out object value) && value is TomlArray array
            ? array.OfType<string>().ToArray()
            : Array.Empty<string>();

    /// <summary>Числа массива по ключу; неразобранные элементы пропускаются.</summary>
    public static float[] Numbers(TomlTable table, string key)
    {
        if (table == null || !table.TryGetValue(key, out object value) || value is not TomlArray array)
            return Array.Empty<float>();

        var result = new List<float>(array.Count);
        foreach (object item in array)
        {
            try
            {
                result.Add(System.Convert.ToSingle(item, CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // Пропускаем нечисловой элемент: правка списка проходит через такие состояния.
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Привести значение Tomlyn к типам, которыми оперируют виджеты формы: числа
    /// с плавающей точкой, целые, списки строк и списки чисел.
    /// </summary>
    public static object Convert(object value) => value switch
    {
        double number => (float)number,
        long number => (int)number,
        TomlArray array when array.All(item => item is string) => array.OfType<string>().ToList(),
        TomlArray array => array
            .Select(item => System.Convert.ToSingle(item, CultureInfo.InvariantCulture))
            .ToList(),
        _ => value,
    };

    /// <summary>Имя файла из пути <c>res://</c>: путь всегда записан прямыми косыми.</summary>
    public static string FileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";

        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }
}
