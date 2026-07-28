using Godot;

/// <summary>Стартовая сборка мира: база в центре и коммандер рядом с ней.</summary>
public partial class Main : Node2D
{
    [Export] public Node2D World;
    [Export] public PackedScene CommanderScene;

    /// <summary>
    /// Отладочная выдача на старте: с ней можно смотреть поздние стадии, не выкапывая
    /// руду вручную. Публикуется документом, а не правкой проекции — запас меняется
    /// только через журнал, и у выдачи есть след.
    /// </summary>
    [Export] public float StartingOre;

    [Export] public float StartingMetal;

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
        GrantStartingResources();
    }

    private void GrantStartingResources()
    {
        var events = GameManager.I.Events;

        if (StartingOre > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Ore, Amount = StartingOre });

        if (StartingMetal > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Metal, Amount = StartingMetal });
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
