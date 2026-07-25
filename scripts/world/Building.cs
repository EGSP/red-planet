using Godot;

/// <summary>Готовая постройка: занимает клетки по матрице своего справочника.</summary>
public partial class Building : Node2D
{
    public int Id { get; private set; }
    public BuildableDef Def { get; private set; }
    public Vector2I Cell { get; private set; }

    public virtual void Init(int id, BuildableDef def, Vector2I cell)
    {
        Id = id;
        Def = def;
        Cell = cell;
        Position = Const.AreaCenter(cell, def.Size);
        AddToGroup("building");
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Def == null)
            return;

        var size = new Vector2(Def.Size.X, Def.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        DrawRect(rect, Def.Color);
        DrawRect(rect, new Color(0f, 0f, 0f, 0.35f), false, 2f);

        DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X + 4f, rect.Position.Y + 16f),
            Def.DisplayName, HorizontalAlignment.Left, -1, 12, Colors.Black);
    }
}
