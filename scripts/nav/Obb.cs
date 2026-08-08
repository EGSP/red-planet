using Godot;

/// <summary>
/// Ориентированный прямоугольник: центр, полные размеры и угол поворота.
///
/// ЗАЧЕМ ОН ПОЯВИЛСЯ. Раньше место занимал осепараллельный Rect2, потому что здание ставилось
/// по клеткам и повернуть его было нельзя. Постановка с поворотом это правило отменяет:
/// прямоугольник, повёрнутый на произвольный угол, осепараллельным описать нельзя, а описанная
/// вокруг него окружность раздувает занятое место в полтора раза и портит плотную застройку.
///
/// ГДЕ ЕЩЁ ОСТАЛСЯ Rect2. Широкая фаза раскладывает препятствия по ячейкам, и для неё нужны
/// именно осепараллельные границы (<see cref="Bounds"/>): грубая проверка обязана быть дешёвой,
/// а точность в ней не нужна. Растр навигации получает границы по той же причине, а точное
/// пересечение проверяет уже по самому прямоугольнику.
///
/// Угол в радианах, 0 — размер X вдоль мировой оси X, как у <see cref="Node2D.Rotation"/>.
/// </summary>
public readonly struct Obb
{
    public readonly Vector2 Center;

    /// <summary>Полные размеры, а не половинные: так же, как <see cref="Rect2.Size"/>.</summary>
    public readonly Vector2 Size;

    public readonly float Angle;

    public Obb(Vector2 center, Vector2 size, float angle = 0f)
    {
        Center = center;
        Size = size;
        Angle = angle;
    }

    /// <summary>Осепараллельный прямоугольник как частный случай повёрнутого на ноль.</summary>
    public static Obb FromRect(Rect2 rect) =>
        new(rect.Position + rect.Size * 0.5f, rect.Size);

    public Vector2 Half => Size * 0.5f;

    /// <summary>Своя ось X в мировых координатах.</summary>
    public Vector2 AxisX => Vector2.Right.Rotated(Angle);

    /// <summary>Своя ось Y. Направление вниз, как принято в экранных координатах движка.</summary>
    public Vector2 AxisY => Vector2.Down.Rotated(Angle);

    public bool IsEmpty => Size.X <= 0f || Size.Y <= 0f;

    /// <summary>
    /// Осепараллельные границы: то, что видит широкая фаза. У неповёрнутого совпадают
    /// с ним самим, у повёрнутого описывают его целиком и потому годятся только
    /// для грубого отсева.
    /// </summary>
    public Rect2 Bounds
    {
        get
        {
            float cos = Mathf.Abs(Mathf.Cos(Angle));
            float sin = Mathf.Abs(Mathf.Sin(Angle));
            var half = Half;

            var extent = new Vector2(
                half.X * cos + half.Y * sin,
                half.X * sin + half.Y * cos);

            return new Rect2(Center - extent, extent * 2f);
        }
    }

    /// <summary>Раздать во все стороны — обязательный зазор вокруг строения задаётся так.</summary>
    public Obb Grow(float amount) =>
        new(Center, Size + new Vector2(amount, amount) * 2f, Angle);

    public Obb MovedTo(Vector2 center) => new(center, Size, Angle);

    /// <summary>Точка в системе координат прямоугольника: центр в нуле, оси вдоль сторон.</summary>
    public Vector2 ToLocal(Vector2 point) => (point - Center).Rotated(-Angle);

    public Vector2 ToGlobal(Vector2 local) => Center + local.Rotated(Angle);

    public bool HasPoint(Vector2 point)
    {
        var local = ToLocal(point);
        var half = Half;

        return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y;
    }

    /// <summary>
    /// Расстояние от точки до ближайшей точки прямоугольника. Внутри и на грани — ноль.
    ///
    /// По нему считается досягаемость инструмента (см. <see cref="Reach"/>): мерить до центра
    /// нельзя, потому что у постройки два на четыре центр отстоит от края на две клетки,
    /// а по описанной окружности — потому что она захватывает пустое место за углом.
    /// </summary>
    public float DistanceTo(Vector2 point)
    {
        var local = ToLocal(point);
        var half = Half;

        float x = Mathf.Max(Mathf.Abs(local.X) - half.X, 0f);
        float y = Mathf.Max(Mathf.Abs(local.Y) - half.Y, 0f);

        return Mathf.Sqrt(x * x + y * y);
    }

    /// <summary>
    /// Половина тени прямоугольника на направлении. Раскладка застройки считает по ней
    /// промежуток между соседями: у повёрнутого строения поперечник вдоль ряда
    /// не совпадает ни с одной из его сторон.
    /// </summary>
    public float Reach(Vector2 axis) => Extent(this, axis);

    /// <summary>Четыре угла по часовой стрелке. Нужны отрисовке.</summary>
    public Vector2[] Corners()
    {
        var half = Half;

        return new[]
        {
            ToGlobal(new Vector2(-half.X, -half.Y)),
            ToGlobal(new Vector2(half.X, -half.Y)),
            ToGlobal(new Vector2(half.X, half.Y)),
            ToGlobal(new Vector2(-half.X, half.Y)),
        };
    }

    /// <summary>
    /// Пересечение по теореме о разделяющей оси. Осей всего четыре — по две стороны
    /// у каждого прямоугольника, — и достаточно одной разделяющей, чтобы дать отказ.
    ///
    /// Касание краями пересечением НЕ считается: то же правило действовало у Rect2,
    /// и на нём держится постановка вплотную с обязательным зазором.
    /// </summary>
    public bool Intersects(in Obb other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        // Грубый отсев осепараллельными границами: сравнение четырёх чисел вместо восьми
        // скалярных произведений, а отвергает оно подавляющее большинство пар
        if (!Bounds.Intersects(other.Bounds))
            return false;

        return !Separates(this, other) && !Separates(other, this);
    }

    /// <summary>
    /// Насколько глубоко прямоугольники сидят друг в друге и куда двигать this, чтобы
    /// вывести его наружу кратчайшим путём. Ложь означает, что двигать не нужно.
    ///
    /// Направление выхода выбирается по оси с наименьшим перекрытием: именно оно и даёт
    /// ощущение прилипания к ближайшей грани, а не отскока в произвольную сторону.
    /// </summary>
    public bool Escape(in Obb other, out Vector2 push)
    {
        push = Vector2.Zero;

        if (!Intersects(other))
            return false;

        float least = float.MaxValue;
        var direction = Vector2.Zero;

        // Оси обоих прямоугольников: разделяющей может оказаться любая из четырёх,
        // и наименьшее перекрытие тоже может обнаружиться на любой
        Probe(AxisX, other, ref least, ref direction);
        Probe(AxisY, other, ref least, ref direction);
        Probe(other.AxisX, other, ref least, ref direction);
        Probe(other.AxisY, other, ref least, ref direction);

        if (direction == Vector2.Zero)
            return false;

        // Запас в сотую пикселя: сдвиг ровно на перекрытие оставляет края в касании,
        // а после округлений оно с равной вероятностью читается и как пересечение
        push = direction * (least + 0.01f);
        return true;
    }

    /// <summary>
    /// Перекрытие вдоль одной оси. Направление разворачивается так, чтобы толкать this
    /// прочь от соседа, — иначе выход по кратчайшему пути уводил бы внутрь.
    /// </summary>
    private void Probe(Vector2 axis, in Obb other, ref float least, ref Vector2 direction)
    {
        float mine = Extent(this, axis);
        float theirs = Extent(other, axis);
        float gap = (other.Center - Center).Dot(axis);

        float overlap = mine + theirs - Mathf.Abs(gap);

        if (overlap >= least)
            return;

        least = overlap;
        direction = gap > 0f ? -axis : axis;
    }

    /// <summary>Разделены ли прямоугольники какой-нибудь из сторон первого.</summary>
    private static bool Separates(in Obb a, in Obb b)
    {
        var delta = b.Center - a.Center;

        return Apart(a.AxisX, a, b, delta) || Apart(a.AxisY, a, b, delta);
    }

    private static bool Apart(Vector2 axis, in Obb a, in Obb b, Vector2 delta) =>
        Mathf.Abs(delta.Dot(axis)) >= Extent(a, axis) + Extent(b, axis);

    /// <summary>Половина тени прямоугольника на оси.</summary>
    private static float Extent(in Obb box, Vector2 axis)
    {
        var half = box.Half;

        return Mathf.Abs(box.AxisX.Dot(axis)) * half.X + Mathf.Abs(box.AxisY.Dot(axis)) * half.Y;
    }
}
