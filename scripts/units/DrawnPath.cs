using System.Collections.Generic;
using Godot;

/// <summary>
/// Нарисованная линия приказа и распределение исполнителей вдоль неё.
///
/// ЧТО ТАКОЕ ЛИНИЯ. Игрок ведёт указателем, а система записывает его положение с шагом
/// не мельче <see cref="Step"/>; получается ломаная, повторяющая жест целиком, включая
/// петли и самопересечения. Упрощать её не следует: игрок нарисовал именно эту форму,
/// и спрямление означало бы, что отряд встанет не туда, куда его поставили.
///
/// КАК РАСПРЕДЕЛЯЮТСЯ ИСПОЛНИТЕЛИ. Линия делится на равные отрезки по длине дуги, а не
/// по числу записанных точек: точки ложатся тем гуще, чем медленнее игрок вёл указатель,
/// и деление по ним растянуло бы строй в местах спешки и сгустило бы там, где рука
/// задержалась. Крайние места достаются началу и концу линии, поэтому двое встают по концам,
/// трое — по концам и в середине.
/// </summary>
public static class DrawnPath
{
    /// <summary>Наименьшее расстояние между записываемыми точками, пикселей.</summary>
    public const float Step = 6f;

    /// <summary>Длина ломаной по сумме её звеньев.</summary>
    public static float Length(IReadOnlyList<Vector2> path)
    {
        if (path == null || path.Count < 2)
            return 0f;

        float total = 0f;

        for (int i = 1; i < path.Count; i++)
            total += path[i - 1].DistanceTo(path[i]);

        return total;
    }

    /// <summary>
    /// Записать точку в линию, если указатель отошёл от предыдущей дальше шага. Возвращает
    /// признак того, что линия изменилась, — по нему решает отрисовка, нужен ли перерисовку
    /// запрашивать.
    /// </summary>
    public static bool Append(List<Vector2> path, Vector2 point)
    {
        if (path.Count > 0 && path[^1].DistanceTo(point) < Step)
            return false;

        path.Add(point);
        return true;
    }

    /// <summary>
    /// Места для <paramref name="count"/> исполнителей вдоль линии, от её начала к концу.
    ///
    /// Единственный исполнитель встаёт в конец линии: рисование для него — не построение
    /// строя, а указание точки назначения, и точка эта та, где игрок отпустил кнопку.
    ///
    /// Вырожденная линия (все точки в одном месте) даёт всем одно и то же место: разводить
    /// их друг от друга здесь не нужно — этим занят локальный слой движения.
    /// </summary>
    public static void Spread(IReadOnlyList<Vector2> path, int count, List<Vector2> into)
    {
        into.Clear();

        if (path == null || path.Count == 0 || count <= 0)
            return;

        if (count == 1 || path.Count == 1)
        {
            for (int i = 0; i < count; i++)
                into.Add(path[^1]);

            return;
        }

        float total = Length(path);

        if (total <= 0.001f)
        {
            for (int i = 0; i < count; i++)
                into.Add(path[0]);

            return;
        }

        float step = total / (count - 1);

        // Идём по звеньям один раз на всё распределение: места запрашиваются по возрастанию
        // длины дуги, и возвращаться к пройденным звеньям не приходится
        int link = 1;
        float passed = 0f;
        float linkLength = path[0].DistanceTo(path[1]);

        for (int i = 0; i < count; i++)
        {
            float wanted = step * i;

            while (link < path.Count - 1 && passed + linkLength < wanted)
            {
                passed += linkLength;
                link++;
                linkLength = path[link - 1].DistanceTo(path[link]);
            }

            float offset = linkLength > 0.001f
                ? Mathf.Clamp((wanted - passed) / linkLength, 0f, 1f)
                : 0f;

            into.Add(path[link - 1].Lerp(path[link], offset));
        }
    }
}
