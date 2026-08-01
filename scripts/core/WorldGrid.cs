using System.Collections.Generic;
using Godot;

/// <summary>
/// Сетка застройки: постройки привязаны к клеткам, а юниты ходят в непрерывном пространстве.
///
/// Клетка описывается двумя независимыми признаками, и смешивать их нельзя. ЗАНЯТОСТЬ —
/// временное свойство: кто на клетке стоит сейчас. ТОЧКА МЕТАЛА — свойство самой местности:
/// она не занимает клетку, не мешает пройти и не исчезает вместе с тем, что на ней построили.
///
/// Раньше месторождение занимало клетку наравне с постройкой, и это было верно, пока его
/// выкапывали: занятость служила запретом строить поверх. Теперь точку требуется застроить
/// экстрактором, поэтому запрет переехал в правило постановки, а сама точка стала признаком
/// местности.
/// </summary>
public sealed class WorldGrid
{
    private readonly Dictionary<Vector2I, int> _occupied = new();

    /// <summary>Клетки с точками метала. Ставятся генерацией мира и больше не меняются.</summary>
    private readonly HashSet<Vector2I> _metal = new();

    public bool InBounds(Vector2I cell) =>
        Mathf.Abs(cell.X) <= Const.WorldRadiusCells && Mathf.Abs(cell.Y) <= Const.WorldRadiusCells;

    public bool IsOccupied(Vector2I cell) => _occupied.ContainsKey(cell);

    public int OwnerOf(Vector2I cell) => _occupied.TryGetValue(cell, out var id) ? id : 0;

    public bool HasMetal(Vector2I cell) => _metal.Contains(cell);

    public void MarkMetal(Vector2I cell) => _metal.Add(cell);

    /// <summary>
    /// Можно ли поставить сущность началом в origin. Проверок три, и последняя работает
    /// в обе стороны: экстрактор обязан встать на точку метала, а всё остальное обязано
    /// точку не перекрывать.
    ///
    /// Второе требование существеннее, чем кажется. Точек на карте конечное число, они
    /// не восстанавливаются, и застроенная генератором точка потеряна до конца партии.
    /// Такую потерю игрок должен получить отказом при постановке, а не через полчаса,
    /// обнаружив, что ставить экстрактор некуда.
    /// </summary>
    public bool CanPlace(Vector2I origin, UnitDefinition def)
    {
        foreach (var cell in def.Cells(origin))
        {
            if (!InBounds(cell) || IsOccupied(cell))
                return false;

            if (HasMetal(cell) != def.RequiresMetalSpot)
                return false;
        }

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
}
