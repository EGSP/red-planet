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
/// и приходит целиком разом. Её юниты помечаются признаком <see cref="PressureOrigin.Wave"/>
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
/// ОБЩЕЙ ЦЕЛИ У ВОЛНЫ НЕТ. Выйдя на карту, её юниты живут по той же логике, что и фоновые:
/// цель каждому раздаёт <see cref="EnemyAiSystem"/> поштучно. Поэтому форма появления
/// определяет только первые секунды — какая часть периметра встречает удар, — а дальше
/// строй расходится. Это осознанное ограничение первой версии, а не упущение.
/// </summary>
public partial class WaveSystem : GameSystem
{
    /// <summary>Константа отдыха, разброс и умолчания формы появления.</summary>
    [Export] public WaveSettings Settings;

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

    // Рабочие списки живут полем, а не переменной шага: набор состава идёт раз в полторы
    // минуты, но выделять под него память заново незачем
    private readonly List<WaveDefinition> _candidates = new();
    private readonly List<float> _weights = new();
    private readonly List<UnitDefinition> _allowed = new();
    private readonly List<UnitDefinition> _pool = new();
    private readonly List<UnitDefinition> _composition = new();

    private string[] _preferred = System.Array.Empty<string>();
    private float _timer;
    private float _gameTime;

    /// <summary>Сколько осталось до ближайшей волны, секунд. Показывается в отладочной панели.</summary>
    public float TimeLeft => Mathf.Max(_timer, 0f);

    /// <summary>Прошедшие волны, от старых к новым.</summary>
    public IReadOnlyList<WaveRecord> History => _history;

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

        _timer = Settings.FirstDelay;
    }

    public override void Step(double dt)
    {
        if (GM.Playground == null)
            return;

        _gameTime += (float)dt;
        _timer -= (float)dt;

        if (_timer > 0f)
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

        Launch(wave, terror);
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

    private void Launch(WaveDefinition wave, float terror)
    {
        float budget = wave.Budget(terror);

        Compose(wave, budget);

        var shape = Settings.ShapeOf(wave.Shape);
        float center = _rng.RandfRange(0f, Mathf.Tau);
        int extraRows = Deploy(shape, center);

        float spent = 0f;

        foreach (var definition in _composition)
            spent += definition.ArmyPower;

        float chill = Settings.ChillAfter(wave, _rng);

        _timer = chill;
        _preferred = wave.PreferNext;

        Record(wave, terror, budget, spent, shape, center, chill, extraRows);
    }

    /// <summary>
    /// Набрать состав в пределах бюджета.
    ///
    /// ДВА ПРОХОДА, И ОНИ РАЗНЫЕ. Сначала выполняются целевые доли — те части бюджета,
    /// которые волна отвела конкретным видам; затем остаток тратится свободно среди всего
    /// допустимого. Порядок списков в файле при этом значим: он и есть порядок исполнения
    /// долей. Неявная сортировка, скажем по величине доли, поставила бы результат в
    /// зависимость от чисел, которые правят ради баланса, а не ради очерёдности.
    /// </summary>
    private void Compose(WaveDefinition wave, float budget)
    {
        _composition.Clear();
        Allowed(wave);

        if (_allowed.Count == 0)
        {
            GD.PushWarning($"[WaveSystem] волна «{wave.Id}»: допустимых видов не осталось");
            return;
        }

        float spent = 0f;

        foreach (var list in wave.UnitLists)
        {
            if (list.TargetBudgetShare <= 0f)
                continue;

            Pool(list);
            spent = Quota(_pool, budget * list.TargetBudgetShare, budget, spent);
        }

        Quota(_allowed, budget, budget, spent);
    }

    /// <summary>
    /// Потратить на виды из pool отведённую им квоту.
    ///
    /// Последний вид берётся и тогда, когда квота им перебирается: условие проверяется
    /// ДО добавления, а не после. Иначе «потратить на этих треть бюджета» почти никогда
    /// не выполнялось бы — доля кратна цене вида лишь по случайности, и набор
    /// останавливался бы, не дойдя до неё.
    ///
    /// Ограничение при этом одно и общее: сколько бы ни осталось в квоте, за пределы
    /// бюджета волны набор не выходит.
    /// </summary>
    private float Quota(List<UnitDefinition> pool, float quota, float budget, float spent)
    {
        if (pool.Count == 0)
            return spent;

        float used = 0f;

        while (used < quota)
        {
            var definition = pool[_rng.RandiRange(0, pool.Count - 1)];
            float power = definition.ArmyPower;

            if (spent + power > budget)
            {
                // В остаток бюджета не помещается именно этот вид, но может поместиться
                // другой из того же списка — перебираем дешёвые, прежде чем сдаться
                if (!Cheapest(pool, budget - spent, out definition))
                    break;

                power = definition.ArmyPower;
            }

            _composition.Add(definition);
            spent += power;
            used += power;
        }

        return spent;
    }

    /// <summary>Самый дешёвый вид, помещающийся в остаток. Ложь — не помещается ни один.</summary>
    private static bool Cheapest(List<UnitDefinition> pool, float available,
        out UnitDefinition found)
    {
        found = null;

        foreach (var definition in pool)
            if (definition.ArmyPower <= available &&
                (found == null || definition.ArmyPower < found.ArmyPower))
                found = definition;

        return found != null;
    }

    /// <summary>
    /// Допустимые виды. Списки allow сужают: есть хоть один — допустимо только их
    /// объединение; нет ни одного — допустимы все виды противника. Списки deny
    /// вычитаются после. Списки limit на отбор не влияют вовсе, у них только доля.
    /// </summary>
    private void Allowed(WaveDefinition wave)
    {
        _allowed.Clear();
        bool narrowed = false;

        foreach (var list in wave.UnitLists)
        {
            if (list.Mode != UnitListMode.Allow)
                continue;

            narrowed = true;

            foreach (var definition in list.Units)
                if (!_allowed.Contains(definition))
                    _allowed.Add(definition);
        }

        if (!narrowed)
            foreach (var definition in GM.Catalog.Enemies)
                if (definition.ArmyPower > 0f)
                    _allowed.Add(definition);

        foreach (var list in wave.UnitLists)
        {
            if (list.Mode != UnitListMode.Deny)
                continue;

            foreach (var definition in list.Units)
                _allowed.Remove(definition);
        }
    }

    /// <summary>Виды списка, оставшиеся допустимыми после сужения и вычитания.</summary>
    private void Pool(WaveUnitList list)
    {
        _pool.Clear();

        foreach (var definition in list.Units)
            if (_allowed.Contains(definition))
                _pool.Add(definition);
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
    /// </summary>
    private int Deploy(WaveShape shape, float center)
    {
        if (_composition.Count == 0)
            return 0;

        // Крупные идут в передние ряды: иначе живучие плетутся позади и вступают в бой
        // последними, хотя вся их роль — принять первый удар
        _composition.Sort((a, b) => b.ArmyPower.CompareTo(a.ArmyPower));

        int groups = Mathf.Min(shape.Groups, _composition.Count);
        int extra = 0;

        for (int g = 0; g < groups; g++)
        {
            float angle = center + g * Mathf.DegToRad(shape.GroupsArcDegrees);
            int index = 0;

            for (int i = g; i < _composition.Count; i += groups)
            {
                var (position, row) = Slot(shape, angle, index++);
                Spawn(_composition[i], position);
                extra = Mathf.Max(extra, row);
            }
        }

        int planned = Rows(shape);

        return Mathf.Max(extra + 1 - planned, 0);
    }

    /// <summary>Сколько рядов помещается в заданную глубину. Не меньше одного.</summary>
    private static int Rows(WaveShape shape) =>
        Mathf.Max(Mathf.FloorToInt(shape.DepthPx / shape.SpacingPx) + 1, 1);

    /// <summary>
    /// Место с порядковым номером внутри очага и номер ряда, в который оно попало.
    ///
    /// Ряды идут от ближнего к базе наружу с шагом в промежуток, мест в ряду столько,
    /// сколько их помещается по длине дуги на этой глубине. Когда состав не умещается
    /// в заданную глубину, ряды продолжаются за дальнюю дугу тем же шагом: заявленная
    /// плотность важнее заявленной глубины, поскольку слишком тесная расстановка приводит
    /// к расталкиванию в первые же секунды, тогда как лишний ряд позади просто отстаёт.
    /// Ширина у таких рядов остаётся как у дальней дуги — экстраполировать угол нельзя,
    /// иначе форма расходится тем сильнее, чем больше волна.
    /// </summary>
    private static (Vector2 Position, int Row) Slot(WaveShape shape, float center, int index)
    {
        int rows = Rows(shape);
        float step = shape.SpacingPx;
        int row = 0;

        while (true)
        {
            float t = rows > 1 ? Mathf.Min(row / (float)(rows - 1), 1f) : Mathf.Min(row, 1f);
            float radius = shape.NearRadiusPx + row * step;
            int places = Mathf.Max(Mathf.FloorToInt(shape.ArcAt(t) * radius / step) + 1, 1);

            if (index < places)
            {
                float offset = (index - (places - 1) * 0.5f) * step;
                float angle = center + offset / radius;

                return (Heading.Forward(angle) * radius, row);
            }

            index -= places;
            row++;
        }
    }

    private void Spawn(UnitDefinition definition, Vector2 position)
    {
        var enemy = GM.Spawn.SpawnUnit(definition, position, Faction.Hostile);
        enemy.Origin = PressureOrigin.Wave;

        // Появился — уже смотрит на базу: иначе первый ход выглядит как разворот на месте
        enemy.Rotation = Heading.AngleTo(position, Vector2.Zero);

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
        WaveShape shape, float center, float chill, int extraRows)
    {
        string composition = Describe();
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
        GD.Print($"[Волна] {wave.Id}: террор {terror:0.0}, бюджет {budget:0.0}, " +
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

    /// <summary>Состав перечислением видов с количествами: «стрелок ×6, толстяк ×2».</summary>
    private string Describe()
    {
        if (_composition.Count == 0)
            return "пусто";

        var counts = new Dictionary<string, int>();
        var order = new List<string>();

        foreach (var definition in _composition)
        {
            string name = definition.DisplayName.Length > 0 ? definition.DisplayName : definition.Id;

            if (!counts.TryAdd(name, 1))
                counts[name]++;
            else
                order.Add(name);
        }

        var parts = new List<string>(order.Count);

        foreach (string name in order)
            parts.Add($"{name} ×{counts[name]}");

        return string.Join(", ", parts);
    }
}
