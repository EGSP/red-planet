using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// Сводит поля зрения стороны игрока в одно поле расстояний и держит в согласии с ним
/// отрисовку: туман войны поверх карты и скрытие сущностей противника вне видимой области.
///
/// ПОЛЕ СЧИТАЕТСЯ НА ВИДЕОКАРТЕ — см. <see cref="VisionCompute"/>. Системе остаётся собрать
/// источники, разложить их по радиусам и отдать вычислению; растра в памяти процессора
/// больше нет вовсе.
///
/// ПОЧЕМУ СИСТЕМА, А НЕ ЛЕНИВАЯ КАРТА ПО ОБРАЗЦУ <see cref="NavGrid"/>. Растр навигации
/// выводится из прямоугольников зданий и потому пересобирается по изменению источника —
/// несколько раз в секунду в самом плотном случае. Источники зрения подвижны, и признака
/// «ничего не менялось» у них не бывает вовсе, поэтому пересчёт идёт по таймеру, а таймер
/// нужно кому-то вести. Этим и занимается система.
///
/// НА СИМУЛЯЦИЮ НЕ ВЛИЯЕТ. Скрытие сущности есть <c>Visible</c> у её узла, то есть чистая
/// отрисовка: цель по-прежнему выбирается, приказ по-прежнему отдаётся, урон по-прежнему
/// проходит. Превращение зрения в игровую механику потребует правки выбора целей,
/// наведения приказов и выделения — и делаться должно отдельно.
///
/// ИДЁТ В ГРАФИЧЕСКОМ ЦИКЛЕ. Поле описывает не состояние мира, а его вид, и потому
/// собирается по тем же положениям, по которым в этом кадре рисуются сами сущности.
/// В физическом цикле при несовпадении частот граница тумана отставала бы от юнитов
/// на переменную величину, и движение её читалось бы как дрожание.
/// </summary>
public partial class VisionSystem : GameSystem
{
    /// <summary>
    /// Ширина полосы вокруг границы, в которой значение поля линейно по расстоянию,
    /// пикселей в каждую сторону. Дальше значение упирается в край шкалы.
    ///
    /// Величина выбрана заметно больше ячейки: интерполяции нужно несколько текселей
    /// с промежуточными значениями, иначе линейный участок вырождается и точность границы
    /// падает до размера ячейки.
    /// </summary>
    public const float RangePx = 96f;

    /// <summary>Настройки отображения. Их же правит панель отладки.</summary>
    [Export] public FogSettings Settings;

    private readonly VisionCompute _compute = new();

    /// <summary>
    /// Источники обзора игрока по месту. Отвечает на вопрос «видит ли кто-нибудь эту точку»
    /// при скрытии противника: поле расстояний живёт в памяти видеокарты и прочитано быть
    /// не может, да и не должно — вопрос этот к отрисовке отношения не имеет.
    ///
    /// Сетка своя, а не общая из <see cref="WorldSpace"/>, поскольку та пересобирается раз
    /// в физический шаг, а зрение работает в графическом цикле. Пересборка стоит одного
    /// прохода по источникам и на фоне запросов не заметна.
    /// </summary>
    private readonly SpatialGrid<IVision> _eyes = new(WorldSpace.BucketPx);

    /// <summary>Точка нынешнего запроса к сетке источников — см. <see cref="Spots"/>.</summary>
    private Vector2 _askAt;

    private readonly Func<IVision, Vector2> _eyeAt = eye => eye.GlobalPosition;
    private Func<IVision, bool> _eyeCovers;

    /// <summary>Источники по радиусу: партия на каждое значение из справочников.</summary>
    private readonly Dictionary<int, List<Vector2>> _byRadius = new();

    private readonly List<int> _radii = new();

    private FogRenderer _renderer;

    /// <summary>Сколько осталось до следующей пересборки поля, секунд.</summary>
    private float _cooldown;

    /// <summary>Скрывали ли противника в прошлом шаге: выключение тоже требует прохода.</summary>
    private bool _hiding;

    /// <summary>Сторона поля в ячейках. Выводится из размера мира и размера ячейки.</summary>
    public int Width { get; private set; }

    /// <summary>Ячейка поля, пикселей.</summary>
    public int CellPx { get; private set; } = Const.NavCell;

    /// <summary>Сколько источников участвовало в последней пересборке.</summary>
    public int Sources { get; private set; }

    /// <summary>Сколько партий по радиусу вышло в последней пересборке.</summary>
    public int Batches { get; private set; }

    /// <summary>
    /// Во что обошёлся сбор источников, миллисекунд. Само вычисление поля идёт на видеокарте
    /// и в это число не входит: с главного потока его длительность не измеряется.
    /// </summary>
    public double LastBuildMs { get; private set; }

    /// <summary>Сколько сущностей противника скрыто сейчас. Показывает панель отладки.</summary>
    public int Hidden { get; private set; }

    protected override void OnRegister()
    {
        // Настройки в сцене могли и не назначить: без них система должна работать
        // на умолчаниях, а не ронять сессию
        Settings ??= new FogSettings();

        // Замыкание заводится один раз: сетка спрашивает пригодность кандидата вызовом,
        // и создавать делегат на каждый запрос значило бы сорить памятью каждый кадр
        _eyeCovers = eye =>
        {
            float radius = eye.VisionRadius;
            return radius > 0f && eye.GlobalPosition.DistanceSquaredTo(_askAt) <= radius * radius;
        };
    }

    public override void Step(double dt)
    {
        EnsureNodes();
        UpdateField(dt);
        Smooth(dt);
        ApplyVisibility();
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _compute.Release();
    }

    public override void CaptureSnapshot(JsonObject data)
    {
        data["hidden"] = Hidden;
        data["hide_enemies"] = Settings.HideEnemies;
        data["every_frame"] = Settings.EveryFrame;
        data["update_hz"] = Settings.UpdateHz;
        data["cell_px"] = CellPx;
        data["width"] = Width;
        data["sources"] = Sources;
        data["batches"] = Batches;
        data["last_build_ms"] = LastBuildMs;
    }

    private void EnsureNodes()
    {
        if (_renderer == null || !IsInstanceValid(_renderer))
        {
            _renderer = GM.Playground.Add(WorldLayer.Fog, new FogRenderer());
            _renderer.Settings = Settings;
        }

        _compute.Publish();
        _renderer.Source = _compute.Ready ? _compute.Texture : null;
    }

    /// <summary>
    /// Пересобрать поле, если подошёл срок либо изменился размер мира. Второе условие важнее
    /// первого: после правки настроек мира сторона поля уже другая, и ждать с ней до конца
    /// отсчёта значило бы показывать поле от прежней карты.
    /// </summary>
    private void UpdateField(double dt)
    {
        int cell = Mathf.Clamp(Settings.CellPx, 4, Const.Unit);
        int width = Mathf.Max(1, World.SizePx / cell);

        bool resized = cell != CellPx || width != Width;

        CellPx = cell;
        Width = width;

        _cooldown -= (float)dt;

        if (!Settings.EveryFrame && _cooldown > 0f && !resized)
            return;

        _cooldown = 1f / Mathf.Max(Settings.UpdateHz, 1f);

        Rebuild();
    }

    /// <summary>
    /// Собрать источники стороны игрока, разложить по радиусам и отдать вычислению.
    ///
    /// РАЗЛОЖЕНИЕ ПО РАДИУСАМ нужно потому, что квадрат обхода вокруг источника зависит
    /// от радиуса, а один запуск вычисления задаёт этот квадрат всем своим потокам сразу.
    /// Радиусы приходят из справочников и потому повторяются у всех сущностей одного вида,
    /// так что партий выходит единицы при любой численности войск.
    /// </summary>
    private void Rebuild()
    {
        ulong started = Time.GetTicksUsec();

        foreach (var list in _byRadius.Values)
            list.Clear();

        float cell = CellPx;
        var min = World.Min;
        int count = 0;

        foreach (var source in GM.Index.All<IVision>())
        {
            if (source.Faction != Faction.Player || source.VisionRadius <= 0f)
                continue;

            if (count >= VisionCompute.MaxSources)
                break;

            int key = Mathf.RoundToInt(source.VisionRadius);

            if (!_byRadius.TryGetValue(key, out var list))
                _byRadius[key] = list = new List<Vector2>();

            list.Add((source.GlobalPosition - min) / cell);
            count++;
        }

        _radii.Clear();

        foreach (var pair in _byRadius)
            if (pair.Value.Count > 0)
                _radii.Add(pair.Key);

        // Массивы заводятся заново на каждую пересборку: их читает поток отрисовки уже после
        // того, как главный поток ушёл в следующий кадр, поэтому повторно использовать
        // хранилище нельзя — правка на месте пришлась бы прямо на чтение
        var positions = new float[count * 2];
        var groups = new float[_radii.Count * 4];

        float range = RangePx / cell;
        int at = 0;

        for (int i = 0; i < _radii.Count; i++)
        {
            int key = _radii[i];
            var list = _byRadius[key];
            float radius = key / cell;

            groups[i * 4] = at;
            groups[i * 4 + 1] = list.Count;

            // Снаружи значение падает до нуля через ту же полосу, поэтому квадрат обхода
            // шире круга. Запас в ячейку покрывает то, что середина источника лежит
            // не в углу своей ячейки, а где придётся
            groups[i * 4 + 2] = Mathf.CeilToInt(radius + range) + 1;
            groups[i * 4 + 3] = radius;

            for (int j = 0; j < list.Count; j++, at++)
            {
                positions[at * 2] = list[j].X;
                positions[at * 2 + 1] = list[j].Y;
            }
        }

        var bytes = new byte[positions.Length * sizeof(float)];
        Buffer.BlockCopy(positions, 0, bytes, 0, bytes.Length);

        _compute.Rebuild(Width, bytes, groups, range);

        Sources = count;
        Batches = _radii.Count;
        LastBuildMs = (Time.GetTicksUsec() - started) / 1000.0;
    }

    /// <summary>
    /// Подвинуть показываемое поле к собранному. Идёт каждый кадр независимо от того,
    /// пересобиралось ли поле: пересборка задаёт цель, а это движение к ней.
    ///
    /// Доля смещения выведена из показательного закона, поэтому она не зависит от частоты
    /// кадров: скорость догона задана за секунду, и при любом делении секунды на кадры
    /// за секунду проходится одна и та же доля пути. Нулевая скорость означает показ
    /// без сглаживания.
    /// </summary>
    private void Smooth(double dt)
    {
        float rate = Settings.CatchUp;
        float part = rate <= 0f ? 1f : 1f - Mathf.Exp(-rate * (float)dt);

        _compute.Smooth(Width, part);
    }

    /// <summary>
    /// Скрыть противника вне поля зрения. Проход делается и в том шаге, когда скрытие
    /// выключили: иначе скрытое так и осталось бы невидимым.
    /// </summary>
    private void ApplyVisibility()
    {
        bool hide = Settings.HideEnemies;

        if (!hide && !_hiding)
            return;

        _hiding = hide;

        if (hide)
            FillEyes();

        int hidden = 0;

        foreach (var unit in GM.Units[Faction.Hostile])
        {
            bool visible = !hide || Spots(unit.GlobalPosition);
            unit.Visible = visible;

            if (!visible)
                hidden++;
        }

        // Снаряды противника скрываются вместе с ним: летящий из пустоты выстрел выдавал бы
        // положение стрелка вернее, чем сам стрелок. Сторона снаряда названа целью, поэтому
        // выпущенный противником — тот, чья цель игрок
        foreach (var projectile in GM.Index.All<Projectile>())
        {
            if (projectile.TargetSide != Faction.Player)
                continue;

            bool visible = !hide || Spots(projectile.GlobalPosition);
            projectile.Visible = visible;

            if (!visible)
                hidden++;
        }

        Hidden = hidden;
    }

    /// <summary>Разложить источники обзора игрока по месту перед запросами этого кадра.</summary>
    private void FillEyes()
    {
        _eyes.Clear();

        foreach (var eye in GM.Index.All<IVision>())
            if (eye.Faction == Faction.Player && eye.VisionRadius > 0f)
                // Радиус обзора кладётся как удаление поверхности от середины: сетка ведёт
                // наибольшее такое удаление по стороне, а оно и есть предел обхода запроса
                _eyes.Add(eye, eye.GlobalPosition, eye.Faction, eye.VisionRadius);
    }

    /// <summary>
    /// Видит ли хоть один источник игрока указанную точку.
    ///
    /// Запрос идёт поиском ближайшего годного, а не перебором всех источников: годность
    /// проверяется собственным радиусом кандидата, а предел обхода назначен наибольшим
    /// радиусом по стороне. Ответ нужен только двоичный, поэтому сам найденный отбрасывается.
    /// </summary>
    private bool Spots(Vector2 point)
    {
        if (!_eyes.Has(Faction.Player))
            return false;

        _askAt = point;

        return _eyes.Nearest(point, Faction.Player, _eyes.ExtentOf(Faction.Player),
            _eyeAt, _eyeCovers) != null;
    }
}
