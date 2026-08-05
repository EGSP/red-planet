using Godot;

/// <summary>
/// Запись журнала препятствий: ревизия, форма и направление изменения.
/// Форма копируется значением, поэтому запись остаётся пригодной после освобождения ноды.
/// </summary>
public readonly struct ObstacleChange
{
    public readonly int Revision;
    public readonly Obb Shape;
    public readonly bool Added;

    public ObstacleChange(int revision, in Obb shape, bool added)
    {
        Revision = revision;
        Shape = shape;
        Added = added;
    }

    public Rect2 Bounds => Shape.Bounds;
}
