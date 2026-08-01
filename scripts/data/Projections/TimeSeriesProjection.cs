using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

/// <summary>
/// Ряды наблюдаемых величин по ходу партии — периодический регистр сведений.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ ПРОЕКЦИЯ, А НЕ ХРАНЕНИЕ В ЖУРНАЛЕ. Журнал документов есть шина в пределах
/// кадра: он чистится каждый кадр и историю не держит. Сделать замеры нетранзиентными
/// значило бы дать журналу вторую, чуждую ему роль и вернуть неограниченный рост, от которого
/// он как раз избавлен. Здесь же соблюдено общее правило проекций — состояние набирается
/// дельтой в момент публикации, из истории ничего не пересчитывается.
///
/// ОБЪЁМ. Замер это один float. Пять рядов при секундном шаге дают за час партии
/// восемнадцать тысяч значений, то есть семьдесят два килобайта. Поэтому ни прореживания,
/// ни кольцевого буфера, ни вытеснения здесь нет: обычный список, растущий до конца сессии.
/// Считать это утечкой не надо — ряд и должен покрывать партию целиком, иначе по нему
/// нельзя будет посмотреть, что было в начале.
///
/// ВРЕМЯ. Отметки времени в ряду нет: шаг постоянен, и номер значения сам есть время.
/// Отсюда требование к <see cref="Step"/> — он задаётся один раз при старте и по ходу партии
/// не меняется. Считается он в игровом времени, поэтому пауза останавливает и замеры,
/// и связь «номер — минута партии» остаётся точной.
///
/// КАК ЧИТАТЬ. <c>Window("terror.raw", 5f)</c> — хвост ряда за последние пять минут,
/// без копирования: возвращается окно поверх самого списка.
/// </summary>
public sealed class TimeSeriesProjection : Projection
{
    private readonly Dictionary<string, List<float>> _channels = new();

    /// <summary>
    /// Шаг ряда, секунд игрового времени. Это настройка, а не состояние: её выставляет
    /// владелец замеров при связывании, и с этого мига она постоянна.
    /// </summary>
    public float Step { get; set; } = 1f;

    /// <summary>Сколько замеров сделано. Длина самого длинного ряда.</summary>
    public int Count { get; private set; }

    /// <summary>Имена заведённых рядов — по ним строится список доступного к показу.</summary>
    public IReadOnlyCollection<string> Channels => _channels.Keys;

    /// <summary>Длина партии в секундах на момент последнего замера.</summary>
    public float Seconds => Count * Step;

    public override void Subscribe(EventStore events)
    {
        events.Stream<MetricSampled>().Appended += OnSampled;
    }

    private void OnSampled(MetricSampled record)
    {
        if (string.IsNullOrEmpty(record.Channel) || record.Tick < 0)
            return;

        var series = Series(record.Channel);

        // Ряд, заведённый позже других, обязан встать на своё место по времени, а не
        // с начала списка: иначе два ряда с разными длинами перестанут сходиться по номеру
        // замера, и график покажет одно значение под временем другого
        while (series.Count < record.Tick)
            series.Add(series.Count > 0 ? series[^1] : 0f);

        if (series.Count == record.Tick)
            series.Add(record.Value);
        else
            series[record.Tick] = record.Value;

        Count = Mathf.Max(Count, record.Tick + 1);
        NotifyChanged();
    }

    /// <summary>
    /// Хвост ряда за последние minutes минут. Окно поверх списка, а не копия: построение
    /// графика ничего не выделяет и может звать это хоть каждый кадр. Читать возвращённое
    /// после следующего замера нельзя — список к тому мигу мог переехать в памяти.
    /// </summary>
    public ReadOnlySpan<float> Window(string channel, float minutes)
    {
        if (!_channels.TryGetValue(channel, out var series))
            return ReadOnlySpan<float>.Empty;

        var all = CollectionsMarshal.AsSpan(series);

        if (minutes <= 0f || Step <= 0f)
            return all;

        int want = Mathf.CeilToInt(minutes * 60f / Step);

        return want >= all.Length ? all : all[^want..];
    }

    /// <summary>Весь ряд целиком. Пустое окно, если такого ряда никто не заводил.</summary>
    public ReadOnlySpan<float> All(string channel) => Window(channel, 0f);

    /// <summary>Последнее значение ряда. Ноль, если замеров ещё не было.</summary>
    public float Last(string channel)
    {
        var window = All(channel);
        return window.Length > 0 ? window[^1] : 0f;
    }

    private List<float> Series(string channel)
    {
        if (_channels.TryGetValue(channel, out var series))
            return series;

        series = new List<float>();
        _channels[channel] = series;
        return series;
    }
}
