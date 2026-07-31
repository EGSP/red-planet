using Godot;

/// <summary>
/// Сессия — корень одной игры. Держит три ветки с разной ответственностью и разным
/// направлением зависимостей:
///
///   Systems     — GameManager и его системы: журнал, проекции, индекс, сетка, порядок кадра.
///   Playground  — мир: всё видимое и физическое, разложенное по слоям отрисовки.
///   View        — наблюдатели: камера и интерфейс. Читают всё, не читает их никто.
///
/// Порядок веток в дереве значим. Systems стоит первым, чтобы планировщик и разрезы
/// собрались раньше, чем проснётся хоть один объект мира; View — последним, потому что
/// ему нужно готовое состояние. Сам же Session просыпается после всех своих детей,
/// и потому стартовая раздача мира происходит здесь и в самом конце.
/// </summary>
public partial class Session : Node2D
{
    [Export] public GameManager Systems;
    [Export] public Playground Playground;
    [Export] public PackedScene CommanderScene;

    /// <summary>
    /// Отладочная выдача на старте: с ней можно смотреть поздние стадии, не выкапывая
    /// руду вручную. Публикуется документом, а не правкой проекции — запас меняется
    /// только через журнал, и у выдачи есть след.
    /// </summary>
    [Export] public float StartingMetal;

    [Export] public float StartingEnergy;

    public override void _Ready()
    {
        Systems ??= GetNodeOrNull<GameManager>("Systems");
        Playground ??= GetNodeOrNull<Playground>("Playground");

        if (Systems == null || Playground == null)
        {
            GD.PushError("[Session] в сцене сессии не хватает Systems или Playground");
            return;
        }

        SpawnBase();
        SpawnCommander();
        GrantStartingResources();
    }

    private void GrantStartingResources()
    {
        var events = Systems.Events;

        if (StartingMetal > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Metal, Amount = StartingMetal });

        if (StartingEnergy > 0f)
            events.Append(new ResourceGained { Kind = ResourceKind.Energy, Amount = StartingEnergy });
    }

    private void SpawnBase()
    {
        var def = Systems.Catalog.Buildable("base");
        if (def == null)
        {
            GD.PushWarning("[Session] нет справочника постройки base");
            return;
        }

        // 3x3 с центром в (0,0) начинается в клетке (-1,-1)
        var origin = new Vector2I(-(def.Width / 2), -(def.Height / 2));

        Systems.Spawn.SpawnBuilding(def, origin);
    }

    private void SpawnCommander()
    {
        if (CommanderScene == null)
        {
            GD.PushWarning("[Session] не задана сцена коммандера");
            return;
        }

        Systems.Spawn.SpawnUnit(CommanderScene, Const.CellCenter(new Vector2I(3, 0)));
    }
}
