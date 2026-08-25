using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Периодические снимки состояния игры, сопутствующие EventPipe-срезу.
///
/// ФОРМАТ. Каждая завершённая строка файла является самостоятельным JSON-объектом:
/// заголовком, снимком или итогом записи. Поэтому аварийное завершение может повредить
/// только последнюю строку, а предыдущие снимки сохранятся.
///
/// ВЛИЯНИЕ НА ИЗМЕРЕНИЕ. Чтение состояния и сериализация выполняются на главном потоке,
/// поскольку системы и Godot-объекты нельзя безопасно читать в фоне. Готовая строка
/// передаётся ограниченной очереди, а файловая запись выполняется отдельной задачей.
/// Если задача не успевает, снимок пропускается вместо блокировки игрового кадра.
///
/// СОСТОЯНИЕ ОЖИДАНИЯ. Пока запись не начата явно, <see cref="Tick"/> ограничивается
/// проверкой ссылки на канал: файл не создаётся, системы не опрашиваются, память под JSON
/// не выделяется.
/// </summary>
public static class GameSnapshotCapture
{
    public const int SchemaVersion = 1;
    public const int QueueCapacity = 64;

    private static readonly object Gate = new();
    private static readonly object CaptureGate = new();
    private static readonly HashSet<Type> FailedSources = new();

    private static Channel<string> _channel;
    private static Task _writeTask;
    private static string _path;
    private static string _status = "ожидание";
    private static bool _stopping;
    private static long _nextAt;
    private static double _intervalSeconds = 0.25;
    private static int _samples;
    private static int _dropped;
    private static double _lastCaptureMs;

    public static bool Recording
    {
        get
        {
            lock (Gate)
                return _channel != null && !_stopping;
        }
    }

    public static bool Busy
    {
        get
        {
            lock (Gate)
                return _channel != null || _stopping;
        }
    }

    public static string Status
    {
        get
        {
            lock (Gate)
                return _status;
        }
    }

    public static int Samples
    {
        get
        {
            lock (Gate)
                return _samples;
        }
    }

    public static int Dropped
    {
        get
        {
            lock (Gate)
                return _dropped;
        }
    }

    public static double LastCaptureMs
    {
        get
        {
            lock (Gate)
                return _lastCaptureMs;
        }
    }

    public static double IntervalSeconds
    {
        get
        {
            lock (Gate)
                return _intervalSeconds;
        }
        set
        {
            lock (Gate)
            {
                if (_channel == null && !_stopping)
                    _intervalSeconds = Math.Clamp(value, 0.1, 10.0);
            }
        }
    }

    /// <summary>
    /// Начать запись в заранее согласованный с EventPipe путь. Файл создаётся только
    /// из этого метода, который вызывает явная команда панели отладки.
    /// </summary>
    public static bool TryStart(string path, string sessionKey, out string error)
    {
        error = null;

        lock (Gate)
        {
            if (_channel != null || _stopping)
            {
                error = "запись игровых снимков уже идёт";
                return false;
            }

            try
            {
                var stream = new StreamWriter(
                    new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read,
                        bufferSize: 64 * 1024, useAsync: true),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var options = new BoundedChannelOptions(QueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                };

                var channel = Channel.CreateBounded<string>(options);
                _writeTask = Task.Run(() => WriteLoop(channel.Reader, stream));
                _path = path;
                _nextAt = Stopwatch.GetTimestamp();
                _samples = 0;
                _dropped = 0;
                _lastCaptureMs = 0;
                FailedSources.Clear();
                _status = $"запись… {Path.GetFileName(path)}";

                var header = new JsonObject
                {
                    ["type"] = "header",
                    ["schema"] = SchemaVersion,
                    ["session"] = sessionKey,
                    ["interval_ms"] = _intervalSeconds * 1000.0,
                    ["started_elapsed_ms"] = PerformanceCapture.Elapsed.TotalMilliseconds,
                };

                if (!channel.Writer.TryWrite(header.ToJsonString()))
                    throw new IOException("не удалось записать заголовок в очередь");

                // Активный канал публикуется последним: Tick не должен увидеть запись,
                // пока путь, задача и заголовок ещё не подготовлены полностью.
                _channel = channel;
                GD.Print($"[GameSnapshotCapture] старт {path}");
                return true;
            }
            catch (Exception ex)
            {
                _channel = null;
                _writeTask = null;
                _path = null;
                error = ex.Message;
                _status = $"ошибка старта: {ex.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Проверить интервал и, если срок подошёл, снять согласованное состояние мира.
    /// Метод вызывается после графического цикла, когда видимость уже обновлена.
    /// </summary>
    public static void Tick(GameManager gm)
    {
        // В обычной игре это единственная выполняемая проверка: обращение к таймеру,
        // блокировка и весь дальнейший сбор начинаются только при активном канале.
        if (Volatile.Read(ref _channel) == null)
            return;

        lock (CaptureGate)
            CaptureDue(gm);
    }

    private static void CaptureDue(GameManager gm)
    {
        Channel<string> channel;
        double interval;
        long now = Stopwatch.GetTimestamp();

        lock (Gate)
        {
            if (_channel == null || _stopping || now < _nextAt)
                return;

            channel = _channel;
            interval = _intervalSeconds;
            _nextAt = now + (long)(interval * Stopwatch.Frequency);
        }

        FrameTraceMarks.BeginSnapshot();
        long started = Stopwatch.GetTimestamp();

        try
        {
            string line = BuildLine(gm, started);
            bool accepted = channel.Writer.TryWrite(line);

            lock (Gate)
            {
                _lastCaptureMs =
                    (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

                if (accepted)
                    _samples++;
                else
                    _dropped++;
            }
        }
        catch (Exception ex)
        {
            lock (Gate)
                _dropped++;

            GD.PushWarning($"[GameSnapshotCapture] снимок пропущен: {ex.Message}");
        }
        finally
        {
            FrameTraceMarks.EndSnapshot();
        }
    }

    public static bool RequestStop(out string error)
    {
        Channel<string> channel;
        Task task;
        string path;
        string footer;

        lock (CaptureGate)
            if (!TakeWriter(out error, out channel, out task, out path, out footer))
                return false;

        Task.Run(() => FinishStop(channel, task, path, footer));
        return true;
    }

    public static void StopIfRecording()
    {
        Channel<string> channel;
        Task task;
        string path;
        string footer;

        lock (CaptureGate)
            if (!TakeWriter(out _, out channel, out task, out path, out footer))
                return;

        FinishStop(channel, task, path, footer);
    }

    private static string BuildLine(GameManager gm, long started)
    {
        var world = new JsonObject
        {
            ["objects"] = gm.Index.Count,
            ["index_revision"] = gm.Index.Revision,
            ["player_units"] = gm.Units[Faction.Player].Count,
            ["hostile_units"] = gm.Units[Faction.Hostile].Count,
            ["buildings"] = gm.Index.All<Building>().Count,
            ["projectiles"] = gm.Index.All<Projectile>().Count,
            ["nav_revision"] = gm.Nav.Revision,
            ["nav_active_revision"] = gm.Nav.ActiveRevision,
            ["nav_requested_revision"] = gm.Nav.RequestedRevision,
            ["nav_build_pending"] = gm.Nav.BuildPending,
            ["nav_last_build_ms"] = gm.Nav.LastBuildMs,
            ["nav_last_rebuilt_tiles"] = gm.Nav.LastRebuiltTiles,

            // Работа пространственной раскладки за шаг. Ради этих трёх чисел счётчики
            // и заведены: по времени кадра удачный обход от обхода всей карты не отличить
            ["space_target_cells"] = gm.Space.Targets.FilledAt.Count,
            ["space_target_queries"] = gm.Space.Targets.Queries,
            ["space_target_visited"] = gm.Space.Targets.Visited,
            ["space_target_scanned"] = gm.Space.Targets.Scanned,
            ["space_mobile_cells"] = gm.Space.Mobiles.FilledAt.Count,
            ["space_mobile_queries"] = gm.Space.Mobiles.Queries,
            ["space_mobile_scanned"] = gm.Space.Mobiles.Scanned,
        };

        var systems = new JsonObject();

        foreach (var system in gm.Scheduler.Systems)
        {
            if (!Alive.Is(system))
                continue;

            var section = new JsonObject();

            try
            {
                system.CaptureSnapshot(section);
            }
            catch (Exception ex)
            {
                section.Clear();
                section["error"] = ex.Message;

                if (FailedSources.Add(system.GetType()))
                    GD.PushWarning($"[GameSnapshotCapture] {system.GetType().Name}: {ex.Message}");
            }

            if (section.Count > 0)
                systems[system.GetType().Name] = section;
        }

        var buffer = new ArrayBufferWriter<byte>(4096);

        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "sample");
            json.WriteNumber("schema", SchemaVersion);
            json.WriteNumber("elapsed_ms", PerformanceCapture.Elapsed.TotalMilliseconds);
            json.WriteNumber("physics_frame", (long)Engine.GetPhysicsFrames());
            json.WriteNumber("process_frame", (long)Engine.GetProcessFrames());
            json.WritePropertyName("world");
            world.WriteTo(json);
            json.WritePropertyName("systems");
            systems.WriteTo(json);

            // Значение включает сбор всех разделов и почти всю сериализацию. Последняя
            // запись числа и перенос строки выполняются уже после измеренной границы.
            double captureMs =
                (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            json.WriteNumber("capture_ms", captureMs);
            json.WriteEndObject();
            json.Flush();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static bool TakeWriter(out string error, out Channel<string> channel, out Task task,
        out string path, out string footer)
    {
        error = null;
        channel = null;
        task = null;
        path = null;
        footer = null;

        lock (Gate)
        {
            if (_channel == null)
            {
                error = _stopping ? "остановка игровых снимков уже идёт" :
                    "запись игровых снимков не идёт";
                return false;
            }

            footer = new JsonObject
            {
                ["type"] = "footer",
                ["schema"] = SchemaVersion,
                ["elapsed_ms"] = PerformanceCapture.Elapsed.TotalMilliseconds,
                ["samples"] = _samples,
                ["dropped_samples"] = _dropped,
            }.ToJsonString();

            channel = _channel;
            task = _writeTask;
            path = _path;
            _channel = null;
            _writeTask = null;
            _path = null;
            _stopping = true;
            _status = "остановка…";
            return true;
        }
    }

    private static async Task WriteLoop(ChannelReader<string> reader, StreamWriter stream)
    {
        await using (stream)
        {
            await foreach (string line in reader.ReadAllAsync())
                await stream.WriteLineAsync(line);

            await stream.FlushAsync();
        }
    }

    private static void FinishStop(Channel<string> channel, Task task, string path, string footer)
    {
        try
        {
            if (task.IsFaulted)
                task.GetAwaiter().GetResult();

            // Итоговая строка должна следовать за всеми принятыми снимками. Она записывается
            // после снятия активного канала, поэтому новые снимки уже не могут войти в очередь.
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                channel.Writer.WriteAsync(footer, timeout.Token).AsTask().GetAwaiter().GetResult();

            channel.Writer.TryComplete();

            if (!task.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("таймаут записи файла");

            task.GetAwaiter().GetResult();

            lock (Gate)
            {
                _stopping = false;
                _status = $"готово: {Path.GetFileName(path)}";
            }

            GD.Print($"[GameSnapshotCapture] стоп {path}");
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                _stopping = false;
                _status = $"ошибка остановки: {ex.Message}";
            }

            GD.PushError($"[GameSnapshotCapture] остановка: {ex}");
            channel.Writer.TryComplete(ex);
        }
    }
}
