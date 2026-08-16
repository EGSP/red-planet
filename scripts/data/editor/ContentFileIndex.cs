using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Слой файлов редактора: какие <c>.toml</c> относятся к содержимому, их тексты, метки
/// времени и перечень записей каталога.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ Store. Store отвечает за черновики, сборку и оповещение интерфейса.
/// Обход каталогов, чтение файла и опознание вида записи от этого не зависят вовсе и
/// проверяются в отрыве от Godot-сигналов. Разделение к тому же убирает соблазн читать
/// файл посреди правки черновика: обращение к диску собрано в одном месте.
/// </summary>
public sealed class ContentFileIndex
{
    /// <summary>Корни, в которых лежит содержимое. Порядок значения не имеет.</summary>
    private static readonly string[] Roots =
    {
        "res://resources/content/",
        "res://resources/tools/",
        "res://resources/units/",
        "res://resources/buildings/",
        "res://resources/waves/",
    };

    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _timestamps = new(StringComparer.Ordinal);
    private readonly List<ContentEditorEntry> _entries = new();

    /// <summary>Последний известный текст каждого файла: снимок диска, а не черновик.</summary>
    public IReadOnlyDictionary<string, string> Texts => _texts;

    /// <summary>Записи каталога, опознанные по <c>id</c> в тексте файла.</summary>
    public IReadOnlyList<ContentEditorEntry> Entries => _entries;

    /// <summary>Полная перечитка всех файлов содержимого.</summary>
    public void ReloadAll()
    {
        _texts.Clear();
        _timestamps.Clear();
        TomlText.ClearCache();

        foreach (string path in EnumerateToml())
        {
            string text = ReadText(path);
            if (text == null)
                continue;

            _texts[path] = text;
            _timestamps[path] = Stamp(path);
        }

        RebuildEntries(_texts);
    }

    /// <summary>Известен ли файл индексу.</summary>
    public bool Has(string path) => _texts.ContainsKey(path);

    /// <summary>Последний известный текст файла либо <c>null</c>.</summary>
    public string Text(string path) => _texts.GetValueOrDefault(path);

    /// <summary>Известная метка времени файла; ноль означает «неизвестна».</summary>
    public ulong KnownStamp(string path) => _timestamps.GetValueOrDefault(path);

    /// <summary>Запомнить текст и метку времени файла как последнее известное состояние.</summary>
    public void Remember(string path, string text, ulong stamp)
    {
        _texts[path] = text;
        _timestamps[path] = stamp;
    }

    /// <summary>Забыть файл: он удалён с диска и открытой вкладки у него нет.</summary>
    public void Forget(string path)
    {
        _texts.Remove(path);
        _timestamps.Remove(path);
        _entries.RemoveAll(entry => entry.Path == path);
    }

    /// <summary>Запись каталога по идентификатору.</summary>
    public ContentEditorEntry ById(string id) =>
        _entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));

    /// <summary>Запись каталога по пути файла.</summary>
    public ContentEditorEntry ByPath(string path) =>
        _entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.Ordinal));

    /// <summary>
    /// Пересобрать перечень записей по переданным текстам. Тексты передаются снаружи,
    /// поскольку в сборке участвуют черновики открытых вкладок, а не только снимок диска:
    /// <c>id</c>, <c>name</c> и <c>abstract</c> правятся в форме и должны отражаться
    /// в каталоге немедленно.
    /// </summary>
    public void RebuildEntries(IReadOnlyDictionary<string, string> texts)
    {
        _entries.Clear();
        foreach (var pair in texts)
        {
            if (TryReadEntry(pair.Key, pair.Value) is { } entry)
                _entries.Add(entry);
        }
    }

    /// <summary>
    /// Уточнить вид записей по собранному каталогу. Локальный <c>kind</c> может находиться
    /// в родительском файле, поэтому окончательный вид известен только после компиляции;
    /// это существенно для оружия и рабочих инструментов.
    /// </summary>
    public void Reclassify(Catalog catalog)
    {
        if (catalog == null)
            return;

        foreach (var entry in _entries)
        {
            if (entry.Kind == ContentEntityKind.Wave)
                continue;

            if (catalog.Tool(entry.Id) is WeaponDefinition)
                entry.Kind = ContentEntityKind.Weapon;
            else if (catalog.Tool(entry.Id) is WorkToolDefinition)
                entry.Kind = ContentEntityKind.WorkTool;
            else if (catalog.Unit(entry.Id) is { } unit)
                entry.Kind = unit.IsStructure
                    ? ContentEntityKind.Building
                    : ContentEntityKind.Unit;
        }
    }

    /// <summary>Пути всех файлов содержимого, существующих на диске сейчас.</summary>
    public static IEnumerable<string> EnumerateToml()
    {
        foreach (string root in Roots)
        foreach (string path in Files(root))
            yield return path;
    }

    /// <summary>Текст файла либо <c>null</c>, если файл недоступен.</summary>
    public static string ReadText(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    /// <summary>
    /// Метка времени файла. Ноль означает, что файла нет: это отдельное внешнее изменение,
    /// а не отсутствие изменений.
    /// </summary>
    public static ulong Stamp(string path)
    {
        try
        {
            string absolute = ProjectSettings.GlobalizePath(path);
            return File.Exists(absolute)
                ? (ulong)File.GetLastWriteTimeUtc(absolute).Ticks
                : 0UL;
        }
        catch (Exception)
        {
            // Файл может быть занят внешним редактором в момент сохранения.
            return 0UL;
        }
    }

    /// <summary>
    /// Опознать запись каталога по тексту файла. Файл без <c>id</c> записью не является:
    /// таковы <c>*.vars.toml</c> и общие таблицы.
    /// </summary>
    private static ContentEditorEntry TryReadEntry(string path, string text)
    {
        var table = TomlText.Parse(text);
        if (table == null)
            return null;

        if (!table.TryGetValue("id", out object idValue) || idValue is not string id || id.Length == 0)
            return null;

        return new ContentEditorEntry
        {
            Id = id,
            Path = path,
            FileName = TomlText.FileName(path),
            DisplayName = table.TryGetValue("name", out object name) && name is string title ? title : id,
            Kind = KindOf(path, table),
            Abstract = table.TryGetValue("abstract", out object flag) && flag is true,
        };
    }

    /// <summary>
    /// Вид записи до компиляции. Волна опознаётся по расположению файла: ключа <c>kind</c>
    /// у неё нет, а класс сущности к ней неприменим.
    /// </summary>
    private static ContentEntityKind KindOf(string path, Tomlyn.Model.TomlTable table)
    {
        if (IsWavePath(path))
            return ContentEntityKind.Wave;

        if (table.TryGetValue("kind", out object kindValue) && kindValue is string kind)
            return kind == "weapon" ? ContentEntityKind.Weapon : ContentEntityKind.WorkTool;

        return path.Contains("/buildings/", StringComparison.Ordinal)
            ? ContentEntityKind.Building
            : ContentEntityKind.Unit;
    }

    private static bool IsWavePath(string path) =>
        path != null && path.Contains("/waves/", StringComparison.Ordinal);

    private static IEnumerable<string> Files(string dir)
    {
        using var access = DirAccess.Open(dir);
        if (access == null)
            yield break;

        foreach (string file in access.GetFiles())
        {
            if (file.EndsWith(".toml", StringComparison.Ordinal))
                yield return dir + file;
        }

        foreach (string sub in access.GetDirectories())
        {
            // Служебные каталоги вида .godot содержимым игры не являются.
            if (sub.StartsWith('.'))
                continue;

            foreach (string path in Files(dir + sub + "/"))
                yield return path;
        }
    }
}
