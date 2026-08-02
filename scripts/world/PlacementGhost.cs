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
///
/// Если выбранная постройка со стволом, у каждого места рисуется ещё и круг атаки:
/// иначе нельзя оценить перекрытие с уже стоящими турелями (их круги включает
/// <see cref="GizmoGate.ShowArmedCoverage"/>).
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

        var weapon = Definition.Weapon;

        foreach (var spot in Spots)
        {
            var kind = spot.Valid ? VizKind.PlacementValid : VizKind.PlacementInvalid;
            var area = Placement.Footprint(Definition, spot.Center, spot.Facing);

            ShapeDraw.Obb(this, area, DrawTheme.Fill(kind, 0.22f));
            ShapeDraw.Obb(this, area, DrawTheme.Outline(kind, 2f, WidthMode.Screen));
            ShapeDraw.Obb(this, area.Grow(Const.BuildMarginPx),
                DrawTheme.Outline(kind, 1.5f, WidthMode.MinScreen, 0.35f));

            if (weapon == null)
                continue;

            // Круг атаки будущего ствола: локально «вперёд» совпадает с углом постановки
            DrawSetTransform(spot.Center, spot.Facing, Vector2.One);
            WeaponGizmo.Draw(this, weapon);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }
}
