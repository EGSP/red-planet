using Godot;

/// <summary>
/// Круги инструментов сущностей: обзор, дальность ствола с рёбрами сектора, рабочая рука.
/// Показываются они матрицей отладки, зажатым Ctrl при непустом выделении и постановкой
/// вооружённой постройки — решает это <see cref="GizmoGate"/>, а здесь идёт только обход
/// и объявление фигур.
///
/// ЧЕМ ЭТО БЫЛО РАНЬШЕ. Круги рисовала себе каждая сущность сама, в собственном <c>_Draw</c>,
/// разбиением окружности на полсотни отрезков. При зажатом Ctrl над отрядом это давало
/// по несколько тысяч вершин и по списку команд отрисовки на каждую машину. Здесь круги
/// лежат в общей множественной сетке мира — см. <see cref="ShapeMesh"/>.
///
/// ПОЧЕМУ СИСТЕМОЙ. Объявление фигур кадровое, а ноды перерисовываются по надобности:
/// сущность, простоявшая кадр без изменений, показывала бы круг с разрывами. Обход разреза
/// индекса от порядка перерисовки нод не зависит.
/// </summary>
public partial class UnitGizmoOverlay : GameSystem
{
    /// <summary>
    /// Слой показа. Тот же, что у очередей приказов: круг инструмента есть такая же
    /// подсказка и закрывать собой сущности не должен.
    /// </summary>
    private const WorldLayer Layer = WorldLayer.GroundEffects;

    public UnitGizmoOverlay()
    {
        // Показ, а не изменение мира: круги объявляются после перемещений и наведения
        // этого кадра, иначе они отстают от корпусов
        Phase = Phase.React;
        UpdateCycle = UpdateCycle.Process;
    }

    public override void Step(double dt)
    {
        // Условия показа обновляет CommandSystem: только он знает выделение и постройку,
        // выбранную для постановки
        foreach (var entity in GM.Index.All<IToolGizmo>())
        {
            // Показ читается полем сущности, а не IsVisibleInTree: последний обходит
            // цепочку родителей у движка, тогда как сущности лежат прямо в слоях площадки,
            // и слои не гасятся — значит собственный показ и есть показ в дереве
            if (entity is not Entity node || !Alive.Is(node) || !node.Visible)
                continue;

            UnitGizmos.Put(entity.GizmoTools, entity.Faction, entity.GlobalPosition,
                entity.ToolFacing, Layer,
                selected: GizmoGate.IsSelected(entity),
                armedStructure: entity.ArmedStructure);
        }
    }
}
