using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Запись отчёта о партии: ряды наблюдаемых величин в файл JSONL.
///
/// ЗАЧЕМ ПИСАТЬ ПО ХОДУ, А НЕ В КОНЦЕ. Партия кончается не только победой и поражением:
/// окно закрывают крестиком, отладчик прерывают, игра может и упасть. Единовременная запись
/// в конце не сохранила бы ни одного из этих случаев, тогда как построчная теряет самое
/// большее последний замер. Возможность писать построчно даёт объявленный заранее состав
/// каналов (<see cref="MetricChannels"/>): набор колонок известен с первой строки.
///
/// ФОРМАТ. Каждая строка есть самостоятельный объект JSON: заголовок, замер, событие или
/// итог. Замер хранит значения массивом в порядке <see cref="MetricChannels.All"/>, а не
/// объектом с именами: имена уже перечислены в заголовке, и повторять их тридцать раз
/// в секунду значило бы раздуть файл на порядок без единого нового сведения.
///
/// ВЛИЯНИЕ НА КАДР. Строка формируется на главном потоке — читать состояние игры в фоне
/// нельзя, — а запись в файл выполняет отдельная задача через ограниченную очередь. При
/// такте раз в секунду очередь не наполняется никогда; переполнение считается пропуском,
/// а не причиной задержать кадр.
/// </summary>
public sealed class MetricsRecorder
{
    /// <summary>
    /// Версия формата. Повышается при изъятии канала или смене устройства строк; добавление
    /// канала в конец списка старые отчёты не портит и версии не требует.
    /// </summary>
    public const int SchemaVersion = 1;

    private const int QueueCapacity = 256;

    private static readonly string Directory = "user://metrics";

    private Channel<string> _channel;
    private Task _writeTask;
    private int _dropped;

    /// <summary>Полный путь к файлу отчёта или null, если запись не начата.</summary>
    public string Path { get; private set; }

    /// <summary>Идёт ли запись.</summary>
    public bool Active => _channel != null;

    /// <summary>Сколько строк не поместилось в очередь. Отличное от нуля означает потерю замеров.</summary>
    public int Dropped => _dropped;

    /// <summary>
    /// Начать отчёт. Имя файла складывается из времени начала партии, поэтому отчёты
    /// нескольких партий подряд не затирают друг друга.
    /// </summary>
    public bool TryStart(float step, out string error)
    {
        error = null;

        if (_channel != null)
        {
            error = "запись отчёта уже идёт";
            return false;
        }

        try
        {
            DirAccess.MakeDirRecursiveAbsolute(Directory);

            string name = $"session-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            string path = ProjectSettings.GlobalizePath($"{Directory}/{name}");

            var stream = new StreamWriter(
                new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read,
                    bufferSize: 32 * 1024, useAsync: true),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var channel = System.Threading.Channels.Channel.CreateBounded<string>(
                new BoundedChannelOptions(QueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropWrite,
                });

            _writeTask = Task.Run(() => WriteLoop(channel.Reader, stream));
            _dropped = 0;
            Path = path;

            if (!channel.Writer.TryWrite(Header(step)))
                throw new IOException("не удалось записать заголовок в очередь");

            // Канал публикуем последним: до этого мига замер не должен считать запись начатой
            _channel = channel;
            GD.Print($"[MetricsRecorder] отчёт партии: {path}");
            return true;
        }
        catch (Exception ex)
        {
            _channel = null;
            _writeTask = null;
            Path = null;
            error = ex.Message;
            GD.PushWarning($"[MetricsRecorder] отчёт не начат: {ex.Message}");
            return false;
        }
    }

    /// <summary>Замер: номер такта, игровое время и значения в порядке объявления каналов.</summary>
    public void Sample(int tick, float seconds, IReadOnlyList<float> values)
    {
        if (_channel == null)
            return;

        var text = new StringBuilder(64 + values.Count * 8);
        text.Append("{\"type\":\"sample\",\"tick\":").Append(tick);
        text.Append(",\"t\":").Append(Number(seconds));
        text.Append(",\"v\":[");

        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
                text.Append(',');

            text.Append(Number(values[i]));
        }

        text.Append("]}");
        Push(text.ToString());
    }

    /// <summary>
    /// Отметка на оси времени: запуск волны и подобные ему одиночные происшествия.
    /// Рядом такое хранить нельзя — величина существует не в каждый такт, а лишь в один.
    /// </summary>
    public void Event(int tick, float seconds, string kind, JsonObject fields)
    {
        if (_channel == null)
            return;

        var line = fields ?? new JsonObject();
        line["type"] = "event";
        line["tick"] = tick;
        line["t"] = Math.Round(seconds, 3);
        line["kind"] = kind;

        Push(line.ToJsonString());
    }

    /// <summary>
    /// Закончить отчёт и дождаться, пока файл будет дописан. Ожидание синхронное: метод
    /// зовётся при завершении партии и при сносе сессии, а после них главный поток может
    /// и не дожить до конца фоновой задачи.
    /// </summary>
    public void Stop(int ticks, float seconds, SessionOutcome outcome)
    {
        var channel = _channel;
        var task = _writeTask;

        if (channel == null)
            return;

        _channel = null;
        _writeTask = null;

        var footer = new JsonObject
        {
            ["type"] = "footer",
            ["schema"] = SchemaVersion,
            ["ticks"] = ticks,
            ["seconds"] = Math.Round(seconds, 3),
            ["outcome"] = outcome.ToString().ToLowerInvariant(),
            ["dropped"] = _dropped,
        };

        try
        {
            channel.Writer.TryWrite(footer.ToJsonString());
            channel.Writer.TryComplete();

            if (task != null && !task.Wait(TimeSpan.FromSeconds(10)))
                GD.PushWarning("[MetricsRecorder] файл отчёта не дописан за отведённое время");

            GD.Print($"[MetricsRecorder] отчёт закрыт: {Path}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[MetricsRecorder] закрытие отчёта: {ex.Message}");
        }
    }

    private void Push(string line)
    {
        if (!_channel.Writer.TryWrite(line))
            _dropped++;
    }

    private static string Header(float step)
    {
        var channels = new JsonArray();

        foreach (var channel in MetricChannels.All)
        {
            channels.Add(new JsonObject
            {
                ["id"] = channel.Id,
                ["caption"] = channel.Caption,
                ["format"] = channel.Format.ToString().ToLowerInvariant(),
            });
        }

        var plots = new JsonArray();

        foreach (var plot in MetricChannels.Plots)
        {
            var ids = new JsonArray();

            foreach (string id in plot.Channels)
                ids.Add(id);

            var item = new JsonObject
            {
                ["title"] = plot.Title,
                ["mode"] = plot.Mode.ToString().ToLowerInvariant(),
                ["channels"] = ids,
            };

            if (!string.IsNullOrEmpty(plot.Overlay))
                item["overlay"] = plot.Overlay;

            plots.Add(item);
        }

        return new JsonObject
        {
            ["type"] = "header",
            ["schema"] = SchemaVersion,
            ["started"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
            ["step"] = Math.Round(step, 4),
            ["channels"] = channels,
            ["plots"] = plots,
        }.ToJsonString();
    }

    /// <summary>
    /// Три знака после запятой. Точнее хранить нечего: величины игры задаются с меньшей
    /// точностью, а полная запись float удлинила бы файл вдвое.
    /// </summary>
    private static string Number(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "0";

        return Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Запись строк из очереди в файл. Сброс буфера делается на каждой строке, а не по
    /// заполнении: ради устойчивости к аварийному завершению отчёт и пишется построчно,
    /// а строка, оставшаяся в буфере потока, при падении пропадает вместе с процессом.
    /// При такте раз в секунду сброс стоит пренебрежимо мало и выполняется вне главного потока.
    /// </summary>
    private static async Task WriteLoop(ChannelReader<string> reader, StreamWriter stream)
    {
        await using (stream)
        {
            await foreach (string line in reader.ReadAllAsync())
            {
                await stream.WriteLineAsync(line);
                await stream.FlushAsync();
            }
        }
    }
}
