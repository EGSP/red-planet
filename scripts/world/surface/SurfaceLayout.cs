using System;
using System.Collections.Generic;
using Godot;

/// <summary>Один отпечаток: где лежит, как повёрнут, какого размера и оттенка.</summary>
public readonly struct DecalPlacement
{
    public readonly Vector2 Position;
    public readonly float Rotation;
    public readonly float Size;
    public readonly Color Modulate;

    public DecalPlacement(Vector2 position, float rotation, float size, Color modulate)
    {
        Position = position;
        Rotation = rotation;
        Size = size;
        Modulate = modulate;
    }
}

/// <summary>Все отпечатки одной декали. Рисуются одним узлом, отсюда и группировка.</summary>
public sealed class SurfaceGroup
{
    public SurfaceDecal Decal;
    public List<DecalPlacement> Items = new();
}

/// <summary>Раскладка декалей по всей местности, упорядоченная по номеру слоя.</summary>
public sealed class SurfacePlan
{
    public readonly List<SurfaceGroup> Groups = new();

    /// <summary>Отпечаток настроек, при изменении которого раскладку нужно собрать заново.</summary>
    public int Signature;

    /// <summary>Сколько отпечатков размещено всего. Нужен для подписи в предпросмотре.</summary>
    public int Count;
}

/// <summary>
/// Размещение декалей по правилам местности.
///
/// ПОЧЕМУ РЕШЁТКА КАНДИДАТОВ, А НЕ СВОБОДНЫЙ РАЗБРОС. Точки, набранные подряд случайным
/// образом, слипаются: расстояние между соседями ничем не ограничено. Решётка с шагом
/// <see cref="SurfaceDecal.SpacingPx"/> и смещением внутри клетки даёт нижнюю границу
/// расстояния даром, а вид при достаточном смещении неотличим от свободного разброса.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ ПОТОК СЛУЧАЙНОСТИ НА КАЖДУЮ КЛЕТКУ. Клетка получает числа из своего
/// зерна, выведенного из зерна партии, номера декали и координат клетки. Отсюда два
/// свойства: раскладка не зависит от порядка обхода, и добавление новой декали не
/// сдвигает уже размещённые. Общий на весь проход генератор ни того, ни другого не даёт.
///
/// РАСКЛАДКА НЕ СОХРАНЯЕТСЯ. При воспроизведении партии из журнала она восстанавливается
/// из того же зерна, поэтому в документах ей места нет.
/// </summary>
public static class SurfaceLayout
{
    /// <summary>Наибольшее число отпечатков одной декали. Защита от слишком мелкого шага.</summary>
    private const int Limit = 20000;

    public static SurfacePlan Build(SurfaceSettings settings, SurfaceFields fields)
    {
        var plan = new SurfacePlan { Signature = SignatureOf(settings, fields) };

        if (settings?.Biomes == null || fields == null)
            return plan;

        var bounds = fields.Bounds;
        int index = 0;

        foreach (var biome in settings.Biomes)
        {
            if (biome?.Decals == null)
                continue;

            foreach (var decal in biome.Decals)
            {
                index++;

                if (decal?.Texture == null)
                    continue;

                var group = new SurfaceGroup { Decal = decal };

                Scatter(group, biome, decal, fields, bounds, (ulong)index);

                if (group.Items.Count == 0)
                    continue;

                plan.Groups.Add(group);
                plan.Count += group.Items.Count;
            }
        }

        plan.Groups.Sort((a, b) => a.Decal.Layer.CompareTo(b.Decal.Layer));
        return plan;
    }

    private static void Scatter(
        SurfaceGroup group,
        SurfaceBiome biome,
        SurfaceDecal decal,
        SurfaceFields fields,
        Rect2 bounds,
        ulong salt)
    {
        float step = Mathf.Max(decal.SpacingPx, 16f);
        int cols = Mathf.CeilToInt(bounds.Size.X / step);
        int rows = Mathf.CeilToInt(bounds.Size.Y / step);

        if (cols <= 0 || rows <= 0 || cols * rows > Limit * 4)
            return;

        var noise = decal.Noise?.Build(fields.Seed + salt * 31UL);

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                var random = new Stream(fields.Seed, salt, cx, cy);

                if (random.Next() > decal.Chance)
                    continue;

                var position = new Vector2(
                    bounds.Position.X + (cx + 0.5f + (random.Next() - 0.5f) * decal.Jitter) * step,
                    bounds.Position.Y + (cy + 0.5f + (random.Next() - 0.5f) * decal.Jitter) * step);

                if (!bounds.HasPoint(position))
                    continue;

                float temperature = fields.TemperatureAt(position);

                // Принадлежность биому проверяется вероятностью, а не порогом: у самой
                // границы отпечаток появляется реже, отчего стык двух биомов получается
                // рваным, а не отрезанным по линии
                float belongs = Band(temperature, biome.TemperatureRange, biome.Falloff);

                if (belongs <= 0f || random.Next() > belongs)
                    continue;

                if (!Inside(temperature, decal.TemperatureRange))
                    continue;

                if (noise != null &&
                    !Inside(decal.Noise.Sample(noise, position), decal.NoiseRange))
                    continue;

                if (fields.HasHeight && !Inside(fields.HeightAt(position), decal.HeightRange))
                    continue;

                float size = decal.SizePx *
                    (1f + (random.Next() * 2f - 1f) * decal.SizeVariation);

                float rotation = decal.RandomRotation ? random.Next() * Mathf.Tau : 0f;

                float shade = 1f + (random.Next() * 2f - 1f) * decal.TintVariation;
                var tint = new Color(
                    decal.Tint.R * shade,
                    decal.Tint.G * shade,
                    decal.Tint.B * shade,
                    decal.Tint.A * decal.Opacity);

                group.Items.Add(new DecalPlacement(position, rotation, Mathf.Max(size, 1f), tint));

                if (group.Items.Count >= Limit)
                    return;
            }
        }
    }

    /// <summary>Лежит ли значение в отрезке. Отрезок задан вектором: X — начало, Y — конец.</summary>
    private static bool Inside(float value, Vector2 range) =>
        value >= Mathf.Min(range.X, range.Y) && value <= Mathf.Max(range.X, range.Y);

    /// <summary>
    /// Принадлежность отрезку с размытыми краями: единица внутри, ноль дальше
    /// <paramref name="falloff"/> от края, плавный переход между ними.
    /// </summary>
    private static float Band(float value, Vector2 range, float falloff)
    {
        float low = Mathf.Min(range.X, range.Y);
        float high = Mathf.Max(range.X, range.Y);

        if (falloff <= 0f)
            return value >= low && value <= high ? 1f : 0f;

        float outside = Mathf.Max(low - value, value - high);
        return Mathf.Clamp(1f - outside / falloff, 0f, 1f);
    }

    /// <summary>
    /// Отпечаток раскладки: зерно, область и все правила размещения. Меняется — раскладку
    /// пересобирают, не меняется — оставляют как есть.
    /// </summary>
    public static int SignatureOf(SurfaceSettings settings, SurfaceFields fields)
    {
        if (settings == null || fields == null)
            return 0;

        int hash = fields.Signature;

        if (settings.Biomes == null)
            return hash;

        foreach (var biome in settings.Biomes)
        {
            if (biome == null)
                continue;

            hash = HashCode.Combine(hash, biome.TemperatureRange, biome.Falloff);

            if (biome.Decals == null)
                continue;

            foreach (var decal in biome.Decals)
            {
                if (decal == null)
                    continue;

                hash = HashCode.Combine(hash,
                    decal.Texture?.GetInstanceId() ?? 0UL,
                    HashCode.Combine(decal.Layer, decal.SpacingPx, decal.Chance, decal.Jitter),
                    HashCode.Combine(decal.SizePx, decal.SizeVariation, decal.RandomRotation),
                    HashCode.Combine(decal.NoiseRange, decal.TemperatureRange, decal.HeightRange),
                    HashCode.Combine(decal.Tint, decal.TintVariation, decal.Opacity),
                    SurfaceFields.SignatureOf(decal.Noise));
            }
        }

        return hash;
    }

    /// <summary>
    /// Поток случайных чисел одной клетки решётки. Зерно выводится смешиванием зерна партии,
    /// номера декали и координат клетки, поэтому клетки независимы друг от друга.
    /// </summary>
    private struct Stream
    {
        private ulong _state;

        public Stream(ulong seed, ulong salt, int cx, int cy)
        {
            ulong key = seed
                ^ (salt * 0x9E3779B97F4A7C15UL)
                ^ ((ulong)(uint)cx * 0xBF58476D1CE4E5B9UL)
                ^ ((ulong)(uint)cy * 0x94D049BB133111EBUL);

            _state = Mix(key | 1UL);
        }

        /// <summary>Следующее число от нуля до единицы.</summary>
        public float Next()
        {
            _state = Mix(_state);
            return (_state >> 40) * (1f / 16777216f);
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
