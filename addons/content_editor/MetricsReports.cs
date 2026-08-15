using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// Действия редактора над отчётами партий: открыть каталог и преобразовать отчёты в книги
/// Excel с диаграммами.
///
/// ПОЧЕМУ ЭТО ЖИВЁТ В РЕДАКТОРЕ, А НЕ В ИГРЕ. Отчёты нужны при разборе партии, то есть
/// между запусками, и обращаются к ним теми же средствами, что и к прочим файлам проекта.
/// В самой игре кнопка была бы бесполезна: партия ещё идёт, отчёт не закрыт, а преобразование
/// требует внешнего исполнителя.
///
/// ПРЕОБРАЗОВАНИЕ ВЫПОЛНЯЕТ ВНЕШНИЙ СКРИПТ <c>tools/metrics_to_xlsx.py</c>. Повторять
/// на C# запись книги Excel с диаграммами значило бы завести зависимость от библиотеки
/// формата ради задачи, которая исполняется вручную и вне игрового кадра.
/// </summary>
[Tool]
public static class MetricsReports
{
    private const string UserDirectory = "user://metrics";

    private const string ScriptPath = "res://tools/metrics_to_xlsx.py";

    /// <summary>
    /// Чем пробовать запускать скрипт. На Windows обычно есть только один из двух, и какой
    /// именно — заранее неизвестно; порядок перебора и есть весь способ выбора.
    /// </summary>
    private static readonly string[] Interpreters = { "python", "py", "python3" };

    /// <summary>Каталог отчётов в файловой системе операционной системы.</summary>
    public static string Directory => ProjectSettings.GlobalizePath(UserDirectory);

    /// <summary>
    /// Открыть каталог отчётов средствами системы. Каталог создаётся, если игра ещё
    /// не запускалась: открывать несуществующий путь система отказывается, и пользователь
    /// получил бы отказ вместо ответа «отчётов пока нет».
    /// </summary>
    public static string OpenFolder()
    {
        DirAccess.MakeDirRecursiveAbsolute(UserDirectory);

        string path = Directory;
        OS.ShellOpen($"file://{path}");

        int reports = System.IO.Directory.GetFiles(path, "session-*.jsonl").Length;
        return reports > 0 ? $"metrics folder opened: {reports} report(s)" : "metrics folder is empty";
    }

    /// <summary>
    /// Преобразовать отчёты каталога в книги Excel. Отчёт, у которого книга новее его самого,
    /// пропускается: повторное нажатие не должно переделывать то, что уже сделано, а отчёт
    /// закрытой партии больше не меняется.
    /// </summary>
    public static string Convert()
    {
        string path = Directory;

        if (!System.IO.Directory.Exists(path))
            return "no reports: the game has not been run yet";

        string script = ProjectSettings.GlobalizePath(ScriptPath);

        if (!File.Exists(script))
            return $"converter not found: {ScriptPath}";

        var pending = new List<string>();

        foreach (string report in System.IO.Directory.GetFiles(path, "session-*.jsonl"))
        {
            string book = Path.ChangeExtension(report, ".xlsx");

            if (File.Exists(book) && File.GetLastWriteTimeUtc(book) >= File.GetLastWriteTimeUtc(report))
                continue;

            pending.Add(report);
        }

        if (pending.Count == 0)
            return "all reports are already converted";

        int converted = 0;
        string failure = null;

        foreach (string report in pending)
        {
            if (Run(script, report, out string error))
            {
                converted++;
                continue;
            }

            failure ??= error;
        }

        if (converted == 0)
            return $"conversion failed: {failure}";

        string result = $"converted {converted} of {pending.Count} report(s)";
        return failure == null ? result : $"{result}; first error: {failure}";
    }

    /// <summary>
    /// Запуск скрипта. Перебор исполнителей прекращается на первом, который вообще запустился:
    /// ненулевой код возврата означает уже ошибку самого преобразования — например, отсутствие
    /// openpyxl, — и пробовать другой исполнитель после этого незачем.
    /// </summary>
    private static bool Run(string script, string report, out string error)
    {
        error = null;
        var output = new Godot.Collections.Array();

        foreach (string interpreter in Interpreters)
        {
            output.Clear();

            int code = OS.Execute(interpreter, new[] { script, report }, output, true);

            if (code < 0)
            {
                // Исполнитель не запустился вовсе — пробуем следующее имя
                error ??= $"{interpreter} not found";
                continue;
            }

            if (code == 0)
                return true;

            error = Tail(output);
            return false;
        }

        error = "python not found in PATH";
        return false;
    }

    /// <summary>Последняя содержательная строка вывода — в ней стоит причина отказа.</summary>
    private static string Tail(Godot.Collections.Array output)
    {
        for (int i = output.Count - 1; i >= 0; i--)
        {
            string line = output[i].AsString()?.Trim();

            if (!string.IsNullOrEmpty(line))
                return line.Length > 160 ? line[..160] : line;
        }

        return "no output";
    }
}
