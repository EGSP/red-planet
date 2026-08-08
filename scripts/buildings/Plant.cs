using System.Collections.Generic;
using Godot;

/// <summary>
/// Завод юнитов: очередь производства без каркаса на карте и выпуск с выездом из корпуса.
///
/// Очередь производства — не <see cref="OrderQueue"/>. Приказы заводу (rally) живут в
/// обычной очереди и копируются на каждого выпущенного; состав производства — отдельный
/// список id, который игрок набирает кнопками plant-панели.
///
/// Одновременно из корпуса выезжает не больше одного юнита: следующий слот и пауза
/// factory_cooldown начинаются только после снятия Leaving у текущего.
/// </summary>
public partial class Plant : Building, IProducer
{
    private readonly List<string> _queue = new();

    /// <summary>Указатель текущего слота при циклическом (infinite) производстве.</summary>
    private int _cursor;

    private float _progress;
    private float _coolLeft;
    private Unit _exiting;
    private bool _infinite = true;

    /// <summary>Бесконечное производство: очередь не очищается, а прокручивается.</summary>
    public bool Infinite
    {
        get => _infinite;
        set
        {
            if (_infinite == value)
                return;

            _infinite = value;
            Revision++;
        }
    }

    /// <summary>
    /// Сколько раз менялся состав очереди или режим. По нему панель обновляет счётчики
    /// на кнопках: перебирать очередь каждый кадр незачем, а сравнить одно число дёшево.
    /// Тот же приём, что у <see cref="ObstacleMap.Revision"/>.
    /// </summary>
    public int Revision { get; private set; }

    public IReadOnlyList<string> Queue => _queue;

    /// <summary>Готовый завод производит всегда — на то он и построен.</summary>
    public bool CanProduce => true;

    public float ProgressRatio
    {
        get
        {
            var def = CurrentDefinition;
            if (def == null || def.TotalWork <= 0f)
                return 0f;

            return _progress / def.TotalWork;
        }
    }

    private PlantDefinition Spec => Definition?.Plant;

    private float BuildPower => Spec?.BuildPower ?? 0f;

    private float EnergyDrain => BuildPower * (Spec?.EnergyPerPower ?? 0f);

    private UnitDefinition CurrentDefinition
    {
        get
        {
            if (_queue.Count == 0 || GameManager.I?.Catalog == null)
                return null;

            int index = Mathf.Clamp(_cursor, 0, _queue.Count - 1);
            return GameManager.I.Catalog.Unit(_queue[index]);
        }
    }

    public int CountOf(string unitId)
    {
        int count = 0;

        foreach (string id in _queue)
            if (id == unitId)
                count++;

        return count;
    }

    /// <summary>Добавить <paramref name="count"/> экземпляров в хвост очереди.</summary>
    public void Enqueue(string unitId, int count = 1)
    {
        if (string.IsNullOrEmpty(unitId) || count <= 0)
            return;

        if (GameManager.I?.Catalog.Unit(unitId) == null)
            return;

        for (int i = 0; i < count; i++)
            _queue.Add(unitId);

        Revision++;
    }

    /// <summary>
    /// Снять с конца очереди до <paramref name="count"/> последних вхождений данного типа.
    /// </summary>
    public void Dequeue(string unitId, int count = 1)
    {
        if (string.IsNullOrEmpty(unitId) || count <= 0)
            return;

        for (int n = 0; n < count; n++)
        {
            int at = -1;

            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i] != unitId)
                    continue;

                at = i;
                break;
            }

            // Вхождения кончились раньше запрошенного числа — выходим из цикла, а не из
            // метода: снятое до этого мига уже изменило очередь, и ревизию надо поднять
            if (at < 0)
                break;

            _queue.RemoveAt(at);

            // Прогресс принадлежит слоту, а не номеру в списке. Снят слот перед указателем —
            // указатель едет за своим слотом, и накопленное остаётся при нём. Снят сам
            // текущий слот — накопленное пропадает вместе с ним, иначе оно досталось бы
            // соседу, сдвинувшемуся на освободившийся номер, и юнит другого типа
            // выпустился бы мгновенно
            if (_cursor > at)
            {
                _cursor--;
            }
            else if (_cursor == at)
            {
                _progress = 0f;

                if (_cursor >= _queue.Count)
                    _cursor = 0;
            }
        }

        if (_queue.Count == 0)
        {
            _cursor = 0;
            _progress = 0f;
        }

        Revision++;
    }

    public override void Declare(EconomyLedger ledger)
    {
        base.Declare(ledger);

        if (_exiting != null || _coolLeft > 0f || BuildPower <= 0f)
            return;

        if (CurrentDefinition == null)
            return;

        ledger.Request(ResourceKind.Metal, BuildPower);
        ledger.Request(ResourceKind.Energy, EnergyDrain);
    }

    public override void Run(double dt, EconomyRates rates)
    {
        base.Run(dt, rates);

        WatchExit();

        if (_coolLeft > 0f)
        {
            _coolLeft = Mathf.Max(0f, _coolLeft - (float)dt);
            return;
        }

        if (_exiting != null || BuildPower <= 0f)
            return;

        var def = CurrentDefinition;
        if (def == null)
            return;

        var events = GameManager.I.Events;

        float energy = EnergyDrain * (float)dt * rates.Energy;
        if (energy > 0f)
            events.Append(new ResourceSpent { Kind = ResourceKind.Energy, Amount = energy });

        float done = Mathf.Min(BuildPower * (float)dt * rates.Work, def.TotalWork - _progress);
        if (done <= 0f)
            return;

        events.Append(new ResourceSpent { Kind = ResourceKind.Metal, Amount = done });
        _progress += done;

        if (_progress >= def.TotalWork - 0.001f)
            Release(def);
    }

    private void WatchExit()
    {
        if (_exiting == null)
            return;

        if (Alive.Is(_exiting) && _exiting.Movement.Leaving)
            return;

        _exiting = null;
        _coolLeft = Spec?.FactoryCooldown ?? 3f;
        AdvanceQueue();
        _progress = 0f;
    }

    private void Release(UnitDefinition def)
    {
        var gm = GameManager.I;
        if (gm == null)
            return;

        var unit = gm.Spawn.SpawnUnit(def, GlobalPosition, Faction.Player);

        // Спавн может не состояться (нет сцены мира, снят справочник). Прогресс при этом
        // не сбрасываем: слот остаётся собранным и выпустится, как только спавн заработает
        if (unit == null)
            return;

        var exit = ExitPoint(def);

        // Курс новорождённого — на свой выезд, а не по корпусу завода. Выезд выбирается
        // среди нескольких направлений rolloff, и повёрнутый по корпусу юнит выезжал бы
        // боком, доворачиваясь на ходу со своей скоростью вращения: у неповоротливой
        // машины разворот виден целиком и читается как застрявший выпуск.
        //
        // Направления выезда, совпадающего с корпусом, это не меняет: там угол тот же самый
        var offset = exit - GlobalPosition;
        unit.Rotation = offset.LengthSquared() > 0.0001f
            ? Heading.AngleTo(GlobalPosition, exit)
            : BodyFacing;
        unit.SnapToolToBody();

        CopyRally(unit, exit);
        unit.Movement.BeginExit(this, exit);
        _exiting = unit;
        _progress = 0f;

        // Поверх корпуса завода юнит рисуется сам собой: слой Actors объявлен ниже слоя
        // Structures, а значит выше на экране. Ручной ZIndex здесь не нужен — см. WorldLayer
    }

    private void AdvanceQueue()
    {
        if (_queue.Count == 0)
        {
            _cursor = 0;
            return;
        }

        Revision++;

        if (!Infinite)
        {
            if (_cursor >= 0 && _cursor < _queue.Count)
                _queue.RemoveAt(_cursor);

            if (_cursor >= _queue.Count)
                _cursor = 0;

            return;
        }

        _cursor = (_cursor + 1) % _queue.Count;
    }

    /// <summary>
    /// Раздать новорождённому точку сбора.
    ///
    /// РАЗДАЮТСЯ КОПИИ, А НЕ САМИ ПРИКАЗЫ. Приказ движения дожидается всех своих участников
    /// (см. <see cref="Order"/>), и стоит выехавшему из корпуса войти в состав общего
    /// приказа, как весь отряд встал бы у точки сбора, дожидаясь следующего юнита, которого
    /// завод ещё только собирает. Копия несёт то же намерение, но состав у неё свой.
    ///
    /// Без приказов завод ставит якорь у выезда: иначе <see cref="PlayerAiSystem"/>
    /// послал бы юнита за коммандером, а выпуск без точки сбора должен оставаться у завода.
    /// </summary>
    private void CopyRally(Unit unit, Vector2 exit)
    {
        if (Orders.Count == 0)
        {
            unit.SetAnchor(exit);
            return;
        }

        var copy = new List<Order>(Orders.Count);

        foreach (var order in Orders.Remaining)
        {
            var cloned = CloneOrder(order);
            if (cloned != null)
                copy.Add(cloned);
        }

        if (copy.Count == 0)
        {
            unit.SetAnchor(exit);
            return;
        }

        if (unit.Orders.TrySet(copy.ToArray()))
            unit.SetAnchor(copy[^1].Point);
    }

    private static Order CloneOrder(Order source)
    {
        if (source == null)
            return null;

        return source.Kind switch
        {
            OrderKind.Move => Order.MoveTo(source.Pos),
            OrderKind.Attack when Alive.Is(source.Entity) => Order.Attack(source.Entity),
            OrderKind.Follow when Alive.Is(source.Entity) => Order.Follow(source.Entity),
            OrderKind.Repair when Alive.Is(source.Entity) => Order.Repair(source.Entity),
            OrderKind.Build when Alive.Is(source.Target as Node2D) =>
                Order.Work(OrderKind.Build, source.Target),
            // Снос завода новорождённому не копируем: он относится к самой постройке
            OrderKind.Delete => null,
            _ => Order.MoveTo(source.Point),
        };
    }

    /// <summary>
    /// Внешняя точка выезда. Среди открытых направлений выбирается то, откуда путь
    /// до текущей цели точки сбора короче: иначе юнит выезжает с противоположной
    /// стороны и обходит завод. Без приказов — первое открытое, как раньше.
    /// Если все закрыты — берётся первое (таймаут Leaving телепортирует на NearestPassable).
    /// </summary>
    private Vector2 ExitPoint(UnitDefinition unitDef)
    {
        float radius = unitDef.RadiusPx;
        float extra = radius + Const.Unit * 0.25f;
        return PickExit(radius, extra, pathCost: true);
    }

    /// <summary>
    /// Цель точки сбора для выбора выезда — точка первого приказа, который копируется
    /// новорождённому. Delete к заводу самому относится и в оценку не входит.
    /// </summary>
    private Vector2? RallyGoal()
    {
        foreach (var order in Orders.Remaining)
        {
            if (order.Kind == OrderKind.Delete)
                continue;

            return order.Point;
        }

        return null;
    }

    /// <summary>
    /// Выбрать выезд среди маркеров. <paramref name="pathCost"/> включает полный поиск
    /// пути — только в момент выпуска; отрисовка стрелок обходится евклидовой оценкой,
    /// иначе каждый кадр запускал бы A* на каждый открытый выход.
    /// </summary>
    private Vector2 PickExit(float radius, float extra, bool pathCost)
    {
        Vector2? first = null;
        Vector2? firstUsable = null;
        Vector2? best = null;
        float bestScore = float.PositiveInfinity;
        var goal = RallyGoal();

        foreach (var point in ExitMarkers(extra))
        {
            first ??= point;

            if (!ExitUsable(point, radius))
                continue;

            firstUsable ??= point;

            // Без цели очереди приоритет — порядок rolloff_directions
            if (goal is not { } target)
                return point;

            float score = pathCost
                ? ExitPathScore(point, target, radius)
                : point.DistanceTo(target);

            if (score >= bestScore)
                continue;

            bestScore = score;
            best = point;
        }

        return best ?? firstUsable ?? first ?? GlobalPosition + Heading.Forward(BodyFacing) *
               (Footprint.Half.Length() + extra);
    }

    /// <summary>
    /// Стоимость пути от выезда до цели. Недостижимый выход получает бесконечность,
    /// чтобы уступить достижимому; если оба закрыты сеткой — сравнивать уже нечем,
    /// и вызывающий возьмёт первый открытый геометрически.
    /// </summary>
    private static float ExitPathScore(Vector2 exit, Vector2 goal, float radius)
    {
        var paths = GameManager.I?.System<PathfindingSystem>();
        if (paths == null)
            return exit.DistanceTo(goal);

        if (paths.TryMeasure(exit, goal, radius, out float length))
            return length;

        return float.PositiveInfinity;
    }

    /// <summary>
    /// Можно ли выпускать юнита в эту точку. Смотрим непрерывную геометрию чужих
    /// построек (свой завод в except): растр здесь не годится — клетка у двери
    /// часто непроходима из‑за clearance самого завода, и оба выезда «закрывались» бы зря.
    /// </summary>
    private bool ExitUsable(Vector2 point, float radius)
    {
        var gm = GameManager.I;
        if (gm?.Obstacles == null)
            return true;

        var (body, step) = ExitProbes(point, radius);

        if (gm.Obstacles.Overlaps(body, except: this))
            return false;

        return step is not { } ahead || !gm.Obstacles.Overlaps(ahead, except: this);
    }

    /// <summary>
    /// Области проверки выезда. Первая — тело юнита в самой точке выезда, она обязательна:
    /// это ровно то место, где сущность появится снаружи корпуса.
    ///
    /// Вторая — то же тело, продвинутое вперёд на rolloff_clearance. Она отвечает не за
    /// возможность выехать, а за выбор стороны: завод предпочтёт ту, откуда есть куда
    /// ехать дальше. При нулевом зазоре второй области нет вовсе, потому что покинувший
    /// корпус юнит волен повернуть вбок, и упор прямо по курсу выезду не мешает.
    ///
    /// Размер обеих равен телу юнита. Прежде область была не меньше половины клетки,
    /// а шаг равнялся целой клетке, отчего проверка доставала почти до трёх клеток
    /// от центра завода и закрывала выезд из-за соседа, стоящего через свободный проход.
    /// </summary>
    private (Obb Body, Obb? Step) ExitProbes(Vector2 point, float radius)
    {
        var size = new Vector2(radius * 2f, radius * 2f);
        var body = new Obb(point, size);

        float clearance = Spec?.RolloffClearancePx ?? 0f;

        if (clearance <= 0f)
            return (body, null);

        var offset = point - GlobalPosition;

        var direction = offset.LengthSquared() > 0.0001f
            ? offset.Normalized()
            : Heading.Forward(BodyFacing);

        return (body, new Obb(point + direction * clearance, size));
    }

    /// <summary>
    /// Радиус тела, которым проверяется выезд. Берётся у слота, который сейчас собирается:
    /// проверять надо того, кто действительно поедет. Пустая очередь — типовой бот,
    /// иначе стрелки выездов пропадали бы вместе с очередью.
    /// </summary>
    private float ProbeRadius => CurrentDefinition?.RadiusPx ?? Const.Unit * 0.35f;

    /// <summary>
    /// Мировые точки выезда: грань корпуса плюс <paramref name="extra"/> вдоль каждого
    /// направления rolloff. Порядок совпадает с приоритетом выпуска.
    /// </summary>
    private IEnumerable<Vector2> ExitMarkers(float extra)
    {
        var directions = Spec?.RolloffDirections;
        if (directions == null || directions.Length == 0)
            directions = new[] { Vector2.Right, Vector2.Left };

        var shape = Footprint;

        foreach (var local in directions)
        {
            if (local.LengthSquared() < 0.0001f)
                continue;

            var dir = local.Normalized().Rotated(BodyFacing);
            float edge = DistanceToEdge(shape, dir);
            yield return GlobalPosition + dir * (edge + extra);
        }
    }

    /// <summary>Расстояние от центра OBB до грани вдоль мирового единичного направления.</summary>
    private static float DistanceToEdge(in Obb shape, Vector2 worldDir)
    {
        var local = worldDir.Rotated(-shape.Angle);
        var half = shape.Half;
        float tx = Mathf.Abs(local.X) > 0.0001f ? half.X / Mathf.Abs(local.X) : float.PositiveInfinity;
        float ty = Mathf.Abs(local.Y) > 0.0001f ? half.Y / Mathf.Abs(local.Y) : float.PositiveInfinity;
        float edge = Mathf.Min(tx, ty);
        return float.IsFinite(edge) ? edge : half.Length();
    }

    public override void OnDestroyed()
    {
        // Выезжающий сам снимет Leaving, когда препятствие исчезнет из ObstacleMap
        _exiting = null;
        base.OnDestroyed();
    }

    public override void _Draw()
    {
        base._Draw();

        if (Definition == null)
            return;

        DrawExits();

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        float ratio = ProgressRatio;

        if (ratio > 0.01f)
        {
            var bar = new Rect2(-size.X * 0.4f, size.Y * 0.35f, size.X * 0.8f * ratio, 4f);
            ShapeDraw.Rect(this, bar, ShapeStyle.Solid(new Color(0.35f, 0.9f, 0.45f)));
        }
    }

    /// <summary>
    /// Стрелки выездов по rolloff_directions. Закрытый выход (чужая постройка) —
    /// приглушённый, открытый — яркий; так видно, куда пойдёт выпуск.
    ///
    /// У выделенного завода вдобавок рисуются сами области проверки. Без них
    /// «закрыто» приходится принимать на веру, а спорить с ним нечем: стрелка говорит
    /// о направлении, но молчит о том, какой прямоугольник и во что упёрся.
    /// </summary>
    private void DrawExits()
    {
        float probeRadius = ProbeRadius;
        float extra = probeRadius + Const.Unit * 0.25f;
        bool selected = GizmoGate.IsSelected(this);
        // Евклидова оценка: полный A* на каждый кадр отрисовки не запускаем
        var preferred = PickExit(probeRadius, extra, pathCost: false);

        foreach (var worldTip in ExitMarkers(extra))
        {
            var dir = worldTip - GlobalPosition;
            if (dir.LengthSquared() < 0.0001f)
                continue;

            dir = dir.Normalized();
            float edge = DistanceToEdge(Footprint, dir);
            var worldBase = GlobalPosition + dir * Mathf.Max(edge - Const.Unit * 0.15f, 0f);

            var from = ToLocal(worldBase);
            var to = ToLocal(worldTip);
            bool open = ExitUsable(worldTip, probeRadius);
            bool primary = open && worldTip.DistanceSquaredTo(preferred) < 0.01f;

            var line = open
                ? ShapeStyle.Outline(new Color(0.95f, 0.85f, 0.25f, 0.9f), 2.5f, WidthMode.Screen)
                : ShapeStyle.Outline(new Color(0.55f, 0.5f, 0.4f, 0.45f), 2f, WidthMode.Screen);
            var head = open
                ? ShapeStyle.Filled(new Color(0.95f, 0.85f, 0.25f, 0.95f),
                    new Color(0f, 0f, 0f, 0.4f), 1.5f, WidthMode.Screen)
                : ShapeStyle.Filled(new Color(0.5f, 0.45f, 0.35f, 0.5f),
                    new Color(0f, 0f, 0f, 0.3f), 1.2f, WidthMode.Screen);

            ShapeDraw.Arrow(this, from, to, line, headLength: Const.Unit * 0.35f);

            float mark = primary ? Const.Unit * 0.18f : Const.Unit * 0.12f;
            ShapeDraw.Circle(this, to, mark, head, 10);

            if (selected)
                DrawProbes(worldTip, probeRadius);
        }
    }

    /// <summary>
    /// Области проверки одного выезда. Занятая обводится красным, свободная — зелёным:
    /// так сразу видно, которая из них закрыла выход и какой постройкой. Области проверки
    /// продолжения нет, когда rolloff_clearance равен нулю, — рисовать тогда нечего.
    /// </summary>
    private void DrawProbes(Vector2 worldTip, float radius)
    {
        var (body, step) = ExitProbes(worldTip, radius);

        DrawProbe(body);

        if (step is { } ahead)
            DrawProbe(ahead);
    }

    private void DrawProbe(in Obb probe)
    {
        var obstacles = GameManager.I?.Obstacles;
        bool free = obstacles == null || !obstacles.Overlaps(probe, except: this);

        var style = free
            ? ShapeStyle.Outline(new Color(0.4f, 0.9f, 0.5f, 0.5f), 1.5f, WidthMode.Screen)
            : ShapeStyle.Outline(new Color(0.95f, 0.35f, 0.3f, 0.85f), 2f, WidthMode.Screen);

        ShapeDraw.Obb(this, probe, style);
    }
}
