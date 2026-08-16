using Godot;

/// <summary>
/// Примитивы подсказок, которые узлы модели рисуют в редакторе.
///
/// ПОЧЕМУ НЕ <see cref="ShapeDraw"/>. Тот набор считает толщину линий от масштаба камеры
/// игры и опирается на <see cref="DrawTheme"/>; здесь же рисование идёт в двумерном
/// редакторе Godot, где масштаб задаёт сам редактор. Общего у этих двух наборов нет
/// ничего, кроме имён фигур, поэтому подсказки вынесены отдельно.
///
/// Размеры заданы в пикселях мира при <see cref="Const.Unit"/> = 64, то есть подсказка
/// занимает заметную, но не закрывающую спрайт долю клетки.
/// </summary>
public static class ModelGizmo
{
    /// <summary>Длина оси «вперёд».</summary>
    public const float AxisLength = 40f;

    /// <summary>Половина размера перекрестия точки вращения.</summary>
    public const float PivotSize = 6f;

    /// <summary>Радиус кружка точки вылета.</summary>
    public const float MuzzleRadius = 4f;

    /// <summary>Перекрестие: отмечает точку, вокруг которой идёт поворот.</summary>
    public static void Cross(CanvasItem canvas, Vector2 center, float size, Color color)
    {
        canvas.DrawLine(center - new Vector2(size, 0f), center + new Vector2(size, 0f), color, 1.5f);
        canvas.DrawLine(center - new Vector2(0f, size), center + new Vector2(0f, size), color, 1.5f);
    }

    /// <summary>Стрелка от точки по направлению угла: показывает, куда смотрит часть.</summary>
    public static void Arrow(CanvasItem canvas, Vector2 from, float angle, float length,
        Color color)
    {
        var direction = Vector2.Right.Rotated(angle);
        var tip = from + direction * length;
        float wing = length * 0.25f;

        canvas.DrawLine(from, tip, color, 1.5f);
        canvas.DrawLine(tip, tip - direction.Rotated(0.4f) * wing, color, 1.5f);
        canvas.DrawLine(tip, tip - direction.Rotated(-0.4f) * wing, color, 1.5f);
    }
}
