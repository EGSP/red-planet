using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Одно посадочное место инструмента на носителе: справочник, часть изображения,
/// собственная перезарядка и текущий угол поворота.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ОБЪЕКТ. Справочник инструмента неизменяем и общий на всех носителей,
/// поэтому ни угол доворота, ни остаток перезарядки в нём жить не могут. Раньше состояние
/// ствола лежало прямо на носителе одним полем, и второй ствол выразить было нечем:
/// у двух орудий одного юнита перезарядка идёт врозь, доворачиваются они врозь, и части
/// изображения у них разные.
/// </summary>
public sealed class ToolMount
{
    /// <summary>Что за инструмент. Ссылка в справочник, никогда не null.</summary>
    public ToolDefinition Tool { get; init; }

    /// <summary>Номер в списке <c>tools</c> носителя. Разрешает спор равных приоритетов.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Часть изображения, изображающая инструмент. Null означает, что модели у носителя нет
    /// либо в ней не нашлось части с таким идентификатором: тогда инструмент существует
    /// только в расчёте, а выстрел показывается из центра носителя.
    /// </summary>
    public ModelTool Part { get; set; }

    /// <summary>Перезарядка. Есть у любого места, но читается только у ствола.</summary>
    public WeaponState Gun { get; } = new();

    /// <summary>
    /// Угол поворота ОТНОСИТЕЛЬНО ОСИ КОРПУСА, в радианах. Ограничение сектора задано
    /// в этой же системе отсчёта, и потому башня, упёршаяся в край сектора, едет вместе
    /// с корпусом сама собой, без отдельного правила.
    /// </summary>
    public float Local { get; internal set; }

    /// <summary>Сколько секунд инструмент не наводили. Отсчёт до возврата к оси корпуса.</summary>
    public float IdleFor { get; internal set; }

    /// <summary>Инструмент наводили в этом шаге. Снимается в <see cref="AimRig.Advance"/>.</summary>
    public bool Aimed { get; internal set; }

    /// <summary>
    /// Цель лежит за пределами сектора: доворотом одного инструмента её не достать.
    /// Осмысленно, только пока <see cref="Aimed"/> истинно.
    /// </summary>
    public bool Blocked { get; internal set; }

    public WeaponDefinition Weapon => Tool as WeaponDefinition;

    public WorkToolDefinition Work => Tool as WorkToolDefinition;

    /// <summary>Мировая ось инструмента при заданной оси корпуса.</summary>
    public float World(float bodyFacing) => bodyFacing + Local;

    /// <summary>
    /// Откуда показывать выстрел или луч работы. Без части изображения — из центра
    /// носителя: расчёт от этого не меняется, см. <see cref="IArmed.MuzzlePosition"/>.
    /// </summary>
    public Vector2 Muzzle(Vector2 fallback) =>
        Part != null && Alive.Is(Part) ? Part.MuzzleGlobal : fallback;
}

/// <summary>
/// Наведение всех инструментов одного носителя: доворот каждого в своём секторе, возврат
/// в походное положение и решение о том, разворачивать ли ради них корпус.
///
/// ТРИ ВЕЛИЧИНЫ, КОТОРЫЕ ЛЕГКО СПУТАТЬ. Ось корпуса — куда носитель повёрнут целиком.
/// Сектор наведения (<see cref="ToolDefinition.AimArcDegrees"/>) — насколько инструмент
/// отклоняется от этой оси. Сектор стрельбы (<see cref="WeaponDefinition.FireArcDegrees"/>)
/// — насколько точно ствол уже наведён, чтобы выстрел был разрешён. Первые две складываются,
/// третья проверяется от результата сложения.
///
/// ПОРЯДОК ЗА ШАГ. Системы в течение шага зовут <see cref="Aim"/> для тех мест, у которых
/// есть на что наводиться; носитель в конце шага зовёт <see cref="Advance"/>, и та
/// возвращает ненаведённые инструменты к оси корпуса, а также сообщает, какой угол корпуса
/// нужен инструментам. Разворачивать корпус или нет, решает сам носитель: у турели
/// основание неподвижно, а подвижная сущность на ходу принадлежит системе движения.
/// </summary>
public sealed class AimRig
{
    private static readonly ToolMount[] Empty = Array.Empty<ToolMount>();

    private ToolMount[] _mounts = Empty;

    /// <summary>Требования к развороту корпуса, накопленные за шаг. См. <see cref="Aim"/>.</summary>
    private readonly List<Demand> _demands = new();

    /// <summary>
    /// Одно требование: инструмент просит, чтобы ось корпуса оказалась не дальше
    /// <see cref="HalfWidth"/> радиан от <see cref="Center"/>.
    /// </summary>
    private readonly struct Demand
    {
        public Demand(float center, float halfWidth, int priority, int order)
        {
            Center = center;
            HalfWidth = halfWidth;
            Priority = priority;
            Order = order;
        }

        public float Center { get; }

        public float HalfWidth { get; }

        public int Priority { get; }

        public int Order { get; }
    }

    public IReadOnlyList<ToolMount> Mounts => _mounts;

    public int Count => _mounts.Length;

    /// <summary>
    /// Место главного ствола — первого по порядку. Null у безоружного. По нему берутся
    /// ось прицеливания и точка вылета там, где ответ должен быть один: полоса прочности,
    /// подсказки, отладочная отрисовка.
    /// </summary>
    public ToolMount PrimaryWeapon { get; private set; }

    /// <summary>Место рабочей руки — первой по порядку. Null у того, кто не строит.</summary>
    public ToolMount PrimaryWork { get; private set; }

    /// <summary>
    /// Собрать посадочные места по справочнику и связать их с частями изображения.
    /// Зовётся один раз при рождении носителя: состав инструментов по ходу партии
    /// не меняется.
    /// </summary>
    public void Bind(UnitDefinition definition, UnitModel model)
    {
        var tools = definition?.Tools ?? Array.Empty<ToolDefinition>();

        _mounts = Empty;
        PrimaryWeapon = null;
        PrimaryWork = null;

        if (tools.Length == 0)
            return;

        _mounts = new ToolMount[tools.Length];

        for (int i = 0; i < tools.Length; i++)
        {
            var mount = new ToolMount { Tool = tools[i], Order = i };
            _mounts[i] = mount;

            if (mount.Weapon != null)
                PrimaryWeapon ??= mount;

            if (mount.Work is { CanWork: true })
                PrimaryWork ??= mount;
        }

        var parts = model?.Bind(tools);

        if (parts == null)
            return;

        for (int i = 0; i < _mounts.Length; i++)
            _mounts[i].Part = parts[i];
    }

    /// <summary>
    /// Навести все стволы на одну точку. Отдельным действием от <see cref="Aim"/>, потому
    /// что система стрельбы работает с носителем целиком: цель у него одна, а стволов
    /// может быть несколько, и каждый доворачивается сам.
    /// </summary>
    public void AimWeapons(float bodyFacing, Vector2 from, Vector2 to, double dt)
    {
        foreach (var mount in _mounts)
            if (mount.Weapon != null)
                Aim(mount, bodyFacing, from, to, dt);
    }

    /// <summary>Привести все инструменты к оси корпуса без доворота. Нужно при рождении.</summary>
    public void Snap()
    {
        foreach (var mount in _mounts)
        {
            mount.Local = 0f;
            mount.IdleFor = 0f;
            mount.Aimed = false;
            mount.Blocked = false;
        }

        _demands.Clear();
    }

    /// <summary>Место по справочнику инструмента. Null, если такого места нет.</summary>
    public ToolMount Of(ToolDefinition tool)
    {
        if (tool == null)
            return null;

        foreach (var mount in _mounts)
            if (mount.Tool == tool)
                return mount;

        return null;
    }

    /// <summary>Остудить все стволы. Зовёт система стрельбы раз в шаг.</summary>
    public void TickGuns(double dt)
    {
        foreach (var mount in _mounts)
            mount.Gun.Tick(dt);
    }

    /// <summary>
    /// Довернуть один инструмент к точке за этот шаг и заявить, чего это требует от корпуса.
    ///
    /// Желаемый угол считается в системе отсчёта корпуса и обрезается сектором, поэтому
    /// упёршийся в край сектора инструмент дальше не идёт, но и не срывается: он остаётся
    /// на границе, пока корпус не довернётся.
    /// </summary>
    public void Aim(ToolMount mount, float bodyFacing, Vector2 from, Vector2 to, double dt)
    {
        if (mount == null || from.IsEqualApprox(to))
            return;

        var tool = mount.Tool;
        float wanted = Heading.Delta(bodyFacing, Heading.AngleTo(from, to));
        float arc = tool.AimArc;
        float allowed = Mathf.Clamp(wanted, -arc, arc);

        mount.Local = Turn(tool, mount.Local, allowed, tool.AimRate * (float)dt);

        mount.Aimed = true;
        mount.IdleFor = 0f;
        mount.Blocked = Mathf.Abs(wanted) > arc;

        Claim(mount, bodyFacing + wanted);
    }

    /// <summary>
    /// Довернуть инструмент на один шаг ВНУТРИ его сектора.
    ///
    /// ЧЕРЕЗ ЗАПРЕЩЁННУЮ ЗОНУ ХОДА НЕТ, И ЭТО ГЛАВНОЕ ЗДЕСЬ. Обыкновенный доворот
    /// (<see cref="Heading.TurnToward"/>) идёт кратчайшей дугой по окружности, а кратчайшая
    /// дуга между двумя краями сектора пролегает СНАРУЖИ него: башне с сектором ±125°,
    /// стоящей на одном краю, до другого края ближе через корму — сто десять градусов
    /// против двухсот пятидесяти. Из этого следовало, что при появлении цели с другой
    /// стороны башня проворачивалась через ту самую зону, которую сектор и запрещает.
    ///
    /// Внутри ограниченного сектора углы образуют ОТРЕЗОК, а не окружность: и нынешний
    /// угол, и желаемый лежат в пределах ±<c>arc</c>, где <c>arc</c> меньше половины оборота.
    /// Поэтому здесь идёт обыкновенное сближение чисел, проходящее через ноль, — то есть
    /// башня разворачивается «через нос», как ей и положено.
    ///
    /// Круговому сектору запрещать нечего, и для него сохраняется кратчайший путь: иначе
    /// башня с полным оборотом отказывалась бы переходить через направление назад
    /// и всякий раз шла бы длинной дорогой.
    /// </summary>
    private static float Turn(ToolDefinition tool, float current, float allowed, float step)
    {
        if (tool.Fixed)
            return 0f;

        return tool.FullCircle
            ? Heading.TurnToward(current, allowed, step)
            : Mathf.MoveToward(current, allowed, step);
    }

    /// <summary>
    /// Записать, какой угол корпуса нужен этому инструменту. Круговой сектор не требует
    /// ничего никогда, поэтому и требования не оставляет: именно отсюда берётся правило
    /// «пока башня достаёт сама, корпус стоит».
    /// </summary>
    private void Claim(ToolMount mount, float target)
    {
        var tool = mount.Tool;

        switch (tool.BodyAssist)
        {
            case BodyAssist.Never:
                return;

            // Жёстко закреплённому инструменту доворот нужен всегда и точный: своего запаса
            // у него нет вовсе, поэтому ширина требования нулевая
            case BodyAssist.Always:
                _demands.Add(new Demand(target, 0f, tool.AimPriority, mount.Order));
                return;

            case BodyAssist.WhenBlocked:
                if (!mount.Blocked || tool.FullCircle)
                    return;

                _demands.Add(new Demand(target, tool.AimArc, tool.AimPriority, mount.Order));
                return;
        }
    }

    /// <summary>
    /// Закрыть шаг: вернуть ненаведённые инструменты к оси корпуса и сообщить, какой угол
    /// корпуса удовлетворил бы инструменты. NaN означает «корпус трогать незачем» — либо
    /// требований нет, либо нынешний угол их уже удовлетворяет.
    ///
    /// Накопленные требования после вызова снимаются: они принадлежат одному шагу,
    /// как и само наведение.
    /// </summary>
    public float Advance(float bodyFacing, double dt)
    {
        Relax(dt);

        float request = Resolve(bodyFacing);

        foreach (var mount in _mounts)
        {
            mount.Aimed = false;
            mount.Blocked = false;
        }

        _demands.Clear();
        return request;
    }

    /// <summary>
    /// Возврат в походное положение. Задержка нужна против дрожания: без неё смена цели
    /// у скорострельного оружия гоняла бы ствол до оси корпуса и обратно между выстрелами.
    /// </summary>
    private void Relax(double dt)
    {
        foreach (var mount in _mounts)
        {
            if (mount.Aimed)
                continue;

            var tool = mount.Tool;

            if (tool.Fixed)
            {
                mount.Local = 0f;
                continue;
            }

            mount.IdleFor += (float)dt;

            if (mount.IdleFor < tool.AimIdleDelay)
                continue;

            // Возврат идёт по тем же правилам, что и наведение: у ограниченного сектора
            // ноль лежит в его середине, и сближение чисел ведёт к нему кратчайшим путём
            // ВНУТРИ сектора — см. Turn
            mount.Local = Turn(tool, mount.Local, 0f, tool.AimRate * (float)dt);
        }
    }

    /// <summary>
    /// Какой угол корпуса удовлетворяет требования инструментов.
    ///
    /// ГЕОМЕТРИЯ, А НЕ ОЧЕРЁДНОСТЬ. Каждое требование задаёт отрезок допустимых углов
    /// корпуса; общее решение есть пересечение этих отрезков. Существует оно — корпус
    /// доворачивается к ближайшей его точке, и тогда цель попадает в секторы ВСЕХ
    /// инструментов разом. Пусто — угодить всем нельзя (например, у машины с орудиями
    /// по разным бортам), и тогда вступает приоритет.
    ///
    /// ОТРЕЗКИ СЧИТАЮТСЯ ОТ НЫНЕШНЕГО УГЛА КОРПУСА. Углы лежат на окружности, и пересекать
    /// их напрямую пришлось бы с разбором переходов через полный оборот. Разница
    /// <see cref="Heading.Delta"/> переводит каждый центр в отрезок [-pi, pi] вокруг нуля,
    /// после чего пересечение считается как у обыкновенных числовых отрезков, а ноль
    /// означает «корпус уже стоит как надо».
    /// </summary>
    private float Resolve(float bodyFacing)
    {
        if (_demands.Count == 0)
            return float.NaN;

        float low = float.NegativeInfinity;
        float high = float.PositiveInfinity;

        foreach (var demand in _demands)
        {
            float delta = Heading.Delta(bodyFacing, demand.Center);

            low = Mathf.Max(low, delta - demand.HalfWidth);
            high = Mathf.Min(high, delta + demand.HalfWidth);
        }

        float turn = low <= high
            ? Mathf.Clamp(0f, low, high)
            : Fallback(bodyFacing);

        // Доворот меньше десятой доли градуса корпус не разворачивает: иначе требование
        // «стой ровно на цели» у жёсткого орудия заставляло бы корпус дрожать вокруг неё
        return Mathf.Abs(turn) < 0.0017f ? float.NaN : bodyFacing + turn;
    }

    /// <summary>
    /// Спор неразрешим: удовлетворяем самое важное требование, а прочими пренебрегаем.
    /// При равном приоритете побеждает тот инструмент, который раньше стоит в списке
    /// <c>tools</c> носителя, — порядок объявления и есть неявный приоритет.
    /// </summary>
    private float Fallback(float bodyFacing)
    {
        var best = _demands[0];

        foreach (var demand in _demands)
            if (demand.Priority > best.Priority
                || (demand.Priority == best.Priority && demand.Order < best.Order))
            {
                best = demand;
            }

        float delta = Heading.Delta(bodyFacing, best.Center);

        // Доворачиваем не до центра, а до ближайшего края допустимого отрезка: дальше
        // ехать незачем, цель уже в секторе
        return delta - Mathf.Sign(delta) * Mathf.Min(best.HalfWidth, Mathf.Abs(delta));
    }

    /// <summary>
    /// Разослать углы частям изображения. Каждая часть получает СВОЙ угол относительно
    /// корпуса: раньше поворот назначался всем частям сразу, и второй ствол выразить
    /// было нечем.
    /// </summary>
    public void Apply()
    {
        foreach (var mount in _mounts)
            if (mount.Part != null && Alive.Is(mount.Part) && mount.Part.FollowsAim)
                mount.Part.Rotation = mount.Local;
    }
}
