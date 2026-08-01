using System.Collections.Generic;
using Godot;

/// <summary>
/// Призрак будущей застройки: зелёный — можно ставить, красный — нельзя.
///
/// Рисует не одно место, а весь план: протаскивание с выбранной постройкой раскладывает
/// целую партию, и показать её игрок должен до того, как отпустит кнопку. Негодные места
/// остаются в плане красными — при постановке они пропускаются, и молчаливое исчезновение
/// их из показа скрывало бы, почему построилось меньше, чем размечено.
///
/// У каждого места два контура. Сплошной — само место, которое займёт постройка. Пунктирный —
/// обязательный зазор вокруг него: игрок должен видеть не только габарит, но и то,
/// почему постановка вплотную к соседу отклоняется.
/// </summary>
public partial class PlacementGhost : Node2D
{
    public UnitDefinition Definition;

    /// <summary>
    /// План застройки. Список принадлежит CommandSystem и подставляется сюда ссылкой:
    /// призрак обязан рисовать ровно то, что будет поставлено, а копия рано или поздно
    /// разошлась бы с подлинником.
    /// </summary>
    public List<BuildSpot> Spots = new();

    public override void _Draw()
    {
        if (Definition == null)
            return;

        foreach (var spot in Spots)
        {
            var color = spot.Valid ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
            var area = Placement.Footprint(Definition, spot.Center, spot.Facing);

            Outline(area, new Color(color, 0.22f), true);
            Outline(area, color, false, 2f);
            Outline(area.Grow(Const.BuildMarginPx), new Color(color, 0.35f), false, 1f);
        }
    }

    /// <summary>
    /// Повёрнутый прямоугольник в координатах ноды. Заливка и контур рисуются разными
    /// вызовами движка, поэтому способ рисования выбирается признаком.
    /// </summary>
    private void Outline(in Obb area, Color color, bool filled, float width = 1f)
    {
        var corners = area.Corners();
        var local = new Vector2[corners.Length];

        for (int i = 0; i < corners.Length; i++)
            local[i] = corners[i] - GlobalPosition;

        if (filled)
        {
            DrawColoredPolygon(local, color);
            return;
        }

        DrawPolyline(new[] { local[0], local[1], local[2], local[3], local[0] }, color, width);
    }
}
