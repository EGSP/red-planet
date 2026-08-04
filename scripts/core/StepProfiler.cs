using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Замеры времени шага по системам. Живёт при планировщике, поскольку тот — единственное
/// место, откуда системы вызываются, и обернуть вызов можно только здесь.
///
/// ЗАЧЕМ СВОЙ ЗАМЕР. Профилировщик редактора раскладывает по функциям только GDScript,
/// а вызовы C# показывает одной строкой; мониторы отладчика дают время физического кадра
/// целиком, не отделяя работу систем от работы движка. Вопрос «какая система тормозит»
/// ни тем, ни другим не решается, тогда как единая точка вызова делает его дешёвым.
///
/// ПОЧЕМУ ЗАМЕРЫ СВОДЯТСЯ В ИНТЕРВАЛЫ. Время одного шага скачет от кадра к кадру: на него
/// влияют уборка мусора, планировщик операционной системы и попадания в кеш процессора.
/// Показывать такое число, обновляемое шестьдесят раз в секунду, бессмысленно — прочесть
/// его нельзя. Поэтому шаги накапливаются интервалами по <see cref="BucketSeconds"/>,
/// и в окно попадает среднее время шага за интервал.
///
/// Замер стоит двух обращений к счётчику тактов на систему и ведётся только при
/// поднятом <see cref="DebugFlags.Profile"/>; при снятом признаке накопленное сбрасывается,
/// чтобы после повторного включения не сравнивать текущее время с давно устаревшим.
/// </summary>
public sealed class StepProfiler
{
    /// <summary>Длина интервала усреднения в секундах.</summary>
    public const double BucketSeconds = 0.25;

    /// <summary>Сколько интервалов держится в окне. Вместе с длиной даёт три секунды.</summary>
    public const int Buckets = 12;

    /// <summary>Длина окна хранения в секундах — по нему считается медиана.</summary>
    public const double WindowSeconds = Buckets * BucketSeconds;

    /// <summary>Сколько последних значений выводится на панель.</summary>
    public const int Shown = 3;

    /// <summary>Пороги превышения медианы, при которых последнее значение выделяется цветом.</summary>
    public const double WarnRatio = 1.15;

    public const double AlertRatio = 1.35;

    public const double AlarmRatio = 1.55;

    /// <summary>
    /// Ниже этого времени шага отношение к медиане не считается. У системы, которая
    /// укладывается в сотые доли миллисекунды, относительный разброс всегда велик,
    /// и раскраска по нему говорила бы только о погрешности измерения.
    /// </summary>
    public const double NoiseFloorMs = 0.02;

    /// <summary>Меньше этого числа интервалов медиана не считается представительной.</summary>
    public const int MinBuckets = 4;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Ключ — сама система, сравнение по ссылке: обёртки движка переопределяют сравнение,
    /// и у освобождённого узла оно небезопасно. Тот же приём применён в Index и Scheduler.
    /// </summary>
    private readonly Dictionary<GameSystem, Track> _bySystem =
        new(ReferenceEqualityComparer.Instance);

    private readonly List<Track> _order = new();

    private long _bucketStarted;
    private bool _running;

    /// <summary>
    /// Ряды замеров в порядке вызова: список пополняется при первом замере системы,
    /// а планировщик сбрасывает его при всяком изменении состава, поэтому порядок здесь
    /// всегда совпадает с порядком исполнения.
    /// </summary>
    public IReadOnlyList<Track> Tracks => _order;

    /// <summary>
    /// Растёт при каждом закрытии интервала. Панель по нему решает, пересобирать ли текст:
    /// делать это каждый кадр значило бы нагружать кадр тем самым измерением, ради которого
    /// всё и затевалось.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>
    /// Граница интервала. Зовётся из обоих циклов обновления; второй вызов за кадр
    /// ничего не делает, поскольку решение принимается по времени, а не по числу вызовов.
    /// </summary>
    public void Tick(bool enabled)
    {
        if (!enabled)
        {
            if (_running)
                Reset();

            return;
        }

        long now = Stopwatch.GetTimestamp();

        if (!_running)
        {
            _running = true;
            _bucketStarted = now;
            return;
        }

        if ((now - _bucketStarted) * MsPerTick < BucketSeconds * 1000.0)
            return;

        _bucketStarted = now;

        foreach (var track in _order)
            track.Close();

        Generation++;
    }

    /// <summary>Учесть один шаг системы. Время передаётся тактами, перевод — здесь.</summary>
    public void Record(GameSystem system, long ticks)
    {
        if (!_bySystem.TryGetValue(system, out var track))
        {
            track = new Track(system);
            _bySystem[system] = track;
            _order.Add(track);
        }

        track.Add(ticks * MsPerTick);
    }

    /// <summary>
    /// Забыть всё измеренное. Зовётся при изменении состава систем: порядок рядов выводится
    /// из порядка вызова, и после перестановки старый список сравнивать было бы не с чем.
    /// </summary>
    public void Reset()
    {
        _bySystem.Clear();
        _order.Clear();
        _running = false;
        Generation++;
    }

    /// <summary>Ряд замеров одной системы: кольцевое окно закрытых интервалов.</summary>
    public sealed class Track
    {
        private readonly double[] _closed = new double[Buckets];
        private readonly double[] _sorted = new double[Buckets];

        private double _sum;
        private int _steps;
        private int _count;
        private int _head;

        internal Track(GameSystem system)
        {
            Name = system.GetType().Name;
            Cycle = system.UpdateCycle;
        }

        public string Name { get; }

        public UpdateCycle Cycle { get; }

        /// <summary>Сколько интервалов уже закрыто; больше <see cref="Buckets"/> не бывает.</summary>
        public int Count => _count;

        internal void Add(double ms)
        {
            _sum += ms;
            _steps++;
        }

        /// <summary>
        /// Закрыть интервал средним временем шага. Интервал без единого шага не закрывается:
        /// нуль в окне занизил бы медиану, хотя система просто не получала управление.
        /// </summary>
        internal void Close()
        {
            if (_steps == 0)
                return;

            _closed[_head] = _sum / _steps;
            _head = (_head + 1) % Buckets;

            if (_count < Buckets)
                _count++;

            _sum = 0;
            _steps = 0;
        }

        /// <summary>Значение интервала: 0 — последний закрытый, 1 — предыдущий. Иначе NaN.</summary>
        public double Value(int back)
        {
            if (back < 0 || back >= _count)
                return double.NaN;

            return _closed[(_head - 1 - back + Buckets * 2) % Buckets];
        }

        /// <summary>
        /// Медиана окна — та величина, с которой сравнивается последнее значение. Именно
        /// медиана, а не среднее: единичный выброс сдвигает среднее и тем самым прячет сам
        /// себя, тогда как порядковая статистика к выбросам нечувствительна.
        /// </summary>
        public double Median()
        {
            if (_count < MinBuckets)
                return double.NaN;

            for (int i = 0; i < _count; i++)
                _sorted[i] = Value(i);

            Array.Sort(_sorted, 0, _count);

            return _count % 2 == 1
                ? _sorted[_count / 2]
                : (_sorted[_count / 2 - 1] + _sorted[_count / 2]) * 0.5;
        }
    }
}
