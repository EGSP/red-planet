using Godot;

/// <summary>
/// Завод: тянет руду из общего хранилища и кладёт туда метал.
/// Сама переработка идёт в FactorySystem — здесь только параметры и состояние.
/// </summary>
public partial class Factory : Building
{
    /// <summary>Сколько руды потребляет в секунду.</summary>
    [Export] public float OrePerSecond = 2f;

    /// <summary>Сколько метала даёт из единицы руды.</summary>
    [Export] public float MetalPerOre = 0.5f;

    public bool Working { get; set; }

    public override void Init(int id, BuildableDef def, Vector2I cell)
    {
        base.Init(id, def, cell);
        AddToGroup("factory");
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        base._Draw();

        if (Def == null)
            return;

        var size = new Vector2(Def.Size.X, Def.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        var color = Working ? new Color(0.4f, 1f, 0.5f) : new Color(0.5f, 0.5f, 0.5f);
        DrawCircle(new Vector2(rect.End.X - 10f, rect.Position.Y + 10f), 5f, color);
    }
}
