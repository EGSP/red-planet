using System;
using System.Text.RegularExpressions;

/// <summary>
/// Операции над текстом файла сцены: чтение и замена ссылки на внешний ресурс,
/// назначенный свойству узла.
///
/// ПОЧЕМУ ПРАВКА ТЕКСТОМ. Загрузка <c>PackedScene</c>, создание экземпляра, правка свойства
/// и обратная упаковка перестраивают файл целиком: порядок узлов, идентификаторы внешних
/// ресурсов и форматирование при этом меняются, а сцена партии содержит несколько десятков
/// узлов, к которым редактор контента отношения не имеет. Точечная замена повторяет
/// решение, принятое для TOML: правится ровно то, что правится.
///
/// ПОЧЕМУ БЕЗ ОБРАЩЕНИЙ К ДВИЖКУ. Здесь только строки, поэтому правила проверяются
/// в <see cref="ContentEditorSelfTest"/> без запуска Godot. Чтение файла, запись и получение
/// uid оставлены <see cref="TuningSceneBinding"/>.
///
/// ОПОРА — ИМЯ УЗЛА И ИМЯ СВОЙСТВА. Путь файла опорой служить не может: после переключения
/// на вариант он как раз и меняется. Идентификатор <c>ExtResource</c> тоже не годится —
/// движок вправе перенумеровать внешние ресурсы при сохранении сцены.
/// </summary>
public static class TuningSceneText
{
    /// <summary>
    /// Конец строки заголовка раздела. Возврат каретки допускается явно: файл сцены может
    /// быть сохранён с окончаниями строк Windows, и якорь конца строки иначе не срабатывал бы.
    /// </summary>
    private const string LineEnd = "\r?$";

    /// <summary>
    /// Путь ресурса, назначенного свойству узла, либо <c>null</c>, если назначения нет.
    /// </summary>
    public static string PathOf(string sceneText, string nodeName, string property)
    {
        string id = ReferenceId(sceneText, nodeName, property);
        return id == null ? null : PathOfExtResource(sceneText, id);
    }

    /// <summary>
    /// Назначить свойству узла другой ресурс. Возвращает новый текст сцены либо <c>null</c>,
    /// если назначение не найдено.
    ///
    /// Когда прежний внешний ресурс использован в сцене единственный раз, у него меняется
    /// путь: в файле не должно оставаться ссылки на вариант, которым больше не пользуются.
    /// Если тот же ресурс назначен ещё где-то, добавляется отдельная запись, а меняется
    /// только строка нужного узла.
    /// </summary>
    public static string Rebind(
        string sceneText, string nodeName, string property, string newPath, string uid)
    {
        string id = ReferenceId(sceneText, nodeName, property);
        if (id == null || string.IsNullOrEmpty(newPath))
            return null;

        return UsageCount(sceneText, id) > 1
            ? WithNewReference(sceneText, nodeName, property, id, newPath, uid)
            : WithReplacedPath(sceneText, id, newPath, uid);
    }

    /// <summary>Идентификатор внешнего ресурса, присвоенного свойству узла.</summary>
    private static string ReferenceId(string sceneText, string nodeName, string property)
    {
        string block = NodeBlock(sceneText, nodeName, out _, out _);
        if (block == null)
            return null;

        var match = Regex.Match(
            block,
            $"^{Regex.Escape(property)}\\s*=\\s*ExtResource\\(\"([^\"]+)\"\\)",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Свойства узла: от его заголовка до заголовка следующего раздела. Границу приходится
    /// искать явно, иначе одноимённое свойство соседнего узла увело бы поиск.
    /// </summary>
    private static string NodeBlock(string sceneText, string nodeName, out int start, out int end)
    {
        start = -1;
        end = -1;

        var node = Regex.Match(
            sceneText,
            $"^\\[node name=\"{Regex.Escape(nodeName)}\"[^\\]]*\\]{LineEnd}",
            RegexOptions.Multiline);
        if (!node.Success)
            return null;

        start = node.Index + node.Length;
        end = sceneText.IndexOf("\n[", start, StringComparison.Ordinal);
        return end >= 0 ? sceneText[start..end] : sceneText[start..];
    }

    private static string PathOfExtResource(string sceneText, string id)
    {
        var line = ExtResourceLine(sceneText, id);
        if (!line.Success)
            return null;

        var path = Regex.Match(line.Value, "path=\"([^\"]+)\"");
        return path.Success ? path.Groups[1].Value : null;
    }

    private static Match ExtResourceLine(string sceneText, string id) =>
        Regex.Match(
            sceneText,
            $"^\\[ext_resource [^\\]]*id=\"{Regex.Escape(id)}\"[^\\]]*\\]{LineEnd}",
            RegexOptions.Multiline);

    /// <summary>Сколько раз внешний ресурс упомянут в сцене.</summary>
    private static int UsageCount(string sceneText, string id) =>
        Regex.Matches(sceneText, $"ExtResource\\(\"{Regex.Escape(id)}\"\\)").Count;

    /// <summary>Заменить путь существующей записи внешнего ресурса.</summary>
    private static string WithReplacedPath(string sceneText, string id, string newPath, string uid)
    {
        var match = ExtResourceLine(sceneText, id);
        if (!match.Success)
            return null;

        string line = Regex.Replace(match.Value, "path=\"[^\"]*\"", $"path=\"{newPath}\"");
        line = WithUid(line, uid);
        return sceneText[..match.Index] + line + sceneText[(match.Index + match.Length)..];
    }

    /// <summary>
    /// Добавить отдельную запись внешнего ресурса и перевести на неё свойство узла.
    /// Нужно тогда, когда прежний ресурс назначен ещё кому-то и трогать его нельзя.
    /// </summary>
    private static string WithNewReference(
        string sceneText, string nodeName, string property, string oldId, string newPath, string uid)
    {
        var lines = Regex.Matches(
            sceneText, $"^\\[ext_resource [^\\]]*\\]{LineEnd}", RegexOptions.Multiline);
        if (lines.Count == 0)
            return null;

        string id = $"tuning_{Math.Abs(newPath.GetHashCode()) % 1000000}";
        string uidPart = string.IsNullOrEmpty(uid) ? "" : $" uid=\"{uid}\"";
        string entry = $"[ext_resource type=\"Resource\"{uidPart} path=\"{newPath}\" id=\"{id}\"]";

        var anchor = lines[^1];
        int insertAt = anchor.Index + anchor.Length;
        string withResource = sceneText[..insertAt] + "\n" + entry + sceneText[insertAt..];

        string block = NodeBlock(withResource, nodeName, out int start, out int end);
        if (block == null)
            return null;

        // Заменяется только присваивание нужного узла: то же присваивание у другого узла
        // относится к его собственной настройке.
        string patched = Regex.Replace(
            block,
            $"^{Regex.Escape(property)}\\s*=\\s*ExtResource\\(\"{Regex.Escape(oldId)}\"\\)",
            $"{property} = ExtResource(\"{id}\")",
            RegexOptions.Multiline);

        return end >= 0
            ? withResource[..start] + patched + withResource[end..]
            : withResource[..start] + patched;
    }

    /// <summary>
    /// Подставить в строку записи новый uid либо убрать его вовсе: ссылка по пути остаётся
    /// действительной, а прежний uid увёл бы движок к прежнему файлу.
    /// </summary>
    private static string WithUid(string line, string uid)
    {
        bool hasUid = Regex.IsMatch(line, "uid=\"[^\"]*\"");

        if (string.IsNullOrEmpty(uid))
            return hasUid ? Regex.Replace(line, " uid=\"[^\"]*\"", "") : line;

        return hasUid
            ? Regex.Replace(line, "uid=\"[^\"]*\"", $"uid=\"{uid}\"")
            : line.Replace("[ext_resource ", $"[ext_resource uid=\"{uid}\" ");
    }
}
