using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Tomlyn.Model;

/// <summary>
/// Разрешение наследования TOML: <c>base</c> на корне и в секциях, ссылки на файлы
/// в значениях полей, справочники <c>*.vars.toml</c>.
///
/// ПОРЯДОК. Сначала все сырые таблицы лежат в мешке. Затем каждый файл материализуется
/// рекурсивно: основа из <c>base</c>, поверх — ключи текущего файла, строковые пути
/// заменяются значением из указанного файла. Кольцо зависимостей — ошибка со списком
/// участников цепи.
///
/// ПУТИ. Корень ресурсов — <c>res://resources/</c>. Запись <c>/units/dox.toml</c>
/// означает тот же файл. Для юнита в значении поля достаточно пути к файлу: берётся
/// одноимённое свойство (сначала из той же секции, затем с корня). Для справочника
/// констант путь обязан содержать имя свойства: <c>/content/x.vars.toml/radius</c>.
/// </summary>
public static class TomlResolver
{
    private const string ResourcesRoot = "res://resources/";

    /// <summary>
    /// Материализовать все таблицы мешка. В <paramref name="resolved"/> попадают
    /// плоские документы без <c>base</c> и без нераскрытых ссылок на файлы.
    /// </summary>
    public static int Resolve(
        IReadOnlyDictionary<string, TomlTable> raw,
        out Dictionary<string, TomlTable> resolved)
    {
        resolved = new Dictionary<string, TomlTable>(StringComparer.Ordinal);
        int errors = 0;
        var stack = new List<string>();

        foreach (string path in raw.Keys.OrderBy(p => p, StringComparer.Ordinal))
            errors += Materialize(path, raw, resolved, stack);

        return errors;
    }

    private static int Materialize(
        string path,
        IReadOnlyDictionary<string, TomlTable> raw,
        Dictionary<string, TomlTable> resolved,
        List<string> stack)
    {
        if (resolved.ContainsKey(path))
            return 0;

        if (!raw.ContainsKey(path))
        {
            GD.PushError($"[Контент] ссылка на отсутствующий файл: {path}");
            resolved[path] = new TomlTable();
            return 1;
        }

        int cycleAt = stack.IndexOf(path);

        if (cycleAt >= 0)
        {
            var cycle = stack.Skip(cycleAt).Append(path);
            GD.PushError("[Контент] кольцо зависимостей TOML: " +
                         string.Join(" → ", cycle.Select(Short)));
            resolved[path] = new TomlTable();
            return 1;
        }

        stack.Add(path);
        int errors = 0;

        var local = CloneTable(raw[path]);
        string rootBase = PopBase(local);

        TomlTable foundation = new();

        if (rootBase != null)
        {
            if (!TryCanonicalPath(rootBase, out string basePath, out string leftover))
            {
                GD.PushError($"[Контент] {Short(path)}: некорректный base «{rootBase}»");
                errors++;
            }
            else if (!string.IsNullOrEmpty(leftover))
            {
                GD.PushError($"[Контент] {Short(path)}: base указывает на свойство «{leftover}», " +
                             "нужен путь к файлу");
                errors++;
            }
            else
            {
                errors += Materialize(basePath, raw, resolved, stack);
                // id и abstract принадлежат файлу, а не наследуемым числам: иначе потомок
                // abstract-предка сам стал бы abstract и выпал из каталога
                foundation = CloneTable(resolved[basePath]);
                foundation.Remove("id");
                foundation.Remove("abstract");
            }
        }

        var result = new TomlTable();
        var keys = new HashSet<string>(foundation.Keys, StringComparer.Ordinal);

        foreach (string key in local.Keys)
            keys.Add(key);

        foreach (string key in keys)
        {
            local.TryGetValue(key, out object localValue);
            foundation.TryGetValue(key, out object basisValue);

            if (localValue is TomlTable localSection || basisValue is TomlTable)
            {
                var sectionLocal = localValue as TomlTable ?? new TomlTable();
                var sectionBasis = basisValue as TomlTable;
                errors += MaterializeSection(
                    path, key, sectionLocal, sectionBasis, raw, resolved, stack, out var section);
                result[key] = section;
                continue;
            }

            if (localValue != null)
            {
                errors += ResolveValue(
                    path, null, key, localValue, raw, resolved, stack, out object value);
                result[key] = value;
            }
            else
            {
                result[key] = CloneValue(basisValue);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        resolved[path] = result;
        return errors;
    }

    private static int MaterializeSection(
        string ownerPath,
        string sectionName,
        TomlTable local,
        TomlTable inherited,
        IReadOnlyDictionary<string, TomlTable> raw,
        Dictionary<string, TomlTable> resolved,
        List<string> stack,
        out TomlTable result)
    {
        int errors = 0;
        local = CloneTable(local);
        string sectionBase = PopBase(local);
        TomlTable foundation;

        if (sectionBase != null)
        {
            foundation = new TomlTable();

            if (!TryCanonicalPath(sectionBase, out string basePath, out string leftover))
            {
                GD.PushError(
                    $"[Контент] {Short(ownerPath)} → [{sectionName}]: некорректный base «{sectionBase}»");
                errors++;
            }
            else if (!string.IsNullOrEmpty(leftover))
            {
                GD.PushError(
                    $"[Контент] {Short(ownerPath)} → [{sectionName}]: base указывает на свойство, " +
                    "нужен путь к файлу");
                errors++;
            }
            else
            {
                errors += Materialize(basePath, raw, resolved, stack);
                var baseFile = resolved[basePath];

                if (IsVars(basePath))
                    foundation = baseFile;
                else if (baseFile.TryGetValue(sectionName, out object section) &&
                         section is TomlTable table)
                    foundation = table;
                else
                {
                    GD.PushError(
                        $"[Контент] {Short(ownerPath)} → [{sectionName}]: в «{Short(basePath)}» " +
                        $"нет секции [{sectionName}]");
                    errors++;
                }
            }
        }
        else
        {
            foundation = inherited ?? new TomlTable();
        }

        result = Merge(foundation, local);

        var keys = result.Keys.ToList();

        foreach (string key in keys)
        {
            object value = result[key];

            if (value is TomlTable nested)
            {
                // Вложенные таблицы внутри секции (редко) — только раскрытие ссылок в листьях
                errors += MaterializeSection(
                    ownerPath, $"{sectionName}.{key}", nested, null, raw, resolved, stack,
                    out var done);
                result[key] = done;
                continue;
            }

            errors += ResolveValue(
                ownerPath, sectionName, key, value, raw, resolved, stack, out object resolvedValue);
            result[key] = resolvedValue;
        }

        return errors;
    }

    private static int ResolveValue(
        string ownerPath,
        string sectionName,
        string key,
        object value,
        IReadOnlyDictionary<string, TomlTable> raw,
        Dictionary<string, TomlTable> resolved,
        List<string> stack,
        out object result)
    {
        result = CloneValue(value);

        if (value is not string text || !TryCanonicalPath(text, out string filePath, out string prop))
            return 0;

        // Обычные строки справочника (hull = "circle", buildbar = "...") путём не являются
        if (!LooksLikePath(text))
            return 0;

        int errors = Materialize(filePath, raw, resolved, stack);
        var target = resolved[filePath];
        string property = prop;

        if (string.IsNullOrEmpty(property))
        {
            if (IsVars(filePath))
            {
                GD.PushError(
                    $"[Контент] {Where(ownerPath, sectionName)}: ссылка на «{Short(filePath)}» " +
                    "без имени свойства. Для *.vars.toml нужно /файл.vars.toml/имя");
                result = value;
                return errors + 1;
            }

            property = key;
        }

        if (!TryLookup(target, sectionName, property, out result))
        {
            GD.PushError(
                $"[Контент] {Where(ownerPath, sectionName)}: в «{Short(filePath)}» " +
                $"нет свойства «{property}»");
            result = value;
            return errors + 1;
        }

        return errors;
    }

    private static bool TryLookup(
        TomlTable file, string sectionName, string property, out object value)
    {
        if (!string.IsNullOrEmpty(sectionName)
            && file.TryGetValue(sectionName, out object section)
            && section is TomlTable table
            && table.TryGetValue(property, out value))
            return true;

        return file.TryGetValue(property, out value);
    }

    /// <summary>
    /// Разобрать путь к ресурсу. Поддерживаются <c>/units/a.toml</c>,
    /// <c>res://resources/units/a.toml</c>, <c>/content/x.vars.toml/radius</c>
    /// и запись с обратными слэшами.
    /// </summary>
    public static bool TryCanonicalPath(string text, out string filePath, out string property)
    {
        filePath = null;
        property = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = text.Trim().Replace('\\', '/');

        if (!LooksLikePath(normalized))
            return false;

        string rest;

        if (normalized.StartsWith(ResourcesRoot, StringComparison.Ordinal))
            rest = normalized[ResourcesRoot.Length..];
        else if (normalized.StartsWith("res://", StringComparison.Ordinal))
            rest = normalized["res://".Length..];
        else if (normalized.StartsWith('/'))
            rest = normalized.TrimStart('/');
        else
            return false;

        int varsProp = IndexOfVarsProperty(rest);

        if (varsProp >= 0)
        {
            filePath = ResourcesRoot + rest[..varsProp];
            property = rest[(varsProp + 1)..];

            if (string.IsNullOrEmpty(property))
                return false;

            return true;
        }

        int tomlProp = rest.IndexOf(".toml/", StringComparison.Ordinal);

        if (tomlProp >= 0)
        {
            filePath = ResourcesRoot + rest[..(tomlProp + ".toml".Length)];
            property = rest[(tomlProp + ".toml/".Length)..];
            return !string.IsNullOrEmpty(property);
        }

        if (rest.EndsWith(".toml", StringComparison.Ordinal))
        {
            filePath = ResourcesRoot + rest;
            property = null;
            return true;
        }

        return false;
    }

    private static int IndexOfVarsProperty(string rest)
    {
        const string mark = ".vars.toml/";
        int at = rest.IndexOf(mark, StringComparison.Ordinal);
        return at < 0 ? -1 : at + ".vars.toml".Length;
    }

    private static bool LooksLikePath(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        string normalized = text.Replace('\\', '/');
        return (normalized.StartsWith('/') || normalized.StartsWith("res://", StringComparison.Ordinal))
               && normalized.Contains(".toml", StringComparison.Ordinal);
    }

    public static bool IsVars(string path) =>
        path.EndsWith(".vars.toml", StringComparison.OrdinalIgnoreCase);

    private static string PopBase(TomlTable table)
    {
        if (!table.TryGetValue("base", out object value))
            return null;

        table.Remove("base");
        return value as string;
    }

    private static TomlTable Merge(TomlTable foundation, TomlTable overlay)
    {
        var result = CloneTable(foundation);

        foreach (var pair in overlay)
        {
            if (pair.Value is TomlTable overlayTable
                && result.TryGetValue(pair.Key, out object existing)
                && existing is TomlTable basisTable)
            {
                result[pair.Key] = Merge(basisTable, overlayTable);
                continue;
            }

            result[pair.Key] = CloneValue(pair.Value);
        }

        return result;
    }

    private static TomlTable CloneTable(TomlTable source)
    {
        var copy = new TomlTable();

        foreach (var pair in source)
            copy[pair.Key] = CloneValue(pair.Value);

        return copy;
    }

    private static object CloneValue(object value) => value switch
    {
        null => null,
        TomlTable table => CloneTable(table),
        TomlArray array => CloneArray(array),
        TomlTableArray tables => CloneTableArray(tables),
        _ => value,
    };

    private static TomlArray CloneArray(TomlArray source)
    {
        var copy = new TomlArray();

        foreach (object item in source)
            copy.Add(CloneValue(item));

        return copy;
    }

    private static TomlTableArray CloneTableArray(TomlTableArray source)
    {
        var copy = new TomlTableArray();

        foreach (TomlTable table in source)
            copy.Add(CloneTable(table));

        return copy;
    }

    private static string Short(string path) =>
        path.StartsWith(ResourcesRoot, StringComparison.Ordinal)
            ? path[ResourcesRoot.Length..]
            : path;

    private static string Where(string path, string section) =>
        string.IsNullOrEmpty(section) ? Short(path) : $"{Short(path)} → [{section}]";
}
