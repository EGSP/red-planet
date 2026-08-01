using System.Collections.Generic;
using Godot;

/// <summary>
/// Сетка занятости: застройка привязана к клеткам, а юниты ходят в непрерывном пространстве.
/// </summary>
public sealed class WorldGrid
{
    private readonly Dictionary<Vector2I, int> _occupied = new();

    public bool InBounds(Vector2I cell) =>
        Mathf.Abs(cell.X) <= Const.WorldRadiusCells && Mathf.Abs(cell.Y) <= Const.WorldRadiusCells;

    public bool IsOccupied(Vector2I cell) => _occupied.ContainsKey(cell);

    public int OwnerOf(Vector2I cell) => _occupied.TryGetValue(cell, out var id) ? id : 0;

    /// <summary>Свободно ли место под матрицу постройки с началом в origin.</summary>
    public bool IsFree(Vector2I origin, UnitDefinition def)
    {
        foreach (var cell in def.Cells(origin))
            if (!InBounds(cell) || IsOccupied(cell))
                return false;
        return true;
    }

    public void Occupy(Vector2I origin, UnitDefinition def, int entityId)
    {
        foreach (var cell in def.Cells(origin))
            _occupied[cell] = entityId;
    }

    public void Free(Vector2I origin, UnitDefinition def)
    {
        foreach (var cell in def.Cells(origin))
            _occupied.Remove(cell);
    }

    public void Occupy(Vector2I cell, int entityId) => _occupied[cell] = entityId;

    public void Free(Vector2I cell) => _occupied.Remove(cell);
}
