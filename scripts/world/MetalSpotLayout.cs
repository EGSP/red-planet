using System;
using System.Collections.Generic;
using Godot;

/// <summary>Форма области кластера руды.</summary>
public enum MetalClusterShape
{
    Blob,
    Vein,
    Arc,
    Scatter,
}

/// <summary>Один размещённый кластер: центр, форма, габарит и точки внутри.</summary>
public readonly struct MetalCluster
{
    public readonly Vector2 Center;
    public readonly MetalClusterShape Shape;
    public readonly float RadiusPx;
    public readonly IReadOnlyList<Vector2> Spots;

    public MetalCluster(
        Vector2 center,
        MetalClusterShape shape,
        float radiusPx,
        IReadOnlyList<Vector2> spots)
    {
        Center = center;
        Shape = shape;
        RadiusPx = radiusPx;
        Spots = spots;
    }
}

/// <summary>Результат раскладки: плоский список точек и метаданные кластеров.</summary>
public sealed class MetalSpotPlan
{
    public readonly List<Vector2> Spots = new();
    public readonly List<MetalCluster> Clusters = new();
}

/// <summary>
/// Раскладка точек метала: кластеры по кольцам мира при этих настройках и этом сиде.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ <see cref="MetalSpotSystem"/>. Раскладку нужно уметь показать
/// в редакторе, где ни мира, ни <c>GameManager</c> не существует. Оставь подсчёт внутри
/// системы, предпросмотр показывал бы похожую раскладку, а не ту же самую, — и доверять
/// ему было бы нельзя. Здесь же считает один и тот же код: система передаёт настоящую
/// проверку занятости, стенд — приблизительную.
///
/// Единственная разница между ними в том и состоит: система знает растр препятствий,
/// а стенд про него не знает и потому проверяет одни лишь границы. Пока в мире на момент
/// раскладки нет ни одной постройки, обе проверки дают один и тот же ответ; разойтись они
/// смогут снова, если в стартовой выдаче появится что-либо занимающее место.
/// </summary>
public static class MetalSpotLayout
{
    private static readonly MetalClusterShape[] Shapes =
    {
        MetalClusterShape.Blob,
        MetalClusterShape.Vein,
        MetalClusterShape.Arc,
        MetalClusterShape.Scatter,
    };

    /// <summary>
    /// Разложить кластеры и точки. <paramref name="free"/> отвечает на вопрос, свободно ли
    /// место под экстрактор; расстояние между точками и границы мира проверяются здесь.
    /// </summary>
    public static MetalSpotPlan Build(WorldSettings settings, ulong seed, Func<Vector2, bool> free)
    {
        var plan = new MetalSpotPlan();
        var rng = new RandomNumberGenerator();

        if (seed != 0)
            rng.Seed = seed;
        else
            rng.Randomize();

        if (settings?.Rings == null || settings.Rings.Length == 0)
            return plan;

        float spacing = settings.SpotSpacingPx;
        float spacingSquared = spacing * spacing;
        int attempts = Mathf.Max(settings.PlacementAttempts, 1);
        float clearance = Mathf.Max(settings.BaseClearance, 0);

        var centers = new List<(Vector2 Position, float Radius)>();

        for (int ringIndex = 0; ringIndex < settings.Rings.Length; ringIndex++)
        {
            var ring = settings.Rings[ringIndex];

            if (ring == null)
                continue;

            settings.RingBounds(ringIndex, out float inner, out float outer);

            if (outer <= clearance || outer <= inner)
                continue;

            float placeInner = Mathf.Max(inner, clearance);
            int countMin = Mathf.Min(ring.ClusterCountMin, ring.ClusterCountMax);
            int countMax = Mathf.Max(ring.ClusterCountMin, ring.ClusterCountMax);
            int clusterCount = rng.RandiRange(countMin, countMax);

            int spotsMin = Mathf.Max(Mathf.Min(ring.SpotsInClusterMin, ring.SpotsInClusterMax), 1);
            int spotsMax = Mathf.Max(Mathf.Max(ring.SpotsInClusterMin, ring.SpotsInClusterMax), 1);
            float clusterRadius = ClusterRadius(spotsMax, spacing);

            // Равные сектора по окружности плюс общий сдвиг кольца: без сдвига раскладка
            // совпадала бы от сида к сиду по сторонам света, со сдвигом — равномерна,
            // но не «по линейке».
            float sector = clusterCount > 0 ? Mathf.Tau / clusterCount : Mathf.Tau;
            float ringAngleOffset = rng.RandfRange(0f, Mathf.Tau);

            for (int c = 0; c < clusterCount; c++)
            {
                if (!TryPlaceCenter(rng, ring, placeInner, outer, clusterRadius, centers,
                        attempts, ringAngleOffset, sector, c, out var center))
                    continue;

                centers.Add((center, clusterRadius));

                int spots = rng.RandiRange(spotsMin, spotsMax);
                var shape = Shapes[rng.RandiRange(0, Shapes.Length - 1)];
                float facing = rng.RandfRange(0f, Mathf.Tau);

                var clusterSpots = PlaceClusterSpots(rng, free, plan.Spots, center, clusterRadius,
                    shape, facing, spacing, spacingSquared, attempts, spots, spotsMin);

                // Ниже минимума кластер не принимаем: иначе SpotsInClusterMin превращается
                // в пожелание, а формы вроде Arc регулярно отдавали одну-две точки.
                if (clusterSpots.Count < spotsMin)
                {
                    centers.RemoveAt(centers.Count - 1);
                    continue;
                }

                plan.Spots.AddRange(clusterSpots);
                plan.Clusters.Add(new MetalCluster(center, shape, clusterRadius, clusterSpots));
            }
        }

        return plan;
    }

    /// <summary>
    /// Набрать точки кластера. Сначала — по форме (для дуги и жилы слоты вдоль фигуры),
    /// затем добор случайными попытками, в том числе диском, пока не наберётся цель
    /// или не кончится бюджет. Возврат может быть короче цели, но вызывающий отбрасывает
    /// результат короче <paramref name="spotsMin"/>.
    /// </summary>
    private static List<Vector2> PlaceClusterSpots(
        RandomNumberGenerator rng,
        Func<Vector2, bool> free,
        List<Vector2> alreadyPlaced,
        Vector2 center,
        float clusterRadius,
        MetalClusterShape shape,
        float facing,
        float spacing,
        float spacingSquared,
        int attempts,
        int spots,
        int spotsMin)
    {
        var clusterSpots = new List<Vector2>();
        var local = new List<Vector2>();

        bool Accept(Vector2 candidate)
        {
            var snapped = SnapToCell(candidate);

            if (!free(snapped) ||
                TooClose(alreadyPlaced, snapped, spacingSquared) ||
                TooClose(local, snapped, spacingSquared))
                return false;

            local.Add(snapped);
            clusterSpots.Add(snapped);
            return true;
        }

        for (int s = 0; s < spots; s++)
        {
            bool placed = false;

            // Индексированный слот: дуга и жила иначе схлопываются в одни и те же клетки.
            for (int attempt = 0; attempt < attempts && !placed; attempt++)
            {
                var candidate = ShapePoint(rng, center, clusterRadius, shape, facing,
                    spacing, spots, s);
                placed = Accept(candidate);
            }

            for (int attempt = 0; attempt < attempts && !placed; attempt++)
            {
                var candidate = ShapePoint(rng, center, clusterRadius, shape, facing,
                    spacing, spots, -1);
                placed = Accept(candidate);
            }
        }

        // Добор до минимума диском: форма могла не уместить все слоты у края карты.
        int fillAttempts = attempts * Mathf.Max(spotsMin - clusterSpots.Count, 0) * 2;

        for (int attempt = 0; attempt < fillAttempts && clusterSpots.Count < spotsMin; attempt++)
            Accept(BlobPoint(rng, center, clusterRadius));

        return clusterSpots;
    }

    /// <summary>Свободно ли место под экстрактор с точки зрения одних лишь границ мира.</summary>
    public static bool InsideWorld(Vector2 position)
    {
        float half = Const.Unit * 0.5f;
        var area = new Rect2(position - new Vector2(half, half), Const.Unit, Const.Unit);

        return World.Bounds.Encloses(area);
    }

    /// <summary>
    /// Габарит кластера от наибольшего числа рудников. Берётся максимум из плотной
    /// укладки в диске и половины длины жилы/хорды: иначе Arc и Vein при большом
    /// SpotsInClusterMax физически не вмещали минимум точек с заданным шагом.
    /// </summary>
    public static float ClusterRadius(int spotsMax, float spacing)
    {
        int n = Mathf.Max(spotsMax, 1);
        float packs = Mathf.Ceil(Mathf.Sqrt(n));
        float disk = packs * spacing * 0.55f;
        float line = n <= 1 ? 0f : (n - 1) * spacing * 0.5f;
        return Mathf.Max(Mathf.Max(disk, line), spacing * 0.5f);
    }

    /// <summary>
    /// Центр кластера в своём секторе окружности. Угол = сдвиг кольца + индекс сектора
    /// + небольшой разброс внутри сектора; радиус по-прежнему из кривой вероятности.
    /// </summary>
    private static bool TryPlaceCenter(
        RandomNumberGenerator rng,
        WorldRingDefinition ring,
        float inner,
        float outer,
        float clusterRadius,
        List<(Vector2 Position, float Radius)> centers,
        int attempts,
        float ringAngleOffset,
        float sector,
        int slot,
        out Vector2 center)
    {
        center = default;
        float minGap = clusterRadius;

        // Разброс внутри сектора: не больше ±35% ширины, чтобы соседние кластеры
        // не перескакивали в чужой сектор и не сбивались в одну четверть.
        float jitterSpan = sector * 0.35f;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float t = SampleRadial(rng, ring.ClusterProbabilityCurve);
            float radius = Mathf.Lerp(inner, outer, t);
            float jitter = rng.RandfRange(-jitterSpan, jitterSpan);
            float angle = ringAngleOffset + slot * sector + sector * 0.5f + jitter;
            var candidate = PointOnCell(angle, radius);

            bool far = true;

            foreach (var other in centers)
            {
                float need = other.Radius + minGap;
                if (candidate.DistanceSquaredTo(other.Position) < need * need)
                {
                    far = false;
                    break;
                }
            }

            if (!far || !InsideWorld(candidate))
                continue;

            center = candidate;
            return true;
        }

        return false;
    }

    private static Vector2 ShapePoint(
        RandomNumberGenerator rng,
        Vector2 center,
        float radius,
        MetalClusterShape shape,
        float facing,
        float spacing,
        int spots,
        int slot)
    {
        return shape switch
        {
            MetalClusterShape.Vein => VeinPoint(rng, center, radius, facing, spots, slot),
            MetalClusterShape.Arc => ArcPoint(rng, center, radius, facing, spacing, spots, slot),
            MetalClusterShape.Scatter => ScatterPoint(rng, center, radius, facing),
            _ => BlobPoint(rng, center, radius),
        };
    }

    /// <summary>Диск с неравномерным радиусом — основная «куча» в духе PA.</summary>
    private static Vector2 BlobPoint(RandomNumberGenerator rng, Vector2 center, float radius)
    {
        float angle = rng.RandfRange(0f, Mathf.Tau);
        float u = rng.Randf();
        float r = radius * Mathf.Sqrt(u) * rng.RandfRange(0.55f, 1f);
        return center + Heading.Forward(angle) * r;
    }

    /// <summary>Отрезок с поперечным разбросом — жила.</summary>
    private static Vector2 VeinPoint(
        RandomNumberGenerator rng, Vector2 center, float radius, float facing, int spots, int slot)
    {
        float along;

        if (slot >= 0 && spots > 1)
        {
            float t = slot / (float)(spots - 1);
            along = Mathf.Lerp(-1f, 1f, t) * radius;
            along += rng.RandfRange(-0.12f, 0.12f) * radius;
        }
        else
        {
            along = rng.RandfRange(-1f, 1f) * radius;
        }

        float across = rng.RandfRange(-0.35f, 0.35f) * radius;
        var axis = Heading.Forward(facing);
        var normal = new Vector2(-axis.Y, axis.X);
        return center + axis * along + normal * across;
    }

    /// <summary>
    /// Дуга вокруг центра кластера. Раствор считается от числа точек и шага между ними:
    /// фиксированные 70° при SpotSpacing в несколько клеток вмещали лишь одну-две точки
    /// после привязки к сетке.
    /// </summary>
    private static Vector2 ArcPoint(
        RandomNumberGenerator rng,
        Vector2 center,
        float radius,
        float facing,
        float spacing,
        int spots,
        int slot)
    {
        float span = ArcSpan(radius, spacing, spots);
        float angle;

        if (slot >= 0 && spots > 1)
        {
            float t = slot / (float)(spots - 1);
            angle = facing - span * 0.5f + span * t;
            angle += rng.RandfRange(-0.08f, 0.08f) * span;
        }
        else if (slot == 0 || spots <= 1)
        {
            angle = facing;
        }
        else
        {
            angle = facing + rng.RandfRange(-span * 0.5f, span * 0.5f);
        }

        float r = radius * rng.RandfRange(0.7f, 1f);
        return center + Heading.Forward(angle) * r;
    }

    /// <summary>
    /// Угловой раствор, при котором на средней окружности кластера помещается
    /// <paramref name="spots"/> точек с заданным шагом.
    /// </summary>
    private static float ArcSpan(float radius, float spacing, int spots)
    {
        if (spots <= 1)
            return Mathf.DegToRad(40f);

        float meanRadius = Mathf.Max(radius * 0.85f, spacing * 0.5f);
        float needed = (spots - 1) * spacing / meanRadius;
        return Mathf.Clamp(needed, Mathf.DegToRad(40f), Mathf.Tau * 0.85f);
    }

    /// <summary>Разреженный эллипс — рыхлый кластер.</summary>
    private static Vector2 ScatterPoint(
        RandomNumberGenerator rng, Vector2 center, float radius, float facing)
    {
        float angle = rng.RandfRange(0f, Mathf.Tau);
        float u = rng.Randf();
        float r = radius * Mathf.Sqrt(u) * rng.RandfRange(0.75f, 1.25f);
        var local = new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 0.55f);
        float cos = Mathf.Cos(facing);
        float sin = Mathf.Sin(facing);
        return center + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
    }

    private static float SampleRadial(RandomNumberGenerator rng, Curve curve)
    {
        for (int i = 0; i < 64; i++)
        {
            float t = rng.Randf();
            float weight = curve != null ? curve.Sample(t) : DefaultRadialWeight(t);

            if (rng.Randf() <= Mathf.Clamp(weight, 0f, 1f))
                return t;
        }

        return 0.5f;
    }

    /// <summary>Колокол с центром посередине пояса, если кривая в ресурсе не задана.</summary>
    private static float DefaultRadialWeight(float t)
    {
        float x = (t - 0.5f) / 0.22f;
        return Mathf.Exp(-0.5f * x * x);
    }

    private static bool TooClose(List<Vector2> placed, Vector2 position, float spacingSquared)
    {
        foreach (var other in placed)
            if (other.DistanceSquaredTo(position) < spacingSquared)
                return true;

        return false;
    }

    private static Vector2 PointOnCell(float angle, float radiusCells)
    {
        var cell = new Vector2I(
            Mathf.RoundToInt(Mathf.Cos(angle) * radiusCells),
            Mathf.RoundToInt(Mathf.Sin(angle) * radiusCells));

        return Const.CellCenter(cell);
    }

    private static Vector2 SnapToCell(Vector2 position)
    {
        var cell = new Vector2I(
            Mathf.RoundToInt(position.X / Const.Unit - 0.5f),
            Mathf.RoundToInt(position.Y / Const.Unit - 0.5f));

        return Const.CellCenter(cell);
    }
}
