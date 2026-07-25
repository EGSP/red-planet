using Godot;

/// <summary>
/// Создание сущностей в мире — единый обряд на всех: выдать EntityId, поднять сцену,
/// инициализировать, занять клетки сетки, положить в реестр сущностей.
///
/// Зачем вынесено сюда: раньше эти четыре шага дословно повторялись в пяти местах —
/// достройка каркаса, постановка каркаса, спавн руды, стартовая база и коммандер.
/// Правило «кто занимает сетку и кто попадает в реестр» должно жить в одном месте,
/// иначе рассинхрон неизбежен: где-то забыли занять клетки, где-то не зарегистрировали id.
///
/// Почему это НЕ метод каталога: каталог — справочник только для чтения, он отвечает
/// на вопрос «чем является завод». Создание же трогает сетку, реестр и дерево сцены.
/// Смешаешь — и справочник потащит за собой полмира.
///
/// Порядок внутри важен: id и позиция выставляются ДО добавления в дерево.
/// Иначе нода успевает попасть в группу и получить работу с ещё нулевым Id,
/// а подключение к узлу работы запишется не на того исполнителя.
/// </summary>
public sealed class Spawner
{
    private readonly GameManager _gm;

    public Spawner(GameManager gm) => _gm = gm;

    /// <summary>Готовая постройка: занимает клетки по матрице справочника.</summary>
    public Building SpawnBuilding(BuildableDef def, Vector2I cell)
    {
        var building = def.Scene != null
            ? def.Scene.Instantiate<Building>()
            : new Building();

        int id = _gm.NewId();
        building.Init(id, def, cell);

        _gm.WorldRoot.AddChild(building);
        _gm.Grid.Occupy(cell, def, id);
        _gm.Entities.Add(id, building);

        return building;
    }

    /// <summary>Юнит ходит по миру и клеток не занимает.</summary>
    public Unit SpawnUnit(PackedScene scene, Vector2 position)
    {
        var unit = scene.Instantiate<Unit>();

        int id = _gm.NewId();
        unit.Id = id;
        unit.Position = position;

        _gm.WorldRoot.AddChild(unit);
        _gm.Entities.Add(id, unit);

        return unit;
    }

    /// <summary>Каркас занимает клетки на время стройки — место нельзя перекрыть.</summary>
    public Blueprint SpawnBlueprint(PackedScene scene, BuildableDef def, Vector2I cell)
    {
        var blueprint = scene.Instantiate<Blueprint>();

        int id = _gm.NewId();
        blueprint.Init(id, def, cell);

        _gm.WorldRoot.AddChild(blueprint);
        _gm.Grid.Occupy(cell, def, id);
        _gm.Entities.Add(id, blueprint);

        return blueprint;
    }

    /// <summary>Месторождение занимает свою клетку, пока не выработано.</summary>
    public OreDeposit SpawnOre(PackedScene scene, Vector2I cell)
    {
        var deposit = scene.Instantiate<OreDeposit>();

        int id = _gm.NewId();
        deposit.Init(id, cell);

        _gm.WorldRoot.AddChild(deposit);
        _gm.Grid.Occupy(cell, id);
        _gm.Entities.Add(id, deposit);

        return deposit;
    }
}
