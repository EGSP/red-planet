using System;
using System.Collections.Generic;
using Tomlyn.Model;

/// <summary>
/// Определение того, откуда пришло значение поля: из самого файла, из цепочки <c>base</c>,
/// из файла переменных либо из умолчания кода.
///
/// ПОЧЕМУ ПОИСК ИДЁТ ПО СЫРЫМ ТЕКСТАМ, А НЕ ПО РАЗРЕШЁННОЙ ТАБЛИЦЕ. После
/// <see cref="TomlResolver.Resolve"/> ссылка на <c>*.vars.toml</c> уже заменена итоговым
/// числом, и отличить значение из файла переменных от обычного унаследованного числа
/// по результату невозможно. Поэтому цепочка обходится по исходным текстам, а найденный
/// ключ возвращается вместе с путём того файла, где он записан.
/// </summary>
public static class ContentProvenance
{
    /// <summary>
    /// Происхождение значения ключа. <paramref name="texts"/> должен уже содержать
    /// черновик правимого файла: подстановку выполняет вызывающая сторона.
    /// </summary>
    public static FieldSourceInfo Of(
        string path,
        string draftText,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts)
    {
        var draft = TomlText.Parse(draftText);
        if (TomlText.TryGetValue(draft, section, key, out object localValue))
        {
            // Локальный ключ может сам быть ссылкой на файл переменных: тогда источником
            // числа является этот файл, а не правимый.
            return TryVarsTarget(localValue, out string localVars)
                ? new FieldSourceInfo { Provenance = FieldProvenance.Vars, Path = localVars }
                : new FieldSourceInfo { Provenance = FieldProvenance.Local, Path = path };
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return InFoundation(path, section, key, texts, visited)
               ?? new FieldSourceInfo { Provenance = FieldProvenance.Default };
    }

    /// <summary>
    /// Совпадает ли унаследованное значение с кандидатом на запись. Совпадение означает,
    /// что локальный ключ не нужен: файл не должен содержать копию значения родителя.
    /// </summary>
    public static bool InheritedEquals(
        string path,
        string section,
        string key,
        string encoded,
        IReadOnlyDictionary<string, string> texts)
    {
        if (!TryResolve(path, section, key, texts, out object inherited))
            return false;

        return ContentValueReader.SameToml(ContentValueReader.EncodeRaw(inherited), encoded);
    }

    /// <summary>
    /// Переход от правимого файла к его основанию: сначала <c>base</c> самой секции,
    /// затем корневой <c>base</c>. Собственные ключи файла здесь уже проверены.
    /// </summary>
    private static FieldSourceInfo InFoundation(
        string path,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts,
        HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out string text))
            return null;

        var table = TomlText.Parse(text);
        if (table == null)
            return null;

        if (TomlText.SectionOf(table, section) is { } localSection
            && localSection.TryGetValue("base", out object sectionBase)
            && sectionBase is string sectionBasePath
            && TomlResolver.TryCanonicalPath(sectionBasePath, out string sectionTarget, out _))
        {
            return InChain(sectionTarget, section, key, texts, visited);
        }

        if (table.TryGetValue("base", out object rootBase)
            && rootBase is string rootBasePath
            && TomlResolver.TryCanonicalPath(rootBasePath, out string basePath, out _))
        {
            return InChain(basePath, section, key, texts, visited);
        }

        return null;
    }

    /// <summary>Поиск ключа в файле-основании и дальше по его собственной цепочке.</summary>
    private static FieldSourceInfo InChain(
        string path,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts,
        HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out string text))
            return null;

        var table = TomlText.Parse(text);
        if (table == null)
            return null;

        // Секционный base на файл переменных использует его корневые ключи.
        if (TomlResolver.IsVars(path) && table.ContainsKey(key))
            return new FieldSourceInfo { Provenance = FieldProvenance.Vars, Path = path };

        if (string.IsNullOrEmpty(section))
        {
            if (table.TryGetValue(key, out object rootValue))
                return At(path, rootValue);
        }
        else if (TomlText.SectionOf(table, section) is { } sectionTable)
        {
            if (sectionTable.TryGetValue(key, out object value))
                return At(path, value);

            if (sectionTable.TryGetValue("base", out object sectionBase)
                && sectionBase is string sectionBasePath
                && TomlResolver.TryCanonicalPath(sectionBasePath, out string sectionTarget, out _))
            {
                return InChain(sectionTarget, section, key, texts, visited);
            }
        }

        if (table.TryGetValue("base", out object rootBase)
            && rootBase is string rootBasePath
            && TomlResolver.TryCanonicalPath(rootBasePath, out string basePath, out _))
        {
            return InChain(basePath, section, key, texts, visited);
        }

        return null;
    }

    private static FieldSourceInfo At(string definingPath, object value) =>
        TryVarsTarget(value, out string varsPath)
            ? new FieldSourceInfo { Provenance = FieldProvenance.Vars, Path = varsPath }
            : new FieldSourceInfo { Provenance = FieldProvenance.Base, Path = definingPath };

    /// <summary>Является ли значение ссылкой на файл переменных.</summary>
    private static bool TryVarsTarget(object value, out string path)
    {
        path = null;
        return value is string text
               && text.Contains(".vars.toml", StringComparison.Ordinal)
               && TomlResolver.TryCanonicalPath(text, out path, out _);
    }

    /// <summary>
    /// Разрешить значение ключа полностью, со всеми наследованиями и подстановками.
    /// Ошибка разрешения означает «сравнить не с чем»: локальный ключ тогда записывается.
    /// </summary>
    private static bool TryResolve(
        string path,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts,
        out object value)
    {
        value = null;

        var raw = new Dictionary<string, TomlTable>(StringComparer.Ordinal);
        foreach (var pair in texts)
        {
            if (TomlText.Parse(pair.Value) is { } table)
                raw[pair.Key] = table;
        }

        if (TomlResolver.Resolve(raw, out var resolved) > 0 || !resolved.TryGetValue(path, out var doc))
            return false;

        return TomlText.TryGetValue(doc, section, key, out value);
    }
}
