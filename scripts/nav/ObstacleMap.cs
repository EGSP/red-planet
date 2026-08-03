using System.Collections.Generic;
using Godot;

/// <summary>
/// Препятствия мира в непрерывных координатах: множество ориентированных прямоугольников
/// и раскладка их по ячейкам для широкой фазы.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ РАСТРА. Задачи разные и точность нужна разная. Здесь решаются вопросы
/// геометрии — можно ли поставить, что под курсором, куда вытолкнуть юнита, — и решаются
/// точно. Растр навигации (<see cref="NavGrid"/>) выводится отсюда и огрубляет геометрию
/// сознательно, потому что поиск пути точности прямоугольника не требует.
///
/// ПРЯМОУГОЛЬНИК ЗАПОМИНАЕТСЯ ПРИ ДОБАВЛЕНИИ. К моменту снятия нода обычно уже освобождена
/// движком, и спросить у неё Footprint нельзя. Тот же приём применён в Spawner, который
/// хранит снимок регистрации ровно по этой причине.
///
/// Состав правит <see cref="Spawner"/>: добавляет при рождении сущности, снимает по выбытию
/// из индекса. Своей подписки карта не держит — иначе постройка, поставленная и проверенная
/// в одном кадре, попадала бы в карту только к концу кадра, и два каркаса встали бы на одно
/// место.
///
/// ЖУРНАЛ ИЗМЕНЕНИЙ нужен фоновому пересчёту растра: он читает не живые ноды, а снимки
/// прямоугольников и объединённую область правок с заданной ревизии.
/// </summary>
public sealed class ObstacleMap
{
    /// <summary>Сторона ячейки широкой фазы. Клетка застройки: здания крупнее неё редки.</summary>
    private const int BucketPx = Const.Unit;

    /// <summary>Сколько прошлых записей журнала держим, пока потребитель не догнал ревизию.</summary>
    private const int JournalKeep = 256;

    private readonly List<IObstacle> _items = new();

    /// <summary>Снимки прямоугольников. Ключ сравнивается по ссылке — Equals ноды звать нельзя.</summary>
    private readonly Dictionary<object, Obb> _shapes = new(ByReference.Instance);

    /// <summary>Ревизия, на которой препятствие появилось. Нужна временной маске добавлений.</summary>
    private readonly Dictionary<object, int> _bornAt = new(ByReference.Instance);

    private readonly Dictionary<Vector2I, List<IObstacle>> _buckets = new();

    private readonly List<ObstacleChange> _journal = new();

    /// <summary>Сколько раз менялся состав. По нему пересобирается растр навигации.</summary>
    public int Revision { get; private set; }

    /// <summary>Область последнего изменения — по ней выборочно устаревают пути.</summary>
    public Rect2 LastChange { get; private set; }

    public IReadOnlyList<IObstacle> All => _items;

    public int Count => _items.Count;

    /// <summary>Запомненный прямоугольник. У живой ноды совпадает с её Footprint.</summary>
    public Obb ShapeOf(IObstacle obstacle) =>
        _shapes.TryGetValue(obstacle, out var shape) ? shape : new Obb();

    public void Add(IObstacle obstacle)
    {
        if (obstacle == null || _shapes.ContainsKey(obstacle))
            return;

        var shape = obstacle.Footprint;

        _items.Add(obstacle);
        _shapes[obstacle] = shape;

        foreach (var cell in Cells(shape.Bounds))
            Bucket(cell).Add(obstacle);

        Touch(shape, added: true);
        _bornAt[obstacle] = Revision;
    }

    /// <summary>
    /// Снять препятствие. Повторный вызов безвреден: достройка каркаса снимает его
    /// немедленно, а подписка Spawner на выбытие из индекса — ещё раз в конце кадра.
    /// </summary>
    public void Remove(IObstacle obstacle)
    {
        if (obstacle == null || !_shapes.Remove(obstacle, out var shape))
            return;

        _bornAt.Remove(obstacle);

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_items[i], obstacle))
                continue;

            _items.RemoveAt(i);
            break;
        }

        foreach (var cell in Cells(shape.Bounds))
        {
            if (_buckets.TryGetValue(cell, out var bucket))
                bucket.Remove(obstacle);
        }

        Touch(shape, added: false);
    }

    /// <summary>Пересекается ли область с чем-нибудь. Касание краями пересечением не считается.</summary>
    public bool Overlaps(in Obb area, IObstacle except = null) => Blocker(area, except) != null;

    /// <summary>
    /// Кто именно мешает области. Постановке нужен не сам ответ «занято», а сосед, от которого
    /// придётся отодвинуться: прилипание стартовой точки считает выход из его прямоугольника.
    /// </summary>
    public IObstacle Blocker(in Obb area, IObstacle except = null)
    {
        foreach (var cell in Cells(area.Bounds))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            foreach (var obstacle in bucket)
            {
                if (ReferenceEquals(obstacle, except))
                    continue;

                if (_shapes.TryGetValue(obstacle, out var shape) && shape.Intersects(area))
                    return obstacle;
            }
        }

        return null;
    }

    /// <summary>Что накрывает точку. Нужен выбору цели под курсором.</summary>
    public IObstacle At(Vector2 point)
    {
        var cell = ToBucket(point);

        if (!_buckets.TryGetValue(cell, out var bucket))
            return null;

        foreach (var obstacle in bucket)
            if (_shapes.TryGetValue(obstacle, out var shape) && shape.HasPoint(point))
                return obstacle;

        return null;
    }

    /// <summary>
    /// Вытолкнуть окружность наружу из всех препятствий, которых она касается.
    ///
    /// Это ЖЁСТКОЕ ОГРАНИЧЕНИЕ, а не сила: применяется после интегрирования движения
    /// и решает задачу достоверно. Силой её решать нельзя — любая комбинация управляющих
    /// сил рано или поздно загонит юнита внутрь здания, и сила лишь уменьшает вероятность.
    ///
    /// <paramref name="except"/> — препятствие, из которого выталкивать не нужно:
    /// юнит, выезжающий из завода, ещё внутри его корпуса и не должен выскакивать
    /// к ближайшей грани вместо точки rolloff.
    /// </summary>
    public Vector2 PushOut(Vector2 position, float radius, IObstacle except = null)
    {
        var probe = new Rect2(position - new Vector2(radius, radius),
            new Vector2(radius * 2f, radius * 2f));

        foreach (var cell in Cells(probe))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            foreach (var obstacle in bucket)
            {
                if (ReferenceEquals(obstacle, except))
                    continue;

                if (_shapes.TryGetValue(obstacle, out var shape))
                    position = Eject(position, radius, shape);
            }
        }

        return position;
    }

    /// <summary>Есть ли препятствие ещё в карте. Нужно выезду: завод могли снести.</summary>
    public bool Contains(IObstacle obstacle) =>
        obstacle != null && _shapes.ContainsKey(obstacle);

    /// <summary>
    /// Пересекается ли окружность с запомненным прямоугольником препятствия.
    /// Считается так же, как касание в Eject: ближайшая точка на OBB и расстояние до неё.
    /// </summary>
    public bool CircleHits(IObstacle obstacle, Vector2 position, float radius)
    {
        if (!_shapes.TryGetValue(obstacle, out var shape))
            return false;

        var local = shape.ToLocal(position);
        var half = shape.Half;
        var closest = new Vector2(
            Mathf.Clamp(local.X, -half.X, half.X),
            Mathf.Clamp(local.Y, -half.Y, half.Y));

        return (local - closest).LengthSquared() < radius * radius;
    }

    /// <summary>Копия всех запомненных прямоугольников — вход фонового пересчёта.</summary>
    public Obb[] SnapshotShapes()
    {
        var shapes = new Obb[_items.Count];

        for (int i = 0; i < _items.Count; i++)
            shapes[i] = _shapes[_items[i]];

        return shapes;
    }

    /// <summary>
    /// Объединить области журнала строго после <paramref name="afterRevision"/>.
    /// Пустой прямоугольник означает, что после указанной ревизии правок не было.
    /// </summary>
    public Rect2 ChangesSince(int afterRevision)
    {
        bool any = false;
        float x0 = 0f, y0 = 0f, x1 = 0f, y1 = 0f;

        foreach (var change in _journal)
        {
            if (change.Revision <= afterRevision)
                continue;

            var bounds = change.Bounds;

            if (!any)
            {
                x0 = bounds.Position.X;
                y0 = bounds.Position.Y;
                x1 = bounds.End.X;
                y1 = bounds.End.Y;
                any = true;
                continue;
            }

            x0 = Mathf.Min(x0, bounds.Position.X);
            y0 = Mathf.Min(y0, bounds.Position.Y);
            x1 = Mathf.Max(x1, bounds.End.X);
            y1 = Mathf.Max(y1, bounds.End.Y);
        }

        return any ? new Rect2(x0, y0, x1 - x0, y1 - y0) : new Rect2();
    }

    /// <summary>
    /// Прямоугольники, добавленные после указанной ревизии и ещё присутствующие в карте.
    /// Нужны временной маске непроходимости, пока фоновый снимок не опубликован.
    /// </summary>
    public void CopyAddsSince(int afterRevision, List<Obb> destination)
    {
        destination.Clear();

        foreach (var obstacle in _items)
        {
            if (!_bornAt.TryGetValue(obstacle, out int born) || born <= afterRevision)
                continue;

            destination.Add(_shapes[obstacle]);
        }
    }

    /// <summary>
    /// Вытолкнуть из одного прямоугольника. Считается в его собственных координатах:
    /// повёрнутый прямоугольник там снова осепараллелен, а окружность от поворота
    /// не меняется вовсе, поэтому весь разбор случаев остаётся прежним.
    ///
    /// Случая два, и они разные: снаружи двигаем по нормали от ближайшей точки, внутри —
    /// к ближайшей грани, иначе центр внутри здания не имеет направления выхода вовсе.
    /// </summary>
    private static Vector2 Eject(Vector2 position, float radius, in Obb shape)
    {
        var local = shape.ToLocal(position);
        var half = shape.Half;

        if (Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y)
        {
            float left = local.X + half.X;
            float right = half.X - local.X;
            float top = local.Y + half.Y;
            float bottom = half.Y - local.Y;

            float least = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));

            var pushed = least switch
            {
                _ when Mathf.IsEqualApprox(least, left) => new Vector2(-half.X - radius, local.Y),
                _ when Mathf.IsEqualApprox(least, right) => new Vector2(half.X + radius, local.Y),
                _ when Mathf.IsEqualApprox(least, top) => new Vector2(local.X, -half.Y - radius),
                _ => new Vector2(local.X, half.Y + radius),
            };

            return shape.ToGlobal(pushed);
        }

        var closest = new Vector2(
            Mathf.Clamp(local.X, -half.X, half.X),
            Mathf.Clamp(local.Y, -half.Y, half.Y));

        var delta = local - closest;
        float distance = delta.Length();

        if (distance >= radius)
            return position;

        var direction = distance > 0.001f ? delta / distance : Vector2.Up;
        return shape.ToGlobal(closest + direction * radius);
    }

    private void Touch(in Obb shape, bool added)
    {
        Revision++;
        LastChange = shape.Bounds;
        _journal.Add(new ObstacleChange(Revision, shape, added));

        if (_journal.Count > JournalKeep)
            _journal.RemoveRange(0, _journal.Count - JournalKeep);
    }

    private List<IObstacle> Bucket(Vector2I cell)
    {
        if (_buckets.TryGetValue(cell, out var bucket))
            return bucket;

        bucket = new List<IObstacle>();
        _buckets[cell] = bucket;
        return bucket;
    }

    private static Vector2I ToBucket(Vector2 point) => new(
        Mathf.FloorToInt(point.X / BucketPx),
        Mathf.FloorToInt(point.Y / BucketPx));

    /// <summary>Ячейки широкой фазы, которые задевает прямоугольник.</summary>
    private static IEnumerable<Vector2I> Cells(Rect2 rect)
    {
        var min = ToBucket(rect.Position);
        var max = ToBucket(rect.End);

        for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
                yield return new Vector2I(x, y);
    }
}
