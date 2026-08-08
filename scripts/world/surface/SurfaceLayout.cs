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
/// <see cref="SurfaceDecal.Spacing"/> и смещением внутри клетки даёт нижнюю границу
/// расстояния даром, а вид при достаточном смещении неотличим от свободного разброса.
///
/// РАЗМЕРЫ ПРИВОДЯТСЯ К ПИКСЕЛЯМ ЗДЕСЬ, А НЕ ХРАНЯТСЯ В НИХ. Шаг решётки и сторона
/// отпечатка заданы долей большей стороны области и умножаются на неё при раскладке.
/// Отсюда число клеток решётки от размера арены не зависит, и раскладка одной и той же
/// местности на разных настройках мира отличается только масштабом.
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ ПОТОК СЛУЧАЙНОСТИ НА КАЖДУЮ КЛЕТКУ. Клетка получает числа из своего
/// зерна, выведенного из зерна партии, номера декали и координат клетки. Отсюда два
/// свойства: раскладка не зависит от порядка обхода, и добавление новой декали не
/// сдвигает уже размещённые. Общий на весь проход генератор ни того, ни другого не даёт.
///
/// НОМЕР ДЕКАЛИ УЧАСТВУЕТ ТОЛЬКО В СЛУЧАЙНОСТИ РАЗМЕЩЕНИЯ, НО НЕ В ПОЛЕ ШУМА. Поле есть
/// общее свойство местности: две декали, сославшиеся на один ресурс, обязаны видеть один и
/// тот же рисунок, и он же обязан совпадать с тем, по которому шейдер делит базовый слой.
/// Случайность же размещения принадлежит самой декали, иначе все декали встали бы на одни
/// и те же места. Отсюда разделение: номер подмешан в поток чисел клетки и не подмешан в
/// зерно поля.
///
/// УСЛОВИЯ РАЗМЕЩЕНИЯ ПРОВЕРЯЮТСЯ ОДНИМ ПРАВИЛОМ, А НЕ ПОРОГОМ ДЛЯ ОДНИХ И РАЗМЫТИЕМ ДЛЯ
/// ДРУГИХ. Отрезки шума, температуры и высоты, а также отрезок температуры биома дают доли
/// от нуля до единицы (<see cref="SurfaceSettings.Band"/>), их произведение и есть доля
/// принимаемых кандидатов в этой точке. Прежде плавной была только принадлежность биому, а
/// шум, температура декали и высота проверялись строгим вхождением в отрезок, отчего
/// граница по любому из этих полей выходила отрезанной по линии, хотя по температуре биома
/// в том же месте была рваной. Общее правило снимает расхождение и заодно совпадает с тем,
/// по которому шейдер накладывает покрытия базового слоя: отпечатки ложатся по тем же
/// границам, что и цвет под ними.
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
                // Номер присваивается и пропущенным декалям: из него выводится зерно, и
                // выключение одного набора иначе сдвинуло бы раскладку всех следующих
                index++;

                if (!biome.Enabled || decal == null || !decal.Enabled || decal.Texture == null)
                    continue;

                var group = new SurfaceGroup { Decal = decal };

                Scatter(group, biome, decal, fields, bounds, (ulong)index, settings.Smoothness);

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
        ulong salt,
        float smoothness)
    {
        // Доли отнесены к большей стороне области, как и величины температуры у местности:
        // на неквадратной области отпечатки иначе вытянулись бы вслед за её пропорциями
        float side = Mathf.Max(bounds.Size.X, bounds.Size.Y);

        float step = Mathf.Max(decal.Spacing * side, 16f);
        int cols = Mathf.CeilToInt(bounds.Size.X / step);
        int rows = Mathf.CeilToInt(bounds.Size.Y / step);

        if (cols <= 0 || rows <= 0 || cols * rows > Limit * 4)
            return;

        // Поле шума строится на зерне партии без всякой примеси. Номер декали подмешивался
        // сюда раньше, и следствие было ложным: декаль, сославшаяся на то же поле, что
        // делит базовый слой, ложилась не туда, куда указывает рисунок покрытий, а на
        // сдвинутую копию того же поля. Различать два поля с одинаковыми настройками
        // полагается через SeedOffset самого ресурса, и это единственный способ; номер же
        // декали остаётся при том, для чего он и нужен, — при потоке случайных чисел,
        // который решает, где внутри клетки встанет отпечаток
        var noise = decal.Noise?.Build(fields.Seed);

        // Запас нужен только тогда, когда действует нижняя граница: иначе отвергнутые
        // розыгрышем кандидаты некуда девать, а хранить их — лишняя работа
        bool keepReserve = Limited(decal) && decal.CountMin > 0;

        var taken = new List<Candidate>();
        var reserve = keepReserve ? new List<Candidate>() : null;

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                var random = new Stream(fields.Seed, salt, cx, cy);

                // Розыгрыш доли берётся первым числом потока, а решение по нему
                // откладывается: положение обязано выпасть теми же числами и у кандидата,
                // отвергнутого долей, иначе запас лёг бы не туда, куда лягут принятые
                float roll = random.Next();

                var position = new Vector2(
                    bounds.Position.X + (cx + 0.5f + (random.Next() - 0.5f) * decal.Jitter) * step,
                    bounds.Position.Y + (cy + 0.5f + (random.Next() - 0.5f) * decal.Jitter) * step);

                if (!bounds.HasPoint(position))
                    continue;

                // Все условия сводятся в одну долю: у каждой границы отпечаток появляется
                // тем реже, чем ближе к ней, отчего рваным получается не только стык двух
                // биомов, но и край по шуму, температуре и высоте
                float cover = Cover(biome, decal, noise, fields, position, smoothness);

                if (cover <= 0f)
                    continue;

                float bite = random.Next();

                bool accepted = roll <= decal.Chance && bite <= cover;

                if (!accepted && !keepReserve)
                    continue;

                float size = decal.Size * side *
                    (1f + (random.Next() * 2f - 1f) * decal.SizeVariation);

                // Число берётся всегда, а применяется по флагу. Обращение к потоку под
                // условием сдвигало бы всю дальнейшую последовательность клетки, поэтому
                // снятие галки меняло не только поворот, но и разброс яркости у каждого
                // отпечатка — правка одной настройки отзывалась в другой
                float turn = random.Next();
                float rotation = decal.RandomRotation ? turn * Mathf.Tau : 0f;

                // Разброс яркости трогает только цвет: примешанный к альфе, он менял бы
                // плотность наложения отпечатков друг на друга
                float shade = 1f + (random.Next() * 2f - 1f) * decal.TintVariation;
                var tint = new Color(
                    decal.Tint.R * shade,
                    decal.Tint.G * shade,
                    decal.Tint.B * shade,
                    decal.Tint.A);

                var item = new DecalPlacement(position, rotation, Mathf.Max(size, 1f), tint);
                var candidate = new Candidate(item, Stream.Key(fields.Seed, salt, cx, cy));

                if (accepted)
                    taken.Add(candidate);
                else
                    reserve.Add(candidate);
            }
        }

        Apply(group, decal, taken, reserve);
    }

    /// <summary>
    /// Свести принятых кандидатов к границам количества.
    ///
    /// ПОЧЕМУ ОТБОР ПО КЛЮЧУ, А НЕ ОБРЕЗАНИЕ СПИСКА. Кандидаты набраны обходом по строкам,
    /// поэтому первые в списке лежат вверху карты: обрезание оставило бы отпечатки только
    /// в верхней полосе. Ключ выведен из зерна и координат клетки, значит порядок по нему
    /// не связан с положением на карте, и отбор прореживает область равномерно. Заодно
    /// отбор устойчив: правка верхней границы не переставляет уже отобранное, а лишь
    /// прибавляет или убавляет отпечатки с конца порядка.
    /// </summary>
    private static void Apply(
        SurfaceGroup group,
        SurfaceDecal decal,
        List<Candidate> taken,
        List<Candidate> reserve)
    {
        bool limited = Limited(decal);

        int max = limited && decal.CountMax > 0 ? Mathf.Min(decal.CountMax, Limit) : Limit;
        int min = limited ? Mathf.Min(decal.CountMin, max) : 0;

        if (taken.Count > max)
        {
            taken.Sort(Order);
            taken.RemoveRange(max, taken.Count - max);
        }

        if (reserve != null && taken.Count < min)
        {
            reserve.Sort(Order);

            int need = Mathf.Min(min - taken.Count, reserve.Count);

            for (int i = 0; i < need; i++)
                taken.Add(reserve[i]);
        }

        foreach (var candidate in taken)
            group.Items.Add(candidate.Item);
    }

    private static int Order(Candidate a, Candidate b) => a.Key.CompareTo(b.Key);

    /// <summary>
    /// Действуют ли границы количества. Совпадение нижней и верхней означает, что правило
    /// выключено: одинаковыми числами удобно снять ограничение, не помня, какое из них
    /// нулевое, а требование ровно стольких отпечатков смысла не имеет — раскладка тогда
    /// перестала бы зависеть от условий размещения.
    /// </summary>
    private static bool Limited(SurfaceDecal decal) => decal.CountMin != decal.CountMax;

    /// <summary>
    /// Доля кандидатов, принимаемых в этой точке: произведение принадлежностей всем
    /// условиям размещения. Правило то же, каким шейдер считает степень покрытия пикселя
    /// тайлом, поэтому граница отпечатков совпадает с границей покрытий под ними.
    ///
    /// ПОЛЕ ВЫСОТ ПРОВЕРЯЕТСЯ, ТОЛЬКО ЕСЛИ ОНО ЗАДАНО. Без него отрезок высоты декали
    /// условием не является вовсе, а не считается нарушенным.
    /// </summary>
    private static float Cover(
        SurfaceBiome biome,
        SurfaceDecal decal,
        FastNoiseLite noise,
        SurfaceFields fields,
        Vector2 position,
        float smoothness)
    {
        float temperature = fields.TemperatureAt(position);

        float cover = SurfaceSettings.Band(temperature, biome.TemperatureRange, smoothness)
            * SurfaceSettings.Band(temperature, decal.TemperatureRange, smoothness);

        if (cover <= 0f)
            return 0f;

        if (noise != null)
            cover *= SurfaceSettings.Band(
                decal.Noise.Sample(noise, position), decal.NoiseRange, smoothness);

        if (fields.HasHeight)
            cover *= SurfaceSettings.Band(
                fields.HeightAt(position), decal.HeightRange, smoothness);

        return cover;
    }

    /// <summary>
    /// Отпечаток раскладки: зерно, область и все правила размещения. Меняется — раскладку
    /// пересобирают, не меняется — оставляют как есть.
    /// </summary>
    public static int SignatureOf(SurfaceSettings settings, SurfaceFields fields)
    {
        if (settings == null || fields == null)
            return 0;

        // Ширина перехода участвует в отборе кандидатов, поэтому её правка обязана
        // пересобрать раскладку так же, как правка любого отрезка
        int hash = HashCode.Combine(fields.Signature, settings.Smoothness);

        if (settings.Biomes == null)
            return hash;

        foreach (var biome in settings.Biomes)
        {
            if (biome == null)
                continue;

            hash = HashCode.Combine(hash, biome.Enabled, biome.TemperatureRange);

            if (biome.Decals == null)
                continue;

            foreach (var decal in biome.Decals)
            {
                if (decal == null)
                    continue;

                hash = HashCode.Combine(hash,
                    decal.Texture?.GetInstanceId() ?? 0UL,
                    HashCode.Combine(decal.Layer, decal.Spacing, decal.Chance, decal.Jitter,
                        decal.CountMin, decal.CountMax),
                    HashCode.Combine(decal.Enabled, decal.Size, decal.SizeVariation,
                        decal.RandomRotation),
                    HashCode.Combine(decal.NoiseRange, decal.TemperatureRange, decal.HeightRange),
                    HashCode.Combine(decal.Tint, decal.TintVariation),
                    SurfaceFields.SignatureOf(decal.Noise));
            }
        }

        return hash;
    }

    /// <summary>
    /// Кандидат вместе с ключом отбора: по ключу решается, кого убрать при избытке и кого
    /// добрать при недостаче.
    /// </summary>
    private readonly struct Candidate
    {
        public readonly DecalPlacement Item;
        public readonly float Key;

        public Candidate(DecalPlacement item, float key)
        {
            Item = item;
            Key = key;
        }
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

        /// <summary>
        /// Ключ отбора клетки. Берётся отдельным потоком, а не очередным числом основного:
        /// вставка лишнего вызова сдвинула бы всю последовательность клетки, и раскладка
        /// уже подобранных местностей изменилась бы при неизменных настройках.
        /// </summary>
        public static float Key(ulong seed, ulong salt, int cx, int cy) =>
            new Stream(seed, salt ^ 0xA5A55A5A12349E37UL, cx, cy).Next();

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
