using System.Collections.Generic;
using Godot;

/// <summary>
/// Препятствия мира в непрерывных координатах: множество прямоугольников и раскладка их
/// по ячейкам для широкой фазы.
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
/// </summary>
public sealed class ObstacleMap
{
    /// <summary>Сторона ячейки широкой фазы. Клетка застройки: здания крупнее неё редки.</summary>
    private const int BucketPx = Const.Unit;

    private readonly List<IObstacle> _items = new();

    /// <summary>Снимки прямоугольников. Ключ сравнивается по ссылке — Equals ноды звать нельзя.</summary>
    private readonly Dictionary<object, Rect2> _rects = new(ByReference.Instance);

    private readonly Dictionary<Vector2I, List<IObstacle>> _buckets = new();

    /// <summary>Сколько раз менялся состав. По нему пересобирается растр навигации.</summary>
    public int Revision { get; private set; }

    /// <summary>Прямоугольник последнего изменения — по нему выборочно устаревают пути.</summary>
    public Rect2 LastChange { get; private set; }

    public IReadOnlyList<IObstacle> All => _items;

    public int Count => _items.Count;

    /// <summary>Запомненный прямоугольник. У живой ноды совпадает с её Footprint.</summary>
    public Rect2 RectOf(IObstacle obstacle) =>
        _rects.TryGetValue(obstacle, out var rect) ? rect : new Rect2();

    public void Add(IObstacle obstacle)
    {
        if (obstacle == null || _rects.ContainsKey(obstacle))
            return;

        var rect = obstacle.Footprint;

        _items.Add(obstacle);
        _rects[obstacle] = rect;

        foreach (var cell in Cells(rect))
            Bucket(cell).Add(obstacle);

        Touch(rect);
    }

    /// <summary>
    /// Снять препятствие. Повторный вызов безвреден: достройка каркаса снимает его
    /// немедленно, а подписка Spawner на выбытие из индекса — ещё раз в конце кадра.
    /// </summary>
    public void Remove(IObstacle obstacle)
    {
        if (obstacle == null || !_rects.Remove(obstacle, out var rect))
            return;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_items[i], obstacle))
                continue;

            _items.RemoveAt(i);
            break;
        }

        foreach (var cell in Cells(rect))
        {
            if (_buckets.TryGetValue(cell, out var bucket))
                bucket.Remove(obstacle);
        }

        Touch(rect);
    }

    /// <summary>Пересекается ли область с чем-нибудь. Касание краями пересечением не считается.</summary>
    public bool Overlaps(Rect2 area, IObstacle except = null)
    {
        foreach (var cell in Cells(area))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            foreach (var obstacle in bucket)
            {
                if (ReferenceEquals(obstacle, except))
                    continue;

                if (_rects.TryGetValue(obstacle, out var rect) && rect.Intersects(area))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Что накрывает точку. Нужен выбору цели под курсором.</summary>
    public IObstacle At(Vector2 point)
    {
        var cell = ToBucket(point);

        if (!_buckets.TryGetValue(cell, out var bucket))
            return null;

        foreach (var obstacle in bucket)
            if (_rects.TryGetValue(obstacle, out var rect) && rect.HasPoint(point))
                return obstacle;

        return null;
    }

    /// <summary>
    /// Вытолкнуть окружность наружу из всех препятствий, которых она касается.
    ///
    /// Это ЖЁСТКОЕ ОГРАНИЧЕНИЕ, а не сила: применяется после интегрирования движения
    /// и решает задачу достоверно. Силой её решать нельзя — любая комбинация управляющих
    /// сил рано или поздно загонит юнита внутрь здания, и сила лишь уменьшает вероятность.
    /// </summary>
    public Vector2 PushOut(Vector2 position, float radius)
    {
        var probe = new Rect2(position - new Vector2(radius, radius),
            new Vector2(radius * 2f, radius * 2f));

        foreach (var cell in Cells(probe))
        {
            if (!_buckets.TryGetValue(cell, out var bucket))
                continue;

            foreach (var obstacle in bucket)
            {
                if (_rects.TryGetValue(obstacle, out var rect))
                    position = Eject(position, radius, rect);
            }
        }

        return position;
    }

    /// <summary>
    /// Выталкивание из одного прямоугольника. Два случая разные: снаружи двигаем по нормали
    /// от ближайшей точки, внутри — к ближайшей грани, иначе центр внутри здания не имеет
    /// направления выхода вовсе.
    /// </summary>
    private static Vector2 Eject(Vector2 position, float radius, Rect2 rect)
    {
        if (rect.HasPoint(position))
        {
            float left = position.X - rect.Position.X;
            float right = rect.End.X - position.X;
            float top = position.Y - rect.Position.Y;
            float bottom = rect.End.Y - position.Y;

            float least = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));

            if (Mathf.IsEqualApprox(least, left))
                return new Vector2(rect.Position.X - radius, position.Y);

            if (Mathf.IsEqualApprox(least, right))
                return new Vector2(rect.End.X + radius, position.Y);

            return Mathf.IsEqualApprox(least, top)
                ? new Vector2(position.X, rect.Position.Y - radius)
                : new Vector2(position.X, rect.End.Y + radius);
        }

        var closest = new Vector2(
            Mathf.Clamp(position.X, rect.Position.X, rect.End.X),
            Mathf.Clamp(position.Y, rect.Position.Y, rect.End.Y));

        var delta = position - closest;
        float distance = delta.Length();

        if (distance >= radius)
            return position;

        var direction = distance > 0.001f ? delta / distance : Vector2.Up;
        return closest + direction * radius;
    }

    private void Touch(Rect2 rect)
    {
        Revision++;
        LastChange = rect;
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
