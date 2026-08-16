using System;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

/// <summary>
/// Работа с файлом сцены на диске: чтение назначенного ресурса и запись нового назначения.
///
/// Правила разбора и замены живут в <see cref="TuningSceneText"/> и проверяются без
/// запуска движка; здесь остаётся обращение к диску, получение uid сохранённого ресурса
/// и сообщения об отказах.
/// </summary>
public static class TuningSceneBinding
{
    /// <summary>
    /// Путь ресурса, назначенного узлу в сцене. Возвращает <c>null</c>, если сцена
    /// недоступна либо назначение в ней не найдено.
    /// </summary>
    public static string ResourcePathOf(TuningResourceSpec spec)
    {
        if (spec == null || ReadScene(spec.ScenePath) is not { } text)
            return null;

        return TuningSceneText.PathOf(text, spec.NodeName, spec.PropertyName);
    }

    /// <summary>
    /// Назначить узлу другой ресурс. Возвращает описание отказа либо <c>null</c> при успехе.
    /// </summary>
    public static string Rebind(TuningResourceSpec spec, string newResourcePath)
    {
        if (spec == null || string.IsNullOrEmpty(newResourcePath))
            return "no resource to bind";

        if (ReadScene(spec.ScenePath) is not { } text)
            return $"scene {spec.ScenePath} was not read";

        string updated = TuningSceneText.Rebind(
            text, spec.NodeName, spec.PropertyName, newResourcePath, UidOf(newResourcePath));
        if (updated == null)
            return $"property {spec.PropertyName} of node {spec.NodeName} was not found";

        try
        {
            // Запись через временный файл: прерывание не должно оставить сцену
            // наполовину переписанной.
            string absolute = ProjectSettings.GlobalizePath(spec.ScenePath);
            string temp = absolute + ".tmp";
            File.WriteAllText(temp, updated);
            File.Move(temp, absolute, overwrite: true);
        }
        catch (Exception ex)
        {
            return $"scene write failed: {ex.Message}";
        }

        return null;
    }

    /// <summary>
    /// Uid сохранённого ресурса. Читается из заголовка самого файла: движок присваивает его
    /// при записи, а обращение к базе uid до повторного обхода файловой системы вернуло бы
    /// пустое значение.
    /// </summary>
    private static string UidOf(string resourcePath)
    {
        try
        {
            string absolute = ProjectSettings.GlobalizePath(resourcePath);
            if (!File.Exists(absolute))
                return null;

            using var reader = new StreamReader(absolute);
            var uid = Regex.Match(reader.ReadLine() ?? "", "uid=\"(uid://[^\"]+)\"");
            return uid.Success ? uid.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ReadScene(string path)
    {
        try
        {
            string absolute = ProjectSettings.GlobalizePath(path);
            return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] scene {path} was not read: {ex.Message}");
            return null;
        }
    }
}
