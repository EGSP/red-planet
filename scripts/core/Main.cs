using Godot;

/// <summary>Стартовая сборка мира: база в центре и коммандер рядом с ней.</summary>
public partial class Main : Node2D
{
    [Export] public Node2D World;
    [Export] public PackedScene CommanderScene;

    public override void _Ready()
    {
        World ??= GetNodeOrNull<Node2D>("World");

        if (World == null)
        {
            GD.PushError("[Main] не найден узел World");
            return;
        }

        var gm = GameManager.I;
        gm.WorldRoot = World;

        SpawnBase();
        SpawnCommander();
    }

    private void SpawnBase()
    {
        var def = GameManager.I.Catalog.Buildable("base");
        if (def == null)
        {
            GD.PushWarning("[Main] нет справочника постройки base");
            return;
        }

        // 3x3 с центром в (0,0) начинается в клетке (-1,-1)
        var origin = new Vector2I(-(def.Width / 2), -(def.Height / 2));

        GameManager.I.Spawn.SpawnBuilding(def, origin);
    }

    private void SpawnCommander()
    {
        if (CommanderScene == null)
        {
            GD.PushWarning("[Main] не задана сцена коммандера");
            return;
        }

        GameManager.I.Spawn.SpawnUnit(CommanderScene, Const.CellCenter(new Vector2I(3, 0)));
    }
}
