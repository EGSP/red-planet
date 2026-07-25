using Godot;

/// <summary>Коммандер игрока: единственный, кто слушает приказы.</summary>
public partial class Commander : Unit
{
    public override void _Ready()
    {
        base._Ready();
        AddToGroup("commander");
        GameManager.I.Commander = this;
    }

    public override void _Draw()
    {
        base._Draw();

        // Радиус инструмента
        if (Def != null)
            DrawArc(Vector2.Zero, Def.ToolRange * Const.Unit, 0f, Mathf.Tau, 48,
                new Color(0.5f, 0.8f, 1f, 0.18f), 1.5f);
    }
}
