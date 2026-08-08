using System.Collections.Generic;
using Godot;

/// <summary>
/// Одна прошедшая волна в том виде, в каком её показывает отладочная панель.
///
/// Держится в памяти подсистемы, а не восстанавливается перебором событий: панель
/// обновляется каждый кадр, а событие пишется ради воспроизводимости партии. Задачи
/// разные, и цена обращения у них разная.
/// </summary>
public sealed class WaveRecord
{
    public float GameTime;
    public string WaveId;
    public float Terror;
    public float Budget;
    public float Spent;
    public string Composition;
    public float CenterAngleDegrees;
    public int Groups;
    public float ChillSeconds;

    /// <summary>Насколько форма оказалась глубже заданной, рядов. Ноль — состав уместился.</summary>
    public int ExtraRows;
}

/// <summary>
/// Волны: напряжение в моменте поверх постоянного фона.
///
/// ЧЕМ ВОЛНА ОТЛИЧАЕТСЯ ОТ ФОНА, ПОМИМО РАЗМЕРА. Фон ограничен бюджетом, выведенным
/// из террора, и восполняет потери по таймеру; волна бюджетом фона не ограничена
/// и приходит целиком как одно событие — состав набирается разом, а очаги могут
/// выходить с паузой <see cref="WaveShape.GroupDelaySeconds"/> между собой.
/// Её юниты помечаются признаком <see cref="PressureOrigin.Wave"/>
/// и в занятое место фона не входят — иначе после волны фон замер бы на всё время жизни
/// пришедших, и за пиком следовала бы тишина неопределённой длины.
///
/// ЧТО РЕШАЕТ СИСТЕМА, А ЧТО СОДЕРЖИМОЕ. Система не знает ни одной волны поимённо: она
/// отбирает подходящую по террору, считает её бюджет, набирает состав по её же правилам
/// и раскладывает его по её форме. Всё, что можно решить в файле, решается в файле.
///
/// ФОН ПРИ ЭТОМ НЕ ОСТАНАВЛИВАЕТСЯ. <see cref="PressureSystem"/> продолжает работать
/// по своему таймеру, включая миг прихода волны. Останавливать его значило бы вводить
/// между подсистемами связь, которой в замысле нет, и подбирать их пришлось бы вместе.
///
/// ПОРТАЛ ВЫЗЫВАЕТ ВОЛНУ ВНЕ ОЧЕРЕДИ. Достройка портала — заявка на победу, и ответ на неё
/// не должен зависеть от того, сколько случайно осталось до конца отдыха. Отбор при этом
/// обычный: приходит та волна, которая пришла бы и так, только немедленно. Отдых после неё
/// назначается заново, поэтому очередная волна на вызванную не накладывается.
///
/// ОБЩЕЙ ЦЕЛИ У ВОЛНЫ НЕТ. Выйдя на карту, её юниты живут по той же логике, что и фоновые:
/// цель каждому раздаёт <see cref="EnemyAiSystem"/> поштучно. Поэтому форма появления
/// определяет только первые секунды — какая часть периметра встречает удар, — а дальше
/// строй расходится. Это осознанное ограничение первой версии, а не упущение.
/// </summary>
public partial class WaveSystem : GameSystem
{
    /// <summary>
    /// Очаг, ждущий своего часа. Места считаются в миг запуска волны, чтобы пауза
    /// не сдвигала построение относительно уже вышедших соседей.
    /// </summary>
    private sealed class PendingGroup
    {
        public float Remaining;
        public List<(UnitDefinition Definition, Vector2 Position)> Units;
    }

    /// <summary>Константа отдыха, разброс и умолчания формы появления.</summary>
    [Export] public WaveSettings Settings;

    /// <summary>
    /// Настройки мира. Нужны ради одного числа — радиуса окружности появления, от которого
    /// отсчитывается форма. Поле здесь затем, чтобы связь была видна в сцене: подвинув поле
    /// точек метала, вы двигаете и линию подхода волн.
    /// </summary>
    [Export] public WorldSettings WorldTuning;

    /// <summary>
    /// Во сколько раз запрошенный предыдущей волной тег поднимает шанс волны с этим тегом.
    ///
    /// Именно множитель, а не выбор напрямую: пожелание должно оставаться пожеланием.
    /// Жёсткая связка «после накатa всегда кулак» превратила бы последовательность волн
    /// в сценарий, который запоминается со второй партии.
    /// </summary>
    private const float PreferenceFactor = 3f;

    /// <summary>Сколько волн держать в истории для отладочной панели.</summary>
    private const int HistoryLimit = 20;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<WaveRecord> _history = new();

    /// <summary>Набор состава. Тот же класс зовёт предпросмотр в редакторе.</summary>
    private readonly WaveComposer _composer = new();

    // Рабочие списки живут полем, а не переменной шага: отбор идёт раз в полторы минуты,
    // но выделять под него память заново незачем
    private readonly List<WaveDefinition> _candidates = new();
    private readonly List<float> _weights = new();

    /// <summary>Состав по убыванию мощи — в том порядке, в каком он встаёт по рядам.</summary>
    private readonly List<UnitDefinition> _ordered = new();

    /// <summary>Очаги текущей (и, редко, предыдущей) волны, ещё не вышедшие на карту.</summary>
    private readonly List<PendingGroup> _pending = new();

    private string[] _preferred = System.Array.Empty<string>();
    private float _timer;
    private float _gameTime;

    /// <summary>
    /// Портал появился в мире, и очередная волна должна выйти вне отдыха. Флаг, а не запуск
    /// прямо из обработчика: достройка каркаса идёт в середине кадра, и создавать там же
    /// два десятка противников значило бы менять состав мира внутри чужого шага.
    /// </summary>
    private bool _portalCall;

    /// <summary>Сколько осталось до ближайшей волны, секунд. Показывается в отладочной панели.</summary>
    public float TimeLeft => Mathf.Max(_timer, 0f);

    /// <summary>
    /// Время партии, секунд. По этой же шкале записано <see cref="WaveRecord.GameTime"/>,
    /// поэтому полоса событий сравнивает историю с текущим мигом напрямую.
    /// </summary>
    public float GameTime => _gameTime;

    /// <summary>Прошедшие волны, от старых к новым.</summary>
    public IReadOnlyList<WaveRecord> History => _history;

    /// <summary>Сколько очагов уже набрано, но ещё не выведено на карту.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// Через сколько секунд выйдет очаг с этим номером. Порядок в списке произвольный:
    /// очаги удаляются по мере выхода, поэтому номер служит только перебору.
    /// </summary>
    public float PendingIn(int index) => Mathf.Max(_pending[index].Remaining, 0f);

    protected override void OnRegister()
    {
        _rng.Randomize();

        if (Settings == null)
        {
            // Без настроек система обязана работать: незаполненное поле в инспекторе
            // не должно отменять волны на всю партию
            GD.PushWarning("[WaveSystem] настройки не назначены, взяты умолчания");
            Settings = new WaveSettings();
        }

        // Незаполненное поле берёт действующие настройки мира: иначе форма появления
        // считалась бы от умолчаний класса и расходилась бы с той, что видит игрок
        WorldTuning ??= World.Settings;

        _timer = Settings.FirstDelay;
    }

    protected override void OnLink()
    {
        GM.Events.Stream<BuildingSpawned>().Appended += OnBuildingSpawned;
    }

    /// <summary>
    /// Достроенный портал вызывает волну немедленно. Смысл в том, что портал есть заявка
    /// на победу, и ответ на неё не должен зависеть от того, сколько случайно осталось
    /// до конца отдыха: иначе один и тот же поступок игрока стоил бы то полутора минут
    /// спокойствия, то ничего.
    /// </summary>
    private void OnBuildingSpawned(BuildingSpawned record)
    {
        if (record.DefinitionId == VictorySystem.PortalId)
            _portalCall = true;
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null)
            return;

        float step = (float)dt;
        _gameTime += step;

        // Отложенные очаги тикают независимо от отдыха: пауза между ними короче интервала
        // между волнами, и следующий отбор не должен их «забыть»
        TickPending(step);

        _timer -= step;

        // Вызов порталом расходуется здесь же, вне зависимости от исхода отбора: повторно
        // он придёт только со следующим порталом
        bool called = _portalCall;
        _portalCall = false;

        if (_timer > 0f && !called)
            return;

        float terror = GM.System<TerrorSystem>()?.Smoothed ?? 0f;
        var wave = Pick(terror);

        if (wave == null)
        {
            // Подходящей волны нет — показатель ещё не дорос либо уже перерос все границы.
            // Это не ошибка: пусто ждём одну постоянную и пробуем снова
            _timer = Mathf.Max(Settings.ChillInterval, 1f);
            return;
        }

        Launch(wave, terror, called);
    }

    // ── Отбор ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Подходящая волна. Среди применимых выбор равновероятен, а теги, запрошенные
    /// предыдущей волной, дают своим носителям перевес. Собственного веса у волны нет:
    /// он был бы вторым регулятором частоты и требовал бы согласования с первым.
    /// </summary>
    private WaveDefinition Pick(float terror)
    {
        _candidates.Clear();
        _weights.Clear();

        float total = 0f;

        foreach (var wave in GM.Catalog.Waves)
        {
            if (!wave.Fits(terror))
                continue;

            float weight = Preferred(wave) ? PreferenceFactor : 1f;

            _candidates.Add(wave);
            _weights.Add(weight);
            total += weight;
        }

        if (_candidates.Count == 0)
            return null;

        float roll = _rng.RandfRange(0f, total);

        for (int i = 0; i < _candidates.Count; i++)
        {
            roll -= _weights[i];

            if (roll <= 0f)
                return _candidates[i];
        }

        return _candidates[^1];
    }

    private bool Preferred(WaveDefinition wave)
    {
        foreach (string tag in _preferred)
            if (wave.HasTag(tag))
                return true;

        return false;
    }

    // ── Запуск ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Вывести волну на карту. <paramref name="byPortal"/> означает, что волна пришла
    /// по достройке портала, а не по отдыху; на состав и форму это не влияет — различие
    /// видно только в журнале. Отдых назначается заново в любом случае, поэтому вызванная
    /// порталом волна не накладывается на очередную.
    /// </summary>
    private void Launch(WaveDefinition wave, float terror, bool byPortal = false)
    {
        float budget = wave.Budget(terror);

        _composer.Compose(GM.Catalog, wave, budget, _rng);

        var shape = Settings.ShapeOf(wave.Shape, WorldTuning.SpawnRadiusPx);
        float center = _rng.RandfRange(0f, Mathf.Tau);
        int extraRows = Deploy(shape, center);

        float spent = _composer.Spent;
        float chill = Settings.ChillAfter(wave, _rng);

        _timer = chill;
        _preferred = wave.PreferNext;

        Record(wave, terror, budget, spent, shape, center, chill, extraRows, byPortal);
    }

    // ── Расстановка ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Разложить набранный состав по форме и вывести на карту. Возвращает, на сколько рядов
    /// форма оказалась глубже заданной.
    ///
    /// Состав делится по очагам поочерёдно, вид за видом, поэтому очаги получают примерно
    /// равные доли мощи и смешанный состав. Разделение по видам («очаг толстяков и очаг
    /// стрелков») этим не выражается — и не должно: оно и так получается двумя волнами
    /// подряд через малый множитель отдыха.
    ///
    /// Первый очаг выходит сразу; каждый следующий — через
    /// <see cref="WaveShape.GroupDelaySeconds"/> после предыдущего. Ноль паузы сохраняет
    /// прежнее поведение: все очаги в одном шаге.
    /// </summary>
    private int Deploy(WaveShape shape, float center)
    {
        _ordered.Clear();
        _ordered.AddRange(_composer.Composition);

        if (_ordered.Count == 0)
            return 0;

        // Крупные идут в передние ряды: иначе живучие плетутся позади и вступают в бой
        // последними, хотя вся их роль — принять первый удар
        _ordered.Sort((a, b) => b.ArmyPower.CompareTo(a.ArmyPower));

        int groups = Mathf.Min(shape.Groups, _ordered.Count);
        float delay = shape.GroupDelaySeconds;
        int extra = 0;

        for (int g = 0; g < groups; g++)
        {
            float angle = WaveFormation.GroupAngle(shape, center, g);
            var units = new List<(UnitDefinition Definition, Vector2 Position)>();
            int index = 0;

            for (int i = g; i < _ordered.Count; i += groups)
            {
                var (position, row) = WaveFormation.Slot(shape, angle, index++);
                units.Add((_ordered[i], position));
                extra = Mathf.Max(extra, row);
            }

            float when = delay > 0f ? g * delay : 0f;

            if (when <= 0f)
                ReleaseGroup(units);
            else
                _pending.Add(new PendingGroup { Remaining = when, Units = units });
        }

        return Mathf.Max(extra + 1 - WaveFormation.Rows(shape), 0);
    }

    private void TickPending(float dt)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var group = _pending[i];
            group.Remaining -= dt;

            if (group.Remaining > 0f)
                continue;

            ReleaseGroup(group.Units);
            _pending.RemoveAt(i);
        }
    }

    private void ReleaseGroup(List<(UnitDefinition Definition, Vector2 Position)> units)
    {
        foreach (var (definition, position) in units)
            Spawn(definition, position);
    }

    private void Spawn(UnitDefinition definition, Vector2 position)
    {
        var enemy = GM.Spawn.SpawnUnit(definition, position, Faction.Hostile);
        enemy.Origin = PressureOrigin.Wave;

        // Появился — уже смотрит на базу: иначе первый ход выглядит как разворот на месте
        enemy.Rotation = Heading.AngleTo(position, Vector2.Zero);
        enemy.SnapToolToBody();

        GM.Events.Append(new EnemySpawned
        {
            EntityId = enemy.Id,
            DefinitionId = definition.Id,
            Pos = position,
            Origin = PressureOrigin.Wave,
        });
    }

    // ── Запись ────────────────────────────────────────────────────────────────────

    private void Record(WaveDefinition wave, float terror, float budget, float spent,
        WaveShape shape, float center, float chill, int extraRows, bool byPortal)
    {
        string composition = _composer.Describe();
        float degrees = Mathf.RadToDeg(center);

        _history.Add(new WaveRecord
        {
            GameTime = _gameTime,
            WaveId = wave.Id,
            Terror = terror,
            Budget = budget,
            Spent = spent,
            Composition = composition,
            CenterAngleDegrees = degrees,
            Groups = shape.Groups,
            ChillSeconds = chill,
            ExtraRows = extraRows,
        });

        if (_history.Count > HistoryLimit)
            _history.RemoveAt(0);

        // Одна строка на волну. Печать здесь уместна не ради отладки: волна — редкое
        // и крупное событие партии, и по журналу разбора должно быть видно, что именно
        // пришло, не открывая панель
        string cause = byPortal ? " (вызвана порталом)" : string.Empty;

        GD.Print($"[Волна] {wave.Id}{cause}: террор {terror:0.0}, бюджет {budget:0.0}, " +
                 $"потрачено {spent:0.0} — {composition}. Отдых {chill:0} с");

        GM.Events.Append(new WaveStarted
        {
            WaveId = wave.Id,
            Terror = terror,
            Budget = budget,
            Spent = spent,
            Composition = composition,
            CenterAngleDegrees = degrees,
            Groups = shape.Groups,
            ChillSeconds = chill,
        });
    }
}
