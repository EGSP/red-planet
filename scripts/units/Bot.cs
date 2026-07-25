using Godot;

/// <summary>
/// Бот действует сам: приказы игрока не слушает.
/// Роль определяется числами в справочнике — MinePower и BuildPower.
/// </summary>
public partial class Bot : Unit
{
    public override void _Ready()
    {
        base._Ready();
        AddToGroup("bot");
    }
}
