using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// Пояснения к полям настроечного ресурса, взятые из комментариев его исходника.
///
/// ЗАЧЕМ ЭТО НУЖНО. Имя свойства вида <c>ProductionTail</c> не объясняет ни смысла, ни
/// единиц измерения, а именно это и требуется при подборе баланса. Развёрнутые пояснения
/// в проекте уже написаны — они стоят XML-комментариями над каждым <c>[Export]</c>, — но
/// в собранной сборке их нет: компилятор C# сохраняет их только в отдельный XML-файл
/// документации, который проектом не собирается.
///
/// ПОЧЕМУ ЧТЕНИЕ ИСХОДНИКА ДОПУСТИМО. Класс работает лишь в редакторе Godot, где исходники
/// заведомо лежат рядом; отсутствие файла или неожиданное его оформление означает всего
/// лишь подпись без пояснения, а не отказ.
/// </summary>
public static class TuningDocReader
{
    /// <summary>Разобранные пояснения по пути исходника: разбор нужен один раз на файл.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Cache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Пояснения к полям ресурса: имя свойства → текст. Отсутствующее пояснение означает
    /// пустую строку у вызывающей стороны, а не исключение.
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(Resource resource)
    {
        string path = ScriptPathOf(resource);
        if (path == null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        if (Cache.TryGetValue(path, out var cached))
            return cached;

        var parsed = Parse(path);
        Cache[path] = parsed;
        return parsed;
    }

    /// <summary>Забыть разобранное: исходник мог измениться вместе с пересборкой.</summary>
    public static void ClearCache() => Cache.Clear();

    private static string ScriptPathOf(Resource resource)
    {
        var script = resource?.GetScript().As<Script>();
        string path = script?.ResourcePath;
        return string.IsNullOrEmpty(path) ? null : path;
    }

    /// <summary>
    /// Прочитать соответствие «свойство → пояснение». Разбор построчный: накапливаются
    /// строки <c>///</c>, и при встрече объявления поля накопленное приписывается ему.
    /// Всякая иная строка накопленное сбрасывает, поэтому комментарий, оторванный от
    /// объявления, к нему не приписывается.
    /// </summary>
    private static Dictionary<string, string> Parse(string scriptPath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string absolute = ProjectSettings.GlobalizePath(scriptPath);
            if (!File.Exists(absolute))
                return result;

            var doc = new StringBuilder();
            foreach (string raw in File.ReadLines(absolute))
            {
                string line = raw.Trim();

                if (line.StartsWith("///", StringComparison.Ordinal))
                {
                    AppendDocLine(doc, line);
                    continue;
                }

                if (line.Length == 0 || line.StartsWith("[", StringComparison.Ordinal))
                    continue;

                var field = Regex.Match(line, @"^public\s+[\w<>\?\[\]\.]+\s+(\w+)");
                if (field.Success && doc.Length > 0)
                    result[field.Groups[1].Value] = doc.ToString().Trim();

                doc.Clear();
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] hints of {scriptPath} were not read: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Добавить строку комментария к накопленному тексту. Разметка XML снимается: подсказка
    /// показывается обычным текстом, и теги в ней читались бы как опечатка.
    /// </summary>
    private static void AppendDocLine(StringBuilder doc, string line)
    {
        string text = line[3..].Trim();
        if (text.StartsWith("<summary>", StringComparison.Ordinal))
            text = text["<summary>".Length..].Trim();
        if (text.EndsWith("</summary>", StringComparison.Ordinal))
            text = text[..^"</summary>".Length].Trim();
        if (text is "<summary>" or "</summary>")
            return;

        text = Regex.Replace(text, "<see cref=\"([^\"]+)\"\\s*/>", "$1");
        text = Regex.Replace(text, "</?c>", "");
        text = Regex.Replace(text, "<[^>]+>", "");
        if (text.Length == 0)
        {
            // Пустая строка комментария разделяет абзацы: она сохраняется, поскольку
            // в этих пояснениях абзац отделяет вывод от изложения.
            if (doc.Length > 0)
                doc.Append("\n\n");
            return;
        }

        if (doc.Length > 0 && !doc.ToString().EndsWith("\n", StringComparison.Ordinal))
            doc.Append(' ');

        doc.Append(text);
    }
}
