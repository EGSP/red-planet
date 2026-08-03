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

    /// <summary>Ячейка растра, пикселей. Меняется настройкой отображения.</summary>
    public int Cell => _cell;

    /// <summary>Ячеек по стороне. Выводится из размера мира и меняется вместе с ним.</summary>
    public int Width => Mathf.Max(1, World.SizePx / _cell);

    public int Area => Width * Width;

    /// <summary>
    /// Значения ячеек, строка за строкой. Отдаются как есть: отрисовка грузит их в текстуру
    /// целиком, и копия на каждое обновление была бы напрасной работой.
    /// </summary>
    public byte[] Values => _values;

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
    /// Глубоко внутри круга значение упирается в потолок шкалы, и там расстояние не считается
    /// вовсе — строка заполняется целиком. Корень извлекается только в полосе вокруг границы,
    /// то есть там, где значение вообще меняется.
    /// </summary>
    public void Stamp(Vector2 world, float radiusPx)
    {
        if (radiusPx <= 0f)
            return;

        int width = Width;
        float cell = _cell;

        float cx = (world.X - World.Min.X) / cell;
        float cy = (world.Y - World.Min.Y) / cell;

        float radius = radiusPx / cell;
        float range = RangePx / cell;

        // Снаружи значение падает до нуля через ту же полосу, поэтому штамп шире круга
        float outer = radius + range;
        float inner = Mathf.Max(radius - range, 0f);

        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - outer));
        int maxY = Mathf.Min(width - 1, Mathf.CeilToInt(cy + outer));

        for (int y = minY; y <= maxY; y++)
        {
            float dy = y + 0.5f - cy;
            float span = outer * outer - dy * dy;

            if (span <= 0f)
                continue;

            span = Mathf.Sqrt(span);

            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - span));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(cx + span));

            // Половина ширины строки, целиком лежащей в области потолка шкалы
            float innerSpan = inner * inner - dy * dy;
            innerSpan = innerSpan > 0f ? Mathf.Sqrt(innerSpan) : -1f;

            int row = y * width;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - cx;
                byte value;

                if (innerSpan > 0f && Mathf.Abs(dx) <= innerSpan)
                {
                    value = byte.MaxValue;
                }
                else
                {
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float level = 0.5f + (radius - distance) / (2f * range);

                    if (level <= 0f)
                        continue;

                    value = (byte)(Mathf.Min(level, 1f) * byte.MaxValue);
                }

                int at = row + x;

                if (_values[at] < value)
                    _values[at] = value;
            }
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
