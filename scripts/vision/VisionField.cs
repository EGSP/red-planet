using Godot;

/// <summary>
/// Растр видимости: для каждой ячейки поля — расстояние до границы поля зрения стороны игрока.
///
/// ПОЧЕМУ РАСТР, А НЕ НАБОР КРУГОВ. Полей зрения на карте десятки, и они перекрываются.
/// Полупрозрачные круги при наложении складываются по альфе и дают пятна разной плотности
/// вместо однородной области, а общей границы объединения из них не получить вовсе. Растр
/// же отвечает на оба вопроса сразу: чем закрыть карту и где проходит граница видимого.
///
/// ХРАНИТСЯ РАССТОЯНИЕ, А НЕ ПРИЗНАК ВИДИМОСТИ. Ячейка держит значение
/// <c>0.5 + (радиус − расстояние) / (2 · Range)</c>, обрезанное по краям диапазона: половина
/// шкалы означает саму границу, больше половины — внутреннюю сторону, меньше — внешнюю.
/// Такое значение линейно по расстоянию в полосе шириной <see cref="RangePx"/> вокруг границы,
/// поэтому линейная фильтрация текстуры восстанавливает положение границы ТОЧНЕЕ размера
/// ячейки: между двумя соседними значениями граница находится делением отрезка, а не
/// приходится на середину ячейки. Отсюда гладкая обводка даже на грубом растре — тогда как
/// двоичная маска давала ступеньки величиной в ячейку и рваную линию при отдалении камеры.
///
/// Объединение полей зрения есть взятие наибольшего значения: расстояние до ближайшей
/// границы и получается наибольшим из расстояний до границ отдельных кругов.
///
/// РАЗМЕР ПОЛЯ — СЛЕДСТВИЕ РАЗМЕРА МИРА, а не самостоятельное число: <see cref="Width"/>
/// вычисляется из <see cref="World.SizePx"/> при каждом обращении, а массив подгоняется
/// под него в <see cref="Fit"/>. Поэтому изменение настроек мира — в том числе прямо
/// во время работы редактора — растр переживает без пересборки проекта. Там же меняется
/// и размер ячейки: он задан настройкой, поскольку подбирается глазом как соотношение
/// между чёткостью формы и стоимостью пересборки.
///
/// НА ЛОГИКУ НЕ ВЛИЯЕТ. Растр читает только отрисовка: выбор целей, наведение приказов
/// и выделение по-прежнему видят всю карту. Туман войны как игровая механика — отдельный
/// разговор, и вводить её следует вместе с правкой этих систем, а не заодно с картинкой.
/// </summary>
public sealed class VisionField
{
    /// <summary>С какого значения ячейка считается просматриваемой — половина шкалы.</summary>
    public const byte Threshold = 128;

    /// <summary>
    /// Ширина полосы вокруг границы, в которой значение линейно по расстоянию, пикселей
    /// в каждую сторону. Дальше значение упирается в край шкалы.
    ///
    /// Величина выбрана заметно больше ячейки: интерполяции нужно несколько текселей
    /// с промежуточными значениями, иначе линейный участок вырождается и точность границы
    /// снова падает до размера ячейки. Слишком широкая полоса тоже вредна — она съедает
    /// разрешение шкалы, которой всего 256 ступеней.
    /// </summary>
    public const float RangePx = 96f;

    private int _cell = Const.NavCell;

    private byte[] _values = new byte[1];

    /// <summary>
    /// Растр, который показывается, — он догоняет собранный, а не совпадает с ним.
    ///
    /// ЗАЧЕМ ОТДЕЛЬНЫЙ. Собранный растр меняется скачком: пересборка идёт двадцать раз
    /// в секунду, и граница видимого прыгала бы вслед за юнитами ступенями. Показываемый же
    /// каждый кадр смещается к собранному на долю оставшейся разницы, отчего граница
    /// движется непрерывно. Скорость догона задаётся настройкой.
    ///
    /// СГЛАЖИВАТЬ МОЖНО ИМЕННО ЭТИ ЗНАЧЕНИЯ. В ячейке лежит расстояние до границы, а не
    /// признак видимости, поэтому среднее двух состояний само есть расстояние, и граница
    /// у него проходит между прежней и новой. Со двоичной маской такое сглаживание дало бы
    /// полупрозрачную кашу вместо движения границы.
    /// </summary>
    private byte[] _shown = new byte[1];

    /// <summary>Ячейка растра, пикселей. Меняется настройкой отображения.</summary>
    public int CellPx => _cell;

    /// <summary>Ячеек по стороне. Выводится из размера мира и меняется вместе с ним.</summary>
    public int Width => Mathf.Max(1, World.SizePx / _cell);

    public int Area => Width * Width;

    /// <summary>
    /// Значения ячеек, строка за строкой. Отдаются как есть: отрисовка грузит их в текстуру
    /// целиком, и копия на каждое обновление была бы напрасной работой.
    /// </summary>
    public byte[] Values => _values;

    /// <summary>Растр для отрисовки: догоняет собранный со скоростью из настроек.</summary>
    public byte[] Shown => _shown;

    /// <summary>Сколько раз растр пересобирался. По нему отрисовка узнаёт, что пора обновить текстуру.</summary>
    public int Revision { get; private set; }

    /// <summary>Сколько источников участвовало в последней пересборке.</summary>
    public int Sources { get; private set; }

    /// <summary>Во что обошлась последняя пересборка, миллисекунд. Показывает панель отладки.</summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Задать размер ячейки. Значения вне разумного предела отсекаются.</summary>
    public void Resize(int cellPx)
    {
        cellPx = Mathf.Clamp(cellPx, 4, Const.Unit);

        if (cellPx != _cell)
            _cell = cellPx;
    }

    /// <summary>
    /// Подогнать массив под текущий размер мира и ячейки. Возвращает true, если размер
    /// менялся, — по этому признаку отрисовка пересоздаёт текстуру, у которой размер
    /// задан при создании.
    /// </summary>
    public bool Fit()
    {
        if (_values.Length == Area)
            return false;

        _values = new byte[Area];
        _shown = new byte[Area];
        return true;
    }

    /// <summary>
    /// Пересобрать растр по всем зрячим указанной стороны. Полностью, без попыток обновлять
    /// область изменения: источники подвижны, и вычислять эту область дороже, чем заново
    /// заполнить поле.
    /// </summary>
    public void Rebuild(Slice<IVision> sources, Faction side)
    {
        ulong started = Time.GetTicksUsec();

        Fit();
        System.Array.Clear(_values);

        int count = 0;

        foreach (var source in sources)
        {
            if (source.Faction != side || source.VisionRadius <= 0f)
                continue;

            Stamp(source.GlobalPosition, source.VisionRadius);
            count++;
        }

        Sources = count;
        Revision++;
        LastBuildMs = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>
    /// Наложить круг обзора. Значения складываются взятием наибольшего: расстояние до
    /// ближайшей границы объединения и есть наибольшее из расстояний до границ кругов.
    ///
    /// КРУГ БЕРЁТСЯ ГОТОВЫМ. Прежде значение каждой ячейки считалось на месте, с извлечением
    /// корня в полосе вокруг границы; при трёх сотнях источников на одну пересборку уходило
    /// свыше тридцати миллисекунд, то есть два кадра. Между тем круг зависит только
    /// от радиуса, а радиусов в игре ровно столько, сколько их в справочниках, — единицы.
    /// Поэтому круг считается один раз и дальше накладывается сравнением байтов.
    ///
    /// ЦЕНА ЗАГОТОВКИ — ПРИВЯЗКА К СЕТКЕ. Готовый круг можно положить только по целым
    /// ячейкам, поэтому источник округляется до ячейки, в которой стоит. Граница видимого
    /// смещается на половину ячейки, то есть на восемь пикселей при нынешней настройке;
    /// величина эта меньше ширины полосы сглаживания и глазом не различается.
    /// </summary>
    public void Stamp(Vector2 world, float radiusPx)
    {
        if (radiusPx <= 0f)
            return;

        var mask = MaskFor(radiusPx, out int span);

        if (mask == null)
            return;

        int width = Width;
        int side = 2 * span + 1;

        int ix = Mathf.FloorToInt((world.X - World.Min.X) / _cell);
        int iy = Mathf.FloorToInt((world.Y - World.Min.Y) / _cell);

        int minY = Mathf.Max(0, iy - span);
        int maxY = Mathf.Min(width - 1, iy + span);
        int minX = Mathf.Max(0, ix - span);
        int maxX = Mathf.Min(width - 1, ix + span);

        for (int y = minY; y <= maxY; y++)
        {
            int at = y * width + minX;
            int from = (y - iy + span) * side + (minX - ix + span);

            for (int x = minX; x <= maxX; x++, at++, from++)
            {
                byte value = mask[from];

                if (_values[at] < value)
                    _values[at] = value;
            }
        }
    }

    /// <summary>
    /// Заготовки кругов по радиусам. Ключ — радиус в целых пикселях: радиусы приходят
    /// из справочников и потому повторяются у всех сущностей одного вида.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<int, byte[]> _masks = new();

    /// <summary>Полуширина заготовки в ячейках, по тому же ключу.</summary>
    private readonly System.Collections.Generic.Dictionary<int, int> _maskSpans = new();

    /// <summary>При каком размере ячейки заготовлены круги. Смена размера обесценивает их все.</summary>
    private int _maskCell;

    /// <summary>
    /// Заготовка круга указанного радиуса: значения шкалы для квадрата со стороной
    /// <c>2·span+1</c> ячеек, где середина квадрата есть ячейка источника.
    /// </summary>
    private byte[] MaskFor(float radiusPx, out int span)
    {
        if (_maskCell != _cell)
        {
            _maskCell = _cell;
            _masks.Clear();
            _maskSpans.Clear();
        }

        int key = Mathf.RoundToInt(radiusPx);

        if (_masks.TryGetValue(key, out var found))
        {
            span = _maskSpans[key];
            return found;
        }

        float cell = _cell;
        float radius = key / cell;
        float range = RangePx / cell;

        // Снаружи значение падает до нуля через ту же полосу, поэтому заготовка шире круга
        span = Mathf.CeilToInt(radius + range);

        int side = 2 * span + 1;
        var mask = new byte[side * side];

        for (int y = 0; y < side; y++)
        {
            float dy = y - span;

            for (int x = 0; x < side; x++)
            {
                float dx = x - span;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float level = 0.5f + (radius - distance) / (2f * range);

                if (level <= 0f)
                    continue;

                mask[y * side + x] = (byte)(Mathf.Min(level, 1f) * byte.MaxValue);
            }
        }

        _masks[key] = mask;
        _maskSpans[key] = span;

        return mask;
    }

    /// <summary>
    /// Сместить показываемый растр к собранному. Зовётся каждый кадр, а не каждую пересборку:
    /// в этом и состоит сглаживание.
    ///
    /// Доля смещения выведена из показательного закона, поэтому она не зависит от частоты
    /// кадров: <paramref name="rate"/> есть скорость догона за секунду, и при любом делении
    /// секунды на кадры за секунду проходится одна и та же доля пути.
    ///
    /// ШАГ НЕ МЕНЬШЕ ЕДИНИЦЫ ШКАЛЫ. Значения целые, и доля от разницы в единицу округляется
    /// в ноль: без этого правила остаток разницы не сходился бы никогда, а граница замирала
    /// бы в пикселе от нужного места.
    /// </summary>
    public void Approach(double dt, float rate)
    {
        if (_shown.Length != _values.Length)
        {
            _shown = new byte[_values.Length];
            System.Array.Copy(_values, _shown, _values.Length);
            return;
        }

        // Нулевая скорость означает показ без сглаживания
        if (rate <= 0f)
        {
            System.Array.Copy(_values, _shown, _values.Length);
            return;
        }

        float part = 1f - Mathf.Exp(-rate * (float)dt);

        for (int i = 0; i < _shown.Length; i++)
        {
            int target = _values[i];
            int now = _shown[i];

            if (target == now)
                continue;

            int step = (int)((target - now) * part);

            if (step == 0)
                step = target > now ? 1 : -1;

            _shown[i] = (byte)(now + step);
        }
    }

    /// <summary>
    /// Значение в мировой точке. За границами поля — ноль: там не видно ничего,
    /// и отдельного состояния «вне карты» заводить не нужно.
    /// </summary>
    public byte At(Vector2 world)
    {
        int width = Width;

        if (_values.Length != width * width)
            return 0;

        int x = Mathf.FloorToInt((world.X - World.Min.X) / _cell);
        int y = Mathf.FloorToInt((world.Y - World.Min.Y) / _cell);

        if (x < 0 || y < 0 || x >= width || y >= width)
            return 0;

        return _values[y * width + x];
    }

    /// <summary>Просматривается ли точка. Порог тот же, по которому отрисовка проводит обводку.</summary>
    public bool IsVisible(Vector2 world) => At(world) >= Threshold;
}
