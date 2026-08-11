using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Microsoft.Diagnostics.NETCore.Client;

/// <summary>
/// Запись срезов CPU через EventPipe в файлы под <c>artifacts/traces</c>.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ <see cref="StepProfiler"/>. Тот отвечает на вопрос «какая система
/// тормозит», а этот — «какой метод внутри неё»: sampling стеков CLR, тот же формат,
/// что пишет <c>dotnet-trace</c> (<c>.nettrace</c>). Speedscope этот формат не читает,
/// поэтому после остановки вызывается <c>dotnet-trace convert --format Speedscope</c>
/// и рядом появляется <c>.speedscope.json</c> — его и нужно открывать.
///
/// ПОЧЕМУ НЕ ВНЕШНИЙ ПРОЦЕСС ДЛЯ СБОРА. Штатная остановка <c>dotnet-trace collect</c>
/// ждёт Enter или Ctrl+C в консоли; при запуске из игры на Windows это ненадёжно.
/// Клиент EventPipe даёт явные Start/Stop из кнопки панели. Внешний <c>dotnet-trace</c>
/// нужен только для конвертации (глобальный tool: <c>dotnet tool install -g dotnet-trace</c>).
///
/// Ключ сессии и отсчёт времени живут на процесс: перезапуск партии не сбрасывает их,
/// иначе срезы одной рабочей сессии рассыпались бы по разным префиксам.
///
/// Вместе с sampling пишутся метки <see cref="FrameTraceMarks"/> (границы Physics и
/// Process у <see cref="GameManager"/>). В Speedscope их почти не видно; для timeline
/// с отрезками кадра: convert --format Chromium → ui.perfetto.dev.
/// </summary>
public static class DotnetTraceCapture
{
    /// <summary>Каталог относительно корня проекта Godot.</summary>
    public const string RelativeDir = "artifacts/traces";

    /// <summary>
    /// Ключевые слова CLR для профиля, совпадающего с <c>dotnet-trace</c> cpu-sampling:
    /// Default | GC | Type | JITSymbols (0x1C00008001C).
    /// </summary>
    private const long CpuSamplingClrKeywords = 0x1C00008001C;

    private const int CircularBufferMb = 64;

    private static readonly object Gate = new();

    private static readonly string SessionKey = Guid.NewGuid().ToString("N")[..12];

    private static readonly long StartedTicks = Stopwatch.GetTimestamp();

    private static EventPipeSession _session;
    private static Task _copy;
    private static string _currentPath;
    private static string _status = "ожидание";
    private static string _lastPath;
    private static bool _stopping;

    /// <summary>Короткий уникальный ключ текущего запуска процесса.</summary>
    public static string Key => SessionKey;

    /// <summary>Идёт ли запись среза или ещё не закончилась остановка.</summary>
    public static bool Busy
    {
        get
        {
            lock (Gate)
                return _session != null || _stopping;
        }
    }

    /// <summary>Идёт ли именно запись (не фаза остановки).</summary>
    public static bool Recording
    {
        get
        {
            lock (Gate)
                return _session != null;
        }
    }

    /// <summary>Краткий статус для панели: ожидание, запись, путь последнего файла, ошибка.</summary>
    public static string Status
    {
        get
        {
            lock (Gate)
                return _status;
        }
    }

    /// <summary>Абсолютный путь последнего успешно остановленного среза, либо пусто.</summary>
    public static string LastPath
    {
        get
        {
            lock (Gate)
                return _lastPath ?? "";
        }
    }

    /// <summary>Время с момента выдачи ключа сессии.</summary>
    public static TimeSpan Elapsed =>
        TimeSpan.FromSeconds((Stopwatch.GetTimestamp() - StartedTicks) * 1.0 / Stopwatch.Frequency);

    /// <summary>Начать запись нового среза. Повторный вызов во время записи отклоняется.</summary>
    public static bool TryStart(out string error)
    {
        error = null;

        lock (Gate)
        {
            if (_session != null || _stopping)
            {
                error = "запись уже идёт";
                return false;
            }

            string dir;

            try
            {
                dir = EnsureDir();
            }
            catch (Exception ex)
            {
                error = $"каталог: {ex.Message}";
                _status = error;
                return false;
            }

            string name = $"{SessionKey}_{FormatElapsed(Elapsed)}.nettrace";
            string path = Path.Combine(dir, name);

            try
            {
                var client = new DiagnosticsClient(System.Environment.ProcessId);
                var providers = new List<EventPipeProvider>
                {
                    new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
                    new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
                        CpuSamplingClrKeywords),
                    // Метки Physics/Process Start–Stop для Chromium / Perfetto
                    new(FrameTraceMarks.ProviderName, EventLevel.Informational, long.MaxValue),
                };

                EventPipeSession session = client.StartEventPipeSession(
                    providers, requestRundown: true, circularBufferMB: CircularBufferMb);

                FileStream file = new(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
                Task copy = Task.Run(() =>
                {
                    try
                    {
                        session.EventStream.CopyTo(file);
                    }
                    finally
                    {
                        file.Dispose();
                    }
                });

                _session = session;
                _copy = copy;
                _currentPath = path;
                _status = $"запись… {name}";
                GD.Print($"[DotnetTraceCapture] старт {path}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _status = $"ошибка старта: {ex.Message}";
                GD.PushError($"[DotnetTraceCapture] старт: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Запросить остановку на фоне: кнопка панели не должна ждать rundown на кадре.
    /// Без активной записи — отказ.
    /// </summary>
    public static bool RequestStop(out string error)
    {
        if (!TakeSession(out error, out EventPipeSession session, out Task copy, out string path))
            return false;

        Task.Run(() => FinishStop(session, copy, path));
        return true;
    }

    /// <summary>
    /// Остановить запись синхронно. Для выхода из дерева сцены, когда дождаться файла
    /// важнее, чем сохранить кадр.
    /// </summary>
    public static void StopIfRecording()
    {
        if (!TakeSession(out _, out EventPipeSession session, out Task copy, out string path))
            return;

        FinishStop(session, copy, path);
    }

    /// <summary>Каталог срезов; создаётся при первом обращении.</summary>
    public static string EnsureDir()
    {
        string root = ProjectSettings.GlobalizePath("res://");
        string dir = Path.Combine(root, RelativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Время сессии в имени файла: часы, минуты, секунды и миллисекунды с нулями,
    /// чтобы срезы одной сессии шли по алфавиту в порядке записи.
    /// </summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        int hours = (int)elapsed.TotalHours;
        return $"{hours:00}h{elapsed.Minutes:00}m{elapsed.Seconds:00}s{elapsed.Milliseconds:000}";
    }

    private static bool TakeSession(out string error, out EventPipeSession session, out Task copy,
        out string path)
    {
        error = null;
        session = null;
        copy = null;
        path = null;

        lock (Gate)
        {
            if (_session == null)
            {
                error = _stopping ? "остановка уже идёт" : "запись не идёт";
                return false;
            }

            session = _session;
            copy = _copy;
            path = _currentPath;
            _session = null;
            _copy = null;
            _currentPath = null;
            _stopping = true;
            _status = "остановка…";
            return true;
        }
    }

    private static void FinishStop(EventPipeSession session, Task copy, string path)
    {
        try
        {
            session.Stop();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[DotnetTraceCapture] Stop: {ex.Message}");
        }

        try
        {
            if (!copy.Wait(TimeSpan.FromSeconds(30)))
            {
                lock (Gate)
                {
                    _stopping = false;
                    _status = "таймаут записи файла";
                }

                session.Dispose();
                return;
            }

            copy.GetAwaiter().GetResult();
            session.Dispose();

            string speedscope = TryConvertSpeedscope(path, out string convertError);

            lock (Gate)
            {
                _stopping = false;

                if (speedscope != null)
                {
                    _lastPath = speedscope;
                    _status = $"готово: {Path.GetFileName(speedscope)}";
                }
                else
                {
                    _lastPath = path;
                    _status = convertError != null
                        ? $"готово .nettrace; Speedscope: {convertError}"
                        : $"готово: {Path.GetFileName(path)}";
                }
            }

            GD.Print($"[DotnetTraceCapture] стоп {path}" +
                     (speedscope != null ? $" → {speedscope}" : ""));
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                _stopping = false;
                _status = $"ошибка стопа: {ex.Message}";
            }

            GD.PushError($"[DotnetTraceCapture] стоп: {ex}");
            session.Dispose();
        }
    }

    /// <summary>
    /// Рядом с .nettrace пишет .speedscope.json. Без установленного global tool
    /// возвращает null и текст ошибки для статуса панели.
    /// </summary>
    private static string TryConvertSpeedscope(string nettracePath, out string error)
    {
        error = null;
        string tool = FindDotnetTrace();

        if (tool == null)
        {
            error = "нет dotnet-trace (dotnet tool install -g dotnet-trace)";
            return null;
        }

        // convert сам дописывает «.speedscope.json» к -o; передаём путь без расширения
        string outputBase = Path.ChangeExtension(nettracePath, null);
        string output = outputBase + ".speedscope.json";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                ArgumentList =
                {
                    "convert",
                    nettracePath,
                    "--format",
                    "Speedscope",
                    "-o",
                    outputBase,
                },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process process = Process.Start(psi);

            if (process == null)
            {
                error = "не удалось запустить dotnet-trace";
                return null;
            }

            string stderr = process.StandardError.ReadToEnd();
            string stdout = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* процесс уже завершился */ }

                error = "таймаут convert";
                return null;
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                error = string.IsNullOrWhiteSpace(stderr)
                    ? (string.IsNullOrWhiteSpace(stdout) ? $"код {process.ExitCode}" : stdout.Trim())
                    : stderr.Trim();
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string FindDotnetTrace()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string local = Path.Combine(home, ".dotnet", "tools",
            OperatingSystem.IsWindows() ? "dotnet-trace.exe" : "dotnet-trace");

        if (File.Exists(local))
            return local;

        return null;
    }
}
