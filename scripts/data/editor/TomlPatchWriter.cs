using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Точечная правка текста TOML с сохранением комментариев и порядка прочих строк.
///
/// ПОЧЕМУ НЕ Toml.FromModel. Tomlyn 0.17 при сериализации таблицы теряет комментарии
/// и переставляет ключи. Файлы контента содержат пояснения к балансу и PA-ссылки —
/// их нельзя выкидывать при каждом «Применить». Поэтому правки — строковые: найти
/// строку ключа, заменить значение, при отсутствии ключа вставить строку в секцию.
///
/// СЕКЦИИ. section = null или "" означает корень файла (id, name, class…). Иначе
/// ищется заголовок [section]. Пустая секция после RemoveKey удаляется целиком.
/// </summary>
public static class TomlPatchWriter
{
    private static readonly Regex KeyLine = new(
        @"^(?<indent>\s*)(?<key>[A-Za-z0-9_\-]+)\s*=\s*(?<value>.+?)(?<trail>\s*(#.*)?)?$",
        RegexOptions.Compiled);

    private static readonly Regex SectionHeader = new(
        @"^\s*\[(?<name>[^\]]+)\]\s*(#.*)?$",
        RegexOptions.Compiled);

    /// <summary>Записать или обновить ключ. section = null или пусто — корень файла.</summary>
    public static string SetKey(string source, string section, string key, string valueToml)
    {
        source ??= "";
        var lines = SplitLines(source);
        FindKey(lines, section, key, out int keyIndex, out int sectionStart, out int sectionEnd);

        string replacement = FormatAssignment(key, valueToml);

        if (keyIndex >= 0)
        {
            var match = KeyLine.Match(lines[keyIndex]);
            string indent = match.Success ? match.Groups["indent"].Value : "";
            string trail = match.Success ? match.Groups["trail"].Value : "";
            lines[keyIndex] = indent + $"{key} = {valueToml}" + trail;
            return JoinLines(lines, EndsWithNewline(source));
        }

        if (string.IsNullOrEmpty(section))
        {
            int insertAt = FirstRootInsertIndex(lines);
            lines.Insert(insertAt, replacement);
            return JoinLines(lines, EndsWithNewline(source));
        }

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");

            lines.Add($"[{section}]");
            lines.Add(replacement);
            return JoinLines(lines, EndsWithNewline(source));
        }

        int at = sectionEnd;
        while (at > sectionStart + 1 && string.IsNullOrWhiteSpace(lines[at - 1]))
            at--;

        lines.Insert(at, replacement);
        return JoinLines(lines, EndsWithNewline(source));
    }

    /// <summary>Удалить ключ. Пустая секция после удаления тоже снимается.</summary>
    public static string RemoveKey(string source, string section, string key)
    {
        source ??= "";
        var lines = SplitLines(source);
        FindKey(lines, section, key, out int keyIndex, out int sectionStart, out int sectionEnd);

        if (keyIndex < 0)
            return source;

        lines.RemoveAt(keyIndex);
        sectionEnd--;

        if (!string.IsNullOrEmpty(section) && sectionStart >= 0)
        {
            bool hasKeys = false;

            for (int i = sectionStart + 1; i < sectionEnd && i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                if (SectionHeader.IsMatch(lines[i]))
                    break;

                if (KeyLine.IsMatch(lines[i]))
                {
                    hasKeys = true;
                    break;
                }
            }

            if (!hasKeys)
            {
                int removeFrom = sectionStart;
                int removeTo = sectionStart + 1;

                while (removeTo < lines.Count &&
                       (string.IsNullOrWhiteSpace(lines[removeTo]) ||
                        lines[removeTo].TrimStart().StartsWith('#')))
                {
                    if (SectionHeader.IsMatch(lines[removeTo]))
                        break;

                    if (KeyLine.IsMatch(lines[removeTo]))
                        break;

                    removeTo++;
                }

                // Заголовок секции и следующие пустые/комментарии до следующего содержимого
                lines.RemoveRange(removeFrom, Math.Min(removeTo, lines.Count) - removeFrom);

                while (removeFrom < lines.Count && string.IsNullOrWhiteSpace(lines[removeFrom]))
                    lines.RemoveAt(removeFrom);

                if (removeFrom > 0 && removeFrom <= lines.Count &&
                    removeFrom - 1 < lines.Count &&
                    string.IsNullOrWhiteSpace(lines[removeFrom - 1]) &&
                    (removeFrom >= lines.Count || string.IsNullOrWhiteSpace(lines[removeFrom])))
                {
                    // оставляем один разделитель при необходимости
                }
            }
        }

        return JoinLines(lines, EndsWithNewline(source));
    }

    public static string FormatBool(bool value) => value ? "true" : "false";

    public static string FormatFloat(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    public static string FormatString(string value) =>
        "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string FormatStringList(IReadOnlyList<string> values)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(FormatString(values[i]));
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static string FormatFloatList(IReadOnlyList<float> values)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(FormatFloat(values[i]));
        }

        sb.Append(']');
        return sb.ToString();
    }

    public static string FormatColor(float r, float g, float b, float a = 1f)
    {
        if (Math.Abs(a - 1f) < 0.0001f)
            return $"[{FormatFloat(r)}, {FormatFloat(g)}, {FormatFloat(b)}]";

        return $"[{FormatFloat(r)}, {FormatFloat(g)}, {FormatFloat(b)}, {FormatFloat(a)}]";
    }

    public static string FormatEnum(string value)
    {
        string snake = ToSnake(value);
        return FormatString(snake);
    }

    public static string ToSnake(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static string FormatAssignment(string key, string valueToml) => $"{key} = {valueToml}";

    private static void FindKey(
        List<string> lines, string section, string key,
        out int keyIndex, out int sectionStart, out int sectionEnd)
    {
        keyIndex = -1;
        sectionStart = -1;
        sectionEnd = lines.Count;

        bool root = string.IsNullOrEmpty(section);
        bool inTarget = root;
        int currentSectionStart = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            var header = SectionHeader.Match(lines[i]);
            if (header.Success)
            {
                string name = header.Groups["name"].Value.Trim();
                if (inTarget && !root)
                {
                    sectionEnd = i;
                    break;
                }

                if (!root && string.Equals(name, section, StringComparison.Ordinal))
                {
                    inTarget = true;
                    sectionStart = i;
                    currentSectionStart = i;
                    continue;
                }

                if (root)
                {
                    // корень заканчивается на первой секции
                    sectionEnd = i;
                    break;
                }

                inTarget = false;
                continue;
            }

            if (!inTarget)
                continue;

            var match = KeyLine.Match(lines[i]);
            if (!match.Success)
                continue;

            if (string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
            {
                keyIndex = i;
                if (!root)
                    sectionStart = currentSectionStart;
                return;
            }
        }

        if (!root && sectionStart >= 0)
            sectionEnd = Math.Max(sectionStart + 1, sectionEnd);
        else if (root)
            sectionStart = -1;
    }

    private static int FirstRootInsertIndex(List<string> lines)
    {
        int lastRootKey = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            if (SectionHeader.IsMatch(lines[i]))
                return lastRootKey >= 0 ? lastRootKey + 1 : i;

            if (KeyLine.IsMatch(lines[i]))
                lastRootKey = i;
        }

        return lastRootKey >= 0 ? lastRootKey + 1 : lines.Count;
    }

    private static List<string> SplitLines(string source)
    {
        var lines = new List<string>();
        if (source.Length == 0)
            return lines;

        int start = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n')
                continue;

            int length = i - start;
            if (length > 0 && source[i - 1] == '\r')
                length--;

            lines.Add(source.Substring(start, length));
            start = i + 1;
        }

        if (start < source.Length || source.EndsWith('\n'))
        {
            if (start < source.Length)
            {
                string last = source[start..];
                if (last.EndsWith('\r'))
                    last = last[..^1];
                lines.Add(last);
            }
        }

        return lines;
    }

    private static bool EndsWithNewline(string source) =>
        source.EndsWith('\n') || source.EndsWith("\r\n", StringComparison.Ordinal);

    private static string JoinLines(List<string> lines, bool trailingNewline)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            sb.Append(lines[i]);
        }

        if (trailingNewline && (lines.Count == 0 || sb.Length == 0 || sb[^1] != '\n'))
            sb.Append('\n');

        return sb.ToString();
    }
}
