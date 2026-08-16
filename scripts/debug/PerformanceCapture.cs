using System;
using System.IO;

/// <summary>
/// Единое управление CPU-срезом EventPipe и сопутствующими игровыми снимками.
///
/// Объединение выполняется здесь, а не в панели: кнопка является только одним из
/// возможных вызывающих мест, тогда как требование к согласованному запуску и остановке
/// относится к самим средствам диагностики. Частично активная запись не допускается.
/// </summary>
public static class PerformanceCapture
{
    private static readonly object Gate = new();
    private static bool _transition;

    public static string Key => DotnetTraceCapture.Key;

    public static TimeSpan Elapsed => DotnetTraceCapture.Elapsed;

    public static bool Recording
    {
        get
        {
            lock (Gate)
                return DotnetTraceCapture.Recording && GameSnapshotCapture.Recording;
        }
    }

    public static bool Busy
    {
        get
        {
            lock (Gate)
                return _transition || DotnetTraceCapture.Busy || GameSnapshotCapture.Busy;
        }
    }

    public static double IntervalSeconds
    {
        get => GameSnapshotCapture.IntervalSeconds;
        set => GameSnapshotCapture.IntervalSeconds = value;
    }

    public static int Samples => GameSnapshotCapture.Samples;

    public static int Dropped => GameSnapshotCapture.Dropped;

    public static double LastCaptureMs => GameSnapshotCapture.LastCaptureMs;

    public static string Status
    {
        get
        {
            lock (Gate)
                return $"EventPipe: {DotnetTraceCapture.Status}\n" +
                       $"игра: {GameSnapshotCapture.Status}";
        }
    }

    public static bool TryStart(out string error)
    {
        lock (Gate)
        {
            error = null;

            if (_transition || DotnetTraceCapture.Busy || GameSnapshotCapture.Busy)
            {
                error = "запись уже идёт";
                return false;
            }

            _transition = true;

            try
            {
                if (!DotnetTraceCapture.TryStart(out error))
                    return false;

                string tracePath = DotnetTraceCapture.CurrentPath;
                string snapshotPath = Path.ChangeExtension(tracePath, null) + ".game.jsonl";

                if (GameSnapshotCapture.TryStart(snapshotPath, Key, out string snapshotError))
                    return true;

                // EventPipe уже начал запись, но оставлять один канал без второго нельзя:
                // полученный срез не имел бы обещанного игрового контекста.
                DotnetTraceCapture.RequestStop(out _);
                error = $"игровые снимки: {snapshotError}";
                return false;
            }
            finally
            {
                _transition = false;
            }
        }
    }

    public static bool RequestStop(out string error)
    {
        lock (Gate)
        {
            error = null;

            if (_transition)
            {
                error = "изменение состояния записи уже выполняется";
                return false;
            }

            _transition = true;

            try
            {
                bool snapshot = GameSnapshotCapture.RequestStop(out string snapshotError);
                bool trace = DotnetTraceCapture.RequestStop(out string traceError);

                if (snapshot || trace)
                    return true;

                error = snapshotError ?? traceError ?? "запись не идёт";
                return false;
            }
            finally
            {
                _transition = false;
            }
        }
    }

    public static void StopIfRecording()
    {
        lock (Gate)
        {
            if (_transition)
                return;

            _transition = true;

            try
            {
                GameSnapshotCapture.StopIfRecording();
                DotnetTraceCapture.StopIfRecording();
            }
            finally
            {
                _transition = false;
            }
        }
    }

    /// <summary>
    /// Проверить срок очередного игрового снимка. При отсутствии явно начатой записи
    /// дальнейшая работа не выполняется.
    /// </summary>
    public static void Tick(GameManager gm) => GameSnapshotCapture.Tick(gm);

    public static string FormatElapsed(TimeSpan elapsed) =>
        DotnetTraceCapture.FormatElapsed(elapsed);
}
