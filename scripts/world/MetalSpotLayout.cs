using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Раскладка точек метала: где они лежат при этих настройках и этом сиде.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ <see cref="MetalSpotSystem"/>. Раскладку нужно уметь показать
/// в редакторе, где ни мира, ни <c>GameManager</c> не существует. Оставь подсчёт внутри
/// системы, предпросмотр показывал бы похожую раскладку, а не ту же самую, — и доверять
/// ему было бы нельзя. Здесь же считает один и тот же код: система передаёт настоящую
/// проверку занятости, стенд — приблизительную.
///
/// Единственная разница между ними в том и состоит: система знает про стартовую базу
/// и растр препятствий, а стенд про них не знает и потому проверяет одни лишь границы.
/// Расхождение возможно только вблизи базы, и там оно очевидно глазом.
/// </summary>
public static class MetalSpotLayout
{
    /// <summary>
    /// Разложить точки. <paramref name="free"/> отвечает на вопрос, свободно ли место
    /// под экстрактор в этой позиции; расстояние между точками и границы мира проверяются
    /// здесь и вызывающего не касаются.
    /// </summary>
    public static List<Vector2> Build(WorldSettings settings, ulong seed, Func<Vector2, bool> free)
    {
        var placed = new List<Vector2>();
        var rng = new RandomNumberGenerator();

        if (seed != 0)
            rng.Seed = seed;
        else
            rng.Randomize();

        // Сначала ближние точки, потом все остальные. Порядок именно такой, потому что
        // ближние — гарантия, а не пожелание: раскладка, оставившая начало без метала,
        // проиграна до первого решения игрока
        for (int i = 0; i < settings.StartCount; i++)
            TryPlace(settings, rng, placed, free,
                settings.BaseClearanceCells, settings.StartRadiusCells);

        while (placed.Count < settings.SpotCount)
            if (!TryPlace(settings, rng, placed, free,
                    settings.BaseClearanceCells, settings.FieldRadiusCells))
                break;

        return placed;
    }

    /// <summary>Свободно ли место под экстрактор с точки зрения одних лишь границ мира.</summary>
    public static bool InsideWorld(Vector2 position)
    {
        float half = Const.Unit * 0.5f;
        var area = new Rect2(position - new Vector2(half, half), Const.Unit, Const.Unit);

        return World.Bounds.Encloses(area);
    }

    private static bool TryPlace(WorldSettings settings, RandomNumberGenerator rng,
        List<Vector2> placed, Func<Vector2, bool> free, int minRadius, int maxRadius)
    {
        int attempts = Mathf.Max(settings.Attempts, 1);
        float spacing = settings.SpacingCells * Const.Unit;
        float spacingSquared = spacing * spacing;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            var position = RandomPoint(rng, minRadius, maxRadius);

            if (!free(position) || TooClose(placed, position, spacingSquared))
                continue;

            placed.Add(position);
            return true;
        }

        return false;
    }

    private static bool TooClose(List<Vector2> placed, Vector2 position, float spacingSquared)
    {
        foreach (var other in placed)
            if (other.DistanceSquaredTo(position) < spacingSquared)
                return true;

        return false;
    }

    /// <summary>
    /// Случайная точка кольца. Радиус берётся через корень намеренно: площадь кольца растёт
    /// вместе с радиусом, и равномерный выбор радиуса сгущал бы точки к центру — как раз туда,
    /// где они нужны меньше всего.
    ///
    /// Точка садится в центр клетки, хотя постановка давно свободна от сетки. Причина
    /// не в правилах, а в опрятности: экстрактор притягивается к точке, и раскладка,
    /// выровненная по клеткам, читается глазом как порядок, а не как рассыпанная горсть.
    /// </summary>
    private static Vector2 RandomPoint(RandomNumberGenerator rng, int minRadius, int maxRadius)
    {
        float angle = rng.RandfRange(0f, Mathf.Tau);
        float squared = rng.RandfRange(minRadius * minRadius, maxRadius * maxRadius);
        float radius = Mathf.Sqrt(squared);

        var cell = new Vector2I(
            Mathf.RoundToInt(Mathf.Cos(angle) * radius),
            Mathf.RoundToInt(Mathf.Sin(angle) * radius));

        return Const.CellCenter(cell);
    }
}
