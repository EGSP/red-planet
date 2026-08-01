using System.Collections.Generic;
using Godot;

/// <summary>
/// Как подвижная сущность попадает туда, куда её послали: следование по пути, локальный
/// обход соседей и жёсткие ограничения.
///
/// ТРИ СЛОЯ, И ПОРЯДОК МЕЖДУ НИМИ ЗНАЧИМ.
/// Первый — глобальный: <see cref="PathfindingSystem"/> даёт направление, обходящее здания.
/// Второй — локальный: силы boids правят курс, чтобы сущности не слипались и расходились
/// со встречными. Третий — ограничения: выталкивание из зданий и расталкивание перекрывшихся.
/// Третий слой применяется ПОСЛЕ интегрирования и решает задачу достоверно, тогда как
/// сила решала бы её вероятностно. Физического движка в проекте нет, поэтому оба
/// ограничения написаны здесь руками.
///
/// ЧЕГО СИСТЕМА НЕ ДЕЛАЕТ. Не выбирает цель и не завершает приказы: обработчик приказа
/// каждый кадр объявляет намерение через <see cref="Movement.Seek"/>, а сам решает,
/// дошёл ли исполнитель. Не подтверждённое намерение гаснет в конце шага.
/// </summary>
public partial class MovementSystem : GameSystem
{
    /// <summary>Сторона ячейки поиска соседей. Вдвое больше клетки: 3×3 покрывает радиус чутья.</summary>
    private const int BucketPx = Const.Unit * 2;

    /// <summary>Во сколько радиусов сущность замечает соседей.</summary>
    [Export] public float SenseFactor = 3.5f;

    [Export] public float AvoidWeight = 1.4f;

    [Export] public float AlignWeight = 0.35f;

    /// <summary>За сколько долей секунды скорость выходит на заданную.</summary>
    [Export] public float Responsiveness = 6f;

    /// <summary>Сколько проходов расталкивания за кадр.</summary>
    [Export] public int ResolvePasses = 2;

    /// <summary>Сколько секунд без продвижения считается застреванием.</summary>
    [Export] public float StuckTimeout = 1.5f;

    /// <summary>За сколько секунд до цели начинается замедление.</summary>
    [Export] public float BrakingTime = 0.25f;

    /// <summary>
    /// Ниже какой доли полной скорости замедление не опускается. Без нижней границы
    /// сущность подползает к цели бесконечно и никогда не считается дошедшей.
    /// </summary>
    [Export] public float MinApproachSpeed = 0.3f;

    private readonly List<IMobile> _actors = new();
    private readonly Dictionary<Vector2I, List<int>> _buckets = new();
    private readonly List<int> _nearby = new();

    private PathfindingSystem _pathfinding;

    protected override void OnLink() => _pathfinding = GM.System<PathfindingSystem>();

    public override void Step(double dt)
    {
        Collect();

        for (int i = 0; i < _actors.Count; i++)
            Steer(i, dt);

        for (int i = 0; i < _actors.Count; i++)
            Integrate(_actors[i], dt);

        for (int pass = 0; pass < ResolvePasses; pass++)
            Resolve();

        // Намерение живёт один кадр: подтвердит его обработчик приказа — сущность
        // пойдёт дальше, не подтвердит — она сама собой станет удерживающей позицию
        foreach (var actor in _actors)
            actor.Movement.Active = false;
    }

    /// <summary>Список подвижных и раскладка по ячейкам. Пересобирается каждый кадр.</summary>
    private void Collect()
    {
        _actors.Clear();

        foreach (var bucket in _buckets.Values)
            bucket.Clear();

        foreach (var mobile in GM.Index.All<IMobile>())
        {
            if (mobile.Definition == null)
                continue;

            int at = _actors.Count;
            _actors.Add(mobile);
            Bucket(ToBucket(mobile.GlobalPosition)).Add(at);
        }
    }

    // ── управление ────────────────────────────────────────────────────────────────

    private void Steer(int index, double dt)
    {
        var mobile = _actors[index];
        var movement = mobile.Movement;
        var definition = mobile.Definition;

        if (!movement.Active || definition.SpeedPx <= 0f)
        {
            movement.Settled = false;
            Halt(movement);
            return;
        }

        var position = mobile.GlobalPosition;
        float radius = mobile.HitRadius;

        var handle = _pathfinding?.Request(mobile, position, movement.Goal, radius);

        // Настоящая цель — та, куда ведёт путь. От заданной она отличается, когда точка
        // лежит внутри постройки: поиск переносит её на ближайшее свободное место.
        // Считать прибытие по заданной точке значило бы упереться в стену навсегда
        var target = handle?.Goal ?? movement.Goal;

        movement.Blocked = handle is { Status: PathStatus.Unreachable };
        movement.Settled = movement.Blocked
                           || position.DistanceTo(target) <= movement.StopDistance
                           || Crowded(index, mobile, position, radius, target);

        if (movement.Settled)
        {
            Halt(movement);
            return;
        }

        var seek = Follow(handle, position, movement, radius);

        if (seek == Vector2.Zero)
        {
            Halt(movement);
            return;
        }

        Neighbours(index, position, radius * SenseFactor);

        var avoid = Avoidance(mobile, movement, position, seek, radius * SenseFactor);
        var align = Alignment(mobile, position, radius * SenseFactor);

        movement.SeekForce = seek;
        movement.AvoidForce = avoid;
        movement.AlignForce = align;
        movement.SeekScale = SeekScale(mobile, position, seek, radius);
        movement.Neighbours = _nearby.Count;

        var steer = seek * movement.SeekScale + avoid * AvoidWeight + align * AlignWeight;

        var desired = steer.LengthSquared() > 0.000001f
            ? steer.Normalized() * Approach(position, movement, target, definition.SpeedPx)
            : Vector2.Zero;

        movement.Velocity = movement.Velocity.MoveToward(desired,
            definition.SpeedPx * Responsiveness * (float)dt);
    }

    private static void Halt(Movement movement)
    {
        movement.Velocity = Vector2.Zero;
        movement.SeekForce = Vector2.Zero;
        movement.AvoidForce = Vector2.Zero;
        movement.AlignForce = Vector2.Zero;
        movement.AvoidSide = 0;
        movement.SeekScale = 1f;
        movement.StuckFor = 0f;
        movement.Neighbours = 0;
    }

    /// <summary>
    /// Направление по пути. Системы поиска может не быть в сцене — тогда идём напрямую
    /// и препятствий не замечаем: это вырожденный случай, а не рабочий режим.
    /// </summary>
    private static Vector2 Follow(PathHandle handle, Vector2 position, Movement movement,
        float radius)
    {
        if (handle == null)
        {
            var direct = movement.Goal - position;
            return direct.LengthSquared() > 0.0001f ? direct.Normalized() : Vector2.Zero;
        }

        // Точку считаем пройденной, не доходя до неё вплотную: ломаная идёт по центрам
        // ячеек, и требовать попадания в центр значило бы вилять на каждом повороте
        handle.Advance(position, Mathf.Max(radius, NavGrid.Cell * 0.75f));

        return handle.Direction(position);
    }

    /// <summary>
    /// Синхронная остановка: сущность у самой цели и упирается в своего, который стоит
    /// к ней ближе. Значит ближе не пройти, и дальше идти незачем.
    ///
    /// Правило действует только в зоне прибытия. Без этого ограничения отряд встал бы
    /// растянутой цепочкой: каждый останавливался бы, едва упёршись в идущего впереди.
    /// </summary>
    private bool Crowded(int self, IMobile mobile, Vector2 position, float radius, Vector2 target)
    {
        var movement = mobile.Movement;
        float remaining = position.DistanceTo(target);

        if (remaining - movement.StopDistance > radius * 3f)
            return false;

        Neighbours(self, position, radius * 2.5f);

        foreach (int index in _nearby)
        {
            var other = _actors[index];

            if (other.Faction != mobile.Faction)
                continue;

            float gap = position.DistanceTo(other.GlobalPosition);

            if (gap > radius + other.HitRadius + 2f)
                continue;

            if (other.GlobalPosition.DistanceTo(target) < remaining)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Скорость на подходе к цели. Замедление нужно, чтобы сущность не проскакивала точку
    /// остановки и не возвращалась к ней, раскачивая за собой всю группу.
    ///
    /// СНИЗУ СКОРОСТЬ ОГРАНИЧЕНА, и это существенно. Пропорциональное замедление само
    /// по себе даёт апорию: остаток пути стремится к нулю, скорость вместе с ним, и юнит
    /// подползает к цели бесконечно, ни разу её не достигнув. Нижняя граница гарантирует,
    /// что остаток закрывается за считанные кадры.
    /// </summary>
    private float Approach(Vector2 position, Movement movement, Vector2 target, float speedPx)
    {
        float remaining = position.DistanceTo(target) - movement.StopDistance;

        if (remaining >= speedPx * BrakingTime)
            return speedPx;

        return Mathf.Clamp(remaining / BrakingTime, speedPx * MinApproachSpeed, speedPx);
    }

    // ── boids ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Обход, а не расталкивание. Сила отталкивания в скоплении гасит стремление к цели —
    /// они направлены навстречу и взаимно уничтожаются, отчего сущность просто стоит.
    /// Обход правит курс мимо соседа и такого вырождения не даёт.
    ///
    /// Сторона обхода фиксируется, пока сила действует: иначе курс дребезжит между двумя
    /// соседями по разные стороны от направления движения. Освободился путь — фиксация
    /// снимается, и сущность возвращается к цели, а не идёт вдоль скопления дальше.
    ///
    /// Обходят не всех: своего, который сам идёт, обходить не нужно — с ним разберутся
    /// выравнивание и расталкивание. Обходят удерживающего позицию и чужого.
    /// </summary>
    private Vector2 Avoidance(IMobile mobile, Movement movement, Vector2 position,
        Vector2 seek, float sense)
    {
        float total = 0f;
        float strongest = 0f;
        int side = 0;

        foreach (int index in _nearby)
        {
            var other = _actors[index];

            if (other.Faction == mobile.Faction && !other.Movement.HoldGround)
                continue;

            var delta = other.GlobalPosition - position;
            float distance = delta.Length();

            if (distance < 0.001f || distance > sense)
                continue;

            var direction = delta / distance;
            float ahead = seek.Dot(direction);

            if (ahead < 0.2f)
                continue;

            float weight = (1f - distance / sense) * ahead;
            total += weight;

            if (weight <= strongest)
                continue;

            strongest = weight;

            // Уходим В СТОРОНУ, ПРОТИВОПОЛОЖНУЮ соседу. Знак выведен из определения
            // Orthogonal(): для курса (1,0) она даёт (0,−1). Сосед снизу, направление (0,1),
            // даёт положительное векторное произведение — значит уводить надо
            // положительным множителем, то есть вверх. Обратный знак разворачивал бы
            // юнита прямо в соседа, и обход читался бы как притяжение
            side = seek.Cross(direction) > 0f ? 1 : -1;
        }

        if (total <= 0.001f)
        {
            movement.AvoidSide = 0;
            return Vector2.Zero;
        }

        if (movement.AvoidSide == 0)
            movement.AvoidSide = side == 0 ? 1 : side;

        return seek.Orthogonal() * movement.AvoidSide * total;
    }

    /// <summary>
    /// Выравнивание скорости со своими. Единственная сила сплочения, которая здесь нужна:
    /// притяжение к центру группы пришлось бы отключать при встречном движении, а сила,
    /// которую сразу отключают, не нужна вовсе.
    /// </summary>
    private Vector2 Alignment(IMobile mobile, Vector2 position, float sense)
    {
        var sum = Vector2.Zero;
        int count = 0;

        foreach (int index in _nearby)
        {
            var other = _actors[index];

            if (other.Faction != mobile.Faction || !other.Movement.Active)
                continue;

            if (position.DistanceTo(other.GlobalPosition) > sense)
                continue;

            sum += other.Movement.Velocity;
            count++;
        }

        if (count == 0 || sum.LengthSquared() < 0.0001f)
            return Vector2.Zero;

        return sum.Normalized();
    }

    /// <summary>
    /// Правило «кто кого толкает»: стремление к цели ослабляется тем сильнее, чем точнее
    /// оно направлено в близкого соседа. Насколько именно — зависит от того, кто сосед.
    ///
    /// Отсюда же берётся окружение: цель, к которой прижались враги, перестаёт их
    /// расталкивать и остаётся на месте.
    /// </summary>
    private float SeekScale(IMobile mobile, Vector2 position, Vector2 seek, float radius)
    {
        float scale = 1f;

        foreach (int index in _nearby)
        {
            var other = _actors[index];

            float contact = radius + other.HitRadius + radius * 0.5f;
            var delta = other.GlobalPosition - position;
            float distance = delta.Length();

            if (distance < 0.001f || distance > contact)
                continue;

            float into = seek.Dot(delta / distance);

            if (into <= 0f)
                continue;

            float weight = other.Faction != mobile.Faction ? 0.9f
                : other.Movement.HoldGround ? 0.5f
                : 0.1f;

            float proximity = 1f - distance / contact;
            scale = Mathf.Min(scale, 1f - into * weight * proximity);
        }

        return Mathf.Clamp(scale, 0f, 1f);
    }

    // ── интегрирование и ограничения ──────────────────────────────────────────────

    private void Integrate(IMobile mobile, double dt)
    {
        var movement = mobile.Movement;

        if (movement.Velocity.LengthSquared() < 0.0001f)
            return;

        var before = mobile.GlobalPosition;
        mobile.GlobalPosition = before + movement.Velocity * (float)dt;

        // Корпус смотрит туда, куда сущность едет, а не туда, куда её послали: на обходе
        // это разные направления, и разворот к цели выглядел бы боком вперёд
        Turn(mobile, movement.Velocity, dt);

        float expected = movement.Velocity.Length() * (float)dt;
        float actual = before.DistanceTo(mobile.GlobalPosition);

        if (actual < expected * 0.2f)
            Stall(mobile, movement, dt);
        else
            movement.StuckFor = 0f;
    }

    private static void Turn(IMobile mobile, Vector2 direction, double dt)
    {
        float desired = direction.Angle();
        float step = mobile.Definition.TurnSpeed * (float)dt;
        mobile.Rotation = Heading.TurnToward(mobile.Rotation, desired, step);
    }

    /// <summary>
    /// Сущность не продвигается. Через порог путь выбрасывается из кеша, и следующий запрос
    /// посчитает его заново — уже от нынешнего положения и по нынешнему растру. Без этого
    /// любая недоработка локального слоя оборачивается вечно стоящим юнитом.
    /// </summary>
    private void Stall(IMobile mobile, Movement movement, double dt)
    {
        movement.StuckFor += (float)dt;

        if (movement.StuckFor < StuckTimeout)
            return;

        movement.StuckFor = 0f;
        movement.AvoidSide = 0;
        _pathfinding?.Release(mobile);
    }

    /// <summary>
    /// Жёсткие ограничения: наружу из зданий, врозь из чужих корпусов, внутрь границ мира.
    /// Раскладка по ячейкам к этому мигу устарела на один проход — для расталкивания
    /// это несущественно, сдвиги здесь заведомо меньше ячейки.
    /// </summary>
    private void Resolve()
    {
        for (int i = 0; i < _actors.Count; i++)
        {
            var mobile = _actors[i];
            float radius = mobile.HitRadius;
            var position = mobile.GlobalPosition;

            Neighbours(i, position, radius * 2f);

            foreach (int index in _nearby)
            {
                var other = _actors[index];

                if (index <= i)
                    continue;

                var delta = other.GlobalPosition - position;
                float distance = delta.Length();
                float wanted = radius + other.HitRadius;

                if (distance >= wanted)
                    continue;

                var push = distance > 0.001f
                    ? delta / distance
                    : Vector2.Right.Rotated(i * 2.399f);

                float overlap = (wanted - distance) * 0.5f;

                position -= push * overlap;
                other.GlobalPosition += push * overlap;
            }

            position = GM.Obstacles.PushOut(position, radius);

            var bounds = Const.WorldBounds;
            position.X = Mathf.Clamp(position.X, bounds.Position.X + radius, bounds.End.X - radius);
            position.Y = Mathf.Clamp(position.Y, bounds.Position.Y + radius, bounds.End.Y - radius);

            mobile.GlobalPosition = position;
        }
    }

    // ── раскладка соседей ─────────────────────────────────────────────────────────

    /// <summary>Номера сущностей в окрестности, кроме самой спрашивающей.</summary>
    private void Neighbours(int self, Vector2 position, float sense)
    {
        _nearby.Clear();

        var center = ToBucket(position);
        int span = Mathf.Max(1, Mathf.CeilToInt(sense / BucketPx));

        for (int dy = -span; dy <= span; dy++)
        {
            for (int dx = -span; dx <= span; dx++)
            {
                var cell = new Vector2I(center.X + dx, center.Y + dy);

                if (!_buckets.TryGetValue(cell, out var bucket))
                    continue;

                foreach (int index in bucket)
                    if (index != self)
                        _nearby.Add(index);
            }
        }
    }

    private List<int> Bucket(Vector2I cell)
    {
        if (_buckets.TryGetValue(cell, out var bucket))
            return bucket;

        bucket = new List<int>();
        _buckets[cell] = bucket;
        return bucket;
    }

    private static Vector2I ToBucket(Vector2 position) => new(
        Mathf.FloorToInt(position.X / BucketPx),
        Mathf.FloorToInt(position.Y / BucketPx));

    /// <summary>Сколько подвижных сущностей обслужено в прошлом кадре. Читает панель отладки.</summary>
    public int Tracked => _actors.Count;

    /// <summary>Раскладка по ячейкам — рисует отладка.</summary>
    public IReadOnlyDictionary<Vector2I, List<int>> Buckets => _buckets;

    public int BucketSize => BucketPx;
}
