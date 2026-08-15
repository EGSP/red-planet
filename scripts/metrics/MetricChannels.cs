using System.Collections.Generic;

/// <summary>Как показывать значение канала — от этого зависит и подпись, и шкала графика.</summary>
public enum MetricFormat
{
    /// <summary>Обычное число: мощь армии, очки террора, запас.</summary>
    Number,

    /// <summary>Поток в секунду: доход, спрос, расход.</summary>
    Rate,

    /// <summary>Целое количество: юниты, постройки.</summary>
    Count,

    /// <summary>Доля 0..1, показывается процентами.</summary>
    Percent,
}

/// <summary>
/// Описание одного ряда: как он называется на графике и как читается его значение.
///
/// ЗАЧЕМ ОПИСАНИЕ ОТДЕЛЬНО ОТ ДАННЫХ. Ряд в <see cref="TimeSeriesProjection"/> есть просто
/// список чисел под строковым именем, и по нему нельзя ни подписать ось, ни решить, что
/// величина является долей, а не количеством. Кроме того, объявленный заранее состав каналов
/// делает набор колонок отчёта постоянным с первого замера, и потому файл можно писать
/// построчно по ходу партии, а не собирать целиком в конце.
/// </summary>
public sealed class MetricChannel
{
    public MetricChannel(string id, string caption, MetricFormat format)
    {
        Id = id;
        Caption = caption;
        Format = format;
    }

    /// <summary>Имя ряда — оно же имя канала в <see cref="MetricSampled"/> и колонка отчёта.</summary>
    public readonly string Id;

    /// <summary>Подпись на графике и в легенде.</summary>
    public readonly string Caption;

    public readonly MetricFormat Format;
}

/// <summary>Способ показа графика.</summary>
public enum MetricPlotMode
{
    /// <summary>Каждый канал отдельной линией.</summary>
    Lines,

    /// <summary>
    /// Каналы складываются друг на друга. Допустимо только тогда, когда слагаемые измерены
    /// в одних единицах и их сумма имеет смысл, — иначе график покажет величину, которой
    /// в игре не существует.
    /// </summary>
    Stack,
}

/// <summary>Один график отчёта: заголовок, состав каналов и способ показа.</summary>
public sealed class MetricPlot
{
    public MetricPlot(string title, MetricPlotMode mode, string[] channels, string overlay = null)
    {
        Title = title;
        Mode = mode;
        Channels = channels;
        Overlay = overlay;
    }

    public readonly string Title;

    public readonly MetricPlotMode Mode;

    public readonly string[] Channels;

    /// <summary>
    /// Канал, который рисуется линией поверх остального. Нужен стеку: сумма слагаемых
    /// и сглаженная кривая отвечают на разные вопросы, и показывать их врозь бессмысленно.
    /// </summary>
    public readonly string Overlay;
}

/// <summary>
/// Состав наблюдаемых величин партии — единственное место, где он объявлен.
///
/// Порядок объявления значим: в этом же порядке значения замера ложатся в строку отчёта,
/// поэтому колонка отчёта опознаётся по номеру в <see cref="All"/>. Добавлять каналы следует
/// в конец списка, а удалять — с повышением версии схемы в <see cref="MetricsRecorder"/>,
/// иначе ранее записанные отчёты будут прочитаны неверно.
///
/// ВЕЛИЧИНЫ ТОЛЬКО СУММАРНЫЕ. Разложения расхода по назначению и состава армии по типам
/// здесь нет намеренно: пометка расхода назначением требует правки экономики, а состав
/// по типам делает набор колонок зависимым от справочника, то есть непостоянным.
/// </summary>
public static class MetricChannels
{
    private static readonly MetricChannel[] Registry =
    {
        // ── Террор ───────────────────────────────────────────────────────────────
        new("terror.raw", "террор", MetricFormat.Number),
        new("terror.smoothed", "сглаженный", MetricFormat.Number),
        new("terror.production", "производство", MetricFormat.Number),
        new("terror.expansion", "экспансия", MetricFormat.Number),
        new("terror.army", "армия", MetricFormat.Number),
        new("terror.time", "время", MetricFormat.Number),

        // ── Металл ───────────────────────────────────────────────────────────────
        new("metal.income", "добыча", MetricFormat.Rate),
        new("metal.demand", "спрос", MetricFormat.Rate),
        new("metal.spending", "расход", MetricFormat.Rate),
        new("metal.stored", "запас", MetricFormat.Number),
        new("metal.capacity", "ёмкость", MetricFormat.Number),

        // ── Энергия ──────────────────────────────────────────────────────────────
        new("energy.income", "выработка", MetricFormat.Rate),
        new("energy.demand", "спрос", MetricFormat.Rate),
        new("energy.spending", "расход", MetricFormat.Rate),
        new("energy.stored", "запас", MetricFormat.Number),
        new("energy.capacity", "ёмкость", MetricFormat.Number),

        // ── Производительность ───────────────────────────────────────────────────
        new("efficiency.total", "итог", MetricFormat.Percent),
        new("efficiency.metal", "по металлу", MetricFormat.Percent),
        new("efficiency.energy", "по энергии", MetricFormat.Percent),

        // ── Свои силы ────────────────────────────────────────────────────────────
        new("army.count", "армия, шт", MetricFormat.Count),
        new("army.power", "мощь армии", MetricFormat.Number),
        new("builders.count", "строители", MetricFormat.Count),
        new("builders.busy", "из них заняты", MetricFormat.Count),
        new("buildings.count", "постройки", MetricFormat.Count),
        new("buildings.sites", "каркасы", MetricFormat.Count),

        // ── Противник ────────────────────────────────────────────────────────────
        new("enemy.count", "живые, шт", MetricFormat.Count),
        new("enemy.power", "мощь", MetricFormat.Number),
        new("combat.spawned", "пришло всего", MetricFormat.Count),
        new("combat.destroyed", "уничтожено", MetricFormat.Count),
        new("combat.losses", "свои потери", MetricFormat.Count),

        // ── Давление ─────────────────────────────────────────────────────────────
        new("pressure.budget", "бюджет фона", MetricFormat.Number),
        new("pressure.used", "занято фоном", MetricFormat.Number),
        new("wave.terror", "вход волны", MetricFormat.Number),
        new("wave.budget", "бюджет волны", MetricFormat.Number),
    };

    /// <summary>
    /// Раскладка отчёта. Выбор каналов игроку не предлагается: набор постоянен, и каждый
    /// график отвечает на один вопрос — потому раскладка задана здесь, а не собирается
    /// в интерфейсе.
    /// </summary>
    private static readonly MetricPlot[] PlotList =
    {
        new("Террор", MetricPlotMode.Stack,
            new[] { "terror.production", "terror.expansion", "terror.army", "terror.time" },
            "terror.smoothed"),

        // Потоки и запас разнесены по разным графикам намеренно: запас исчисляется тысячами,
        // поток — десятками, и на общей шкале поток прижимается к нулю и становится нечитаем
        new("Металл: потоки", MetricPlotMode.Lines,
            new[] { "metal.income", "metal.demand", "metal.spending" }),

        new("Металл: склад", MetricPlotMode.Lines,
            new[] { "metal.stored", "metal.capacity" }),

        new("Энергия: потоки", MetricPlotMode.Lines,
            new[] { "energy.income", "energy.demand", "energy.spending" }),

        new("Энергия: склад", MetricPlotMode.Lines,
            new[] { "energy.stored", "energy.capacity" }),

        new("Производительность", MetricPlotMode.Lines,
            new[] { "efficiency.total", "efficiency.metal", "efficiency.energy" }),

        new("Свои силы", MetricPlotMode.Lines,
            new[] { "army.count", "army.power", "builders.count", "builders.busy" }),

        new("База", MetricPlotMode.Lines,
            new[] { "buildings.count", "buildings.sites" }),

        new("Противник", MetricPlotMode.Lines,
            new[] { "enemy.count", "enemy.power" }),

        new("Счёт боя", MetricPlotMode.Lines,
            new[] { "combat.spawned", "combat.destroyed", "combat.losses" }),

        new("Давление", MetricPlotMode.Lines,
            new[] { "pressure.budget", "pressure.used", "wave.terror", "wave.budget" }),
    };

    private static readonly Dictionary<string, int> Order = BuildOrder();

    /// <summary>Каналы в порядке объявления — он же порядок значений в строке отчёта.</summary>
    public static IReadOnlyList<MetricChannel> All => Registry;

    /// <summary>Графики отчёта в порядке показа.</summary>
    public static IReadOnlyList<MetricPlot> Plots => PlotList;

    public static int Count => Registry.Length;

    /// <summary>Номер канала в замере. Минус единица, если канал не объявлен.</summary>
    public static int IndexOf(string id) => Order.TryGetValue(id, out int index) ? index : -1;

    public static MetricChannel Find(string id)
    {
        int index = IndexOf(id);
        return index < 0 ? null : Registry[index];
    }

    private static Dictionary<string, int> BuildOrder()
    {
        var map = new Dictionary<string, int>(Registry.Length);

        for (int i = 0; i < Registry.Length; i++)
            map[Registry[i].Id] = i;

        return map;
    }
}
