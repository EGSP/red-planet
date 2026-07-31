using System.Collections.Generic;
using Godot;
using Tomlyn;
using Tomlyn.Model;

/// <summary>
/// Чтение строительных панелей из .toml.
///
/// ПОЧЕМУ НЕ РЕСУРС GODOT. Панель правят руками и часто, а её содержимое — сетка ячеек
/// с координатами. В инспекторе такая сетка разваливается на череду безымянных вложенных
/// ресурсов, где не видно ни строк, ни столбцов. Текстовый файл показывает разметку целиком,
/// ложится в diff построчно и допускает комментарии рядом со спорным местом.
///
/// ПОЧЕМУ TOML, А НЕ YAML. Определения правятся точечно, и цена опечатки должна быть
/// местной: в TOML нет значимых отступов, поэтому промах пробелом портит одну строку,
/// а не структуру всего файла. Типы однозначны — постройка с идентификатором вроде "no"
/// или "on" останется строкой, тогда как YAML превратил бы её в логическое значение.
/// Вложенность здесь мелкая, всего два уровня, поэтому главная слабость TOML не проявляется.
///
/// Разбор ручной, а не отображением на классы: файл пишет человек, и внятное сообщение
/// о том, что именно не так и в какой секции, важнее краткости этого кода.
/// </summary>
public static class BuildbarLoader
{
    /// <summary>Разобрать панель. Возвращает null, если файл прочитать не удалось.</summary>
    public static BuildbarDef Load(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);

        if (file == null)
        {
            GD.PushWarning($"[Buildbar] файл не открывается: {path}");
            return null;
        }

        var syntax = Toml.Parse(file.GetAsText(), path);

        if (syntax.HasErrors)
        {
            foreach (var error in syntax.Diagnostics)
                GD.PushError($"[Buildbar] {error}");

            return null;
        }

        return Read(syntax.ToModel(), path);
    }

    private static BuildbarDef Read(TomlTable table, string path)
    {
        var bar = new BuildbarDef
        {
            Id = Text(table, "id", ""),
            Priority = (int)Number(table, "priority", 10),
        };

        if (string.IsNullOrEmpty(bar.Id))
        {
            GD.PushError($"[Buildbar] не задан id: {path}");
            return null;
        }

        if (table["sections"] is not TomlTableArray sections)
        {
            GD.PushError($"[Buildbar] нет ни одной секции: {path}");
            return null;
        }

        foreach (var source in sections)
            if (ReadSection(source, bar.Id, path) is { } section)
                bar.Sections.Add(section);

        return bar;
    }

    private static BuildbarSectionDef ReadSection(TomlTable table, string barId, string path)
    {
        var section = new BuildbarSectionDef
        {
            Id = Text(table, "id", ""),
            Title = Text(table, "title", ""),
            Order = (int)Number(table, "order", 0),
        };

        if (string.IsNullOrEmpty(section.Id))
        {
            GD.PushError($"[Buildbar] секция без id в панели {barId}: {path}");
            return null;
        }

        if (table["cells"] is not TomlArray cells)
            return section;

        foreach (var item in cells)
        {
            if (item is not TomlTable cell)
            {
                GD.PushError($"[Buildbar] ячейка не таблица, секция {section.Id}: {path}");
                continue;
            }

            string buildableId = Text(cell, "build", "");

            if (string.IsNullOrEmpty(buildableId))
            {
                GD.PushError($"[Buildbar] ячейка без build, секция {section.Id}: {path}");
                continue;
            }

            var position = new Vector2I((int)Number(cell, "x", 0), (int)Number(cell, "y", 0));

            if (position.X < 0 || position.Y < 0)
            {
                GD.PushError($"[Buildbar] отрицательная координата у {buildableId}, " +
                             $"секция {section.Id}: {path}");
                continue;
            }

            section.Cells.Add(new BuildbarCell(position, buildableId));
        }

        return section;
    }

    private static string Text(TomlTable table, string key, string fallback) =>
        table.TryGetValue(key, out var value) && value is string text ? text : fallback;

    private static long Number(TomlTable table, string key, long fallback) =>
        table.TryGetValue(key, out var value) && value is long number ? number : fallback;
}
