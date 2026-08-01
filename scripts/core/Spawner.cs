using Godot;

/// <summary>
/// Создание сущностей в мире — единый обряд на всех: выдать EntityId, поднять сцену,
/// инициализировать, занять клетки сетки, положить в реестр по id и в индекс.
///
/// Зачем вынесено сюда: раньше эти шаги дословно повторялись в пяти местах —
/// достройка каркаса, постановка каркаса, спавн руды, стартовая база и коммандер.
/// Правило «кто занимает сетку и кто попадает в реестр» должно жить в одном месте,
/// иначе рассинхрон неизбежен: где-то забыли занять клетки, где-то не зарегистрировали id.
///
/// Здесь же единственное место, где сущность входит в индекс. Раньше каждая прописывала
/// себя сама, перечисляя строками свои группы, — и списки эти жили в семи разных файлах.
/// Теперь состав разрезов выводится из того, что сущность собой представляет, а сама она
/// про индекс не знает вовсе. Выход из индекса вызова не требует: удалённую ноду он
/// отличает сам, и забыть снять сущность физически нельзя.
///
/// Почему это НЕ метод каталога: каталог — справочник только для чтения, он отвечает
/// на вопрос «чем является завод». Создание же трогает сетку, реестр и дерево сцены.
/// Смешаешь — и справочник потащит за собой полмира.
///
/// Порядок внутри важен: id и позиция выставляются ДО добавления в дерево.
/// Иначе нода успевает получить работу с ещё нулевым Id, а подключение к узлу работы
/// запишется не на того исполнителя.
///
/// Слой мира выбирается здесь же и по той же причине: род сущности известен ровно тут.
/// Порядок отрисовки перестал быть россыпью ZIndex по коду и стал следствием структуры —
/// снаряд летит поверх юнита не потому, что кто-то однажды выписал ему шестёрку.
/// </summary>
public sealed class Spawner
{
    private readonly GameManager _gm;

    public Spawner(GameManager gm) => _gm = gm;

    /// <summary>Готовая постройка: занимает клетки по матрице справочника.</summary>
    public Building SpawnBuilding(UnitDefinition def, Vector2I cell)
    {
        var building = NewBuilding(def.Class);

        int id = _gm.NewId();
        building.Init(id, def, cell);

        _gm.Playground.Add(WorldLayer.Structures, building);
        _gm.Grid.Occupy(cell, def, id);
        _gm.Entities.Add(id, building);
        _gm.Index.Add(building);

        // Факт «постройка есть в мире» публикуем здесь, а не в каркасе: стартовая база
        // появляется без всякой стройки, а ёмкость хранилища она поднимать обязана
        _gm.Events.Append(new BuildingSpawned
        {
            EntityId = id,
            DefinitionId = def.Id,
            Cell = cell,
        });

        return building;
    }

    /// <summary>Юнит ходит по миру и клеток не занимает.</summary>
    public Unit SpawnUnit(UnitDefinition def, Vector2 position)
    {
        var unit = NewUnit(def.Class);

        int id = _gm.NewId();
        unit.Id = id;
        unit.Definition = def;
        unit.Position = position;

        _gm.Playground.Add(WorldLayer.Actors, unit);
        _gm.Entities.Add(id, unit);
        _gm.Index.Add(unit);

        return unit;
    }

    /// <summary>Каркас занимает клетки на время стройки — место нельзя перекрыть.</summary>
    public Blueprint SpawnBlueprint(PackedScene scene, UnitDefinition def, Vector2I cell)
    {
        var blueprint = scene.Instantiate<Blueprint>();

        int id = _gm.NewId();
        blueprint.Init(id, def, cell);

        _gm.Playground.Add(WorldLayer.Structures, blueprint);
        _gm.Grid.Occupy(cell, def, id);
        _gm.Entities.Add(id, blueprint);
        _gm.Index.Add(blueprint);

        return blueprint;
    }

    /// <summary>Враг ходит по миру и клеток не занимает — как и юнит игрока.</summary>
    public Enemy SpawnEnemy(UnitDefinition def, Vector2 position)
    {
        var enemy = new Enemy();

        int id = _gm.NewId();
        enemy.Init(id, def, position);

        _gm.Playground.Add(WorldLayer.Actors, enemy);
        _gm.Entities.Add(id, enemy);
        _gm.Index.Add(enemy);

        return enemy;
    }

    /// <summary>
    /// Снаряд. Единственная сущность мира без EntityId: их за бой тысячи, они живут доли
    /// секунды, и ссылаться на снаряд из документа некому — попадание носит id стрелка и цели.
    /// </summary>
    public Projectile SpawnProjectile(WeaponDefinition weapon, IArmed shooter, Vector2 from, float angle)
    {
        var direction = Heading.Forward(angle);

        var projectile = new Projectile
        {
            Position = from + direction * weapon.ProjectileRadiusPx * 2f,
            Velocity = direction * weapon.SpeedPx,
            Damage = weapon.Damage,
            Radius = weapon.ProjectileRadiusPx,
            Life = weapon.Lifetime,
            SourceId = shooter.EntityId,
            TargetSide = shooter.Faction.Opposite(),
            Tint = weapon.ProjectileColor,
        };

        _gm.Playground.Add(WorldLayer.Projectiles, projectile);
        _gm.Index.Add(projectile);

        return projectile;
    }

    /// <summary>
    /// Точка метала. Клетку она не занимает, а помечает: занятость означает «здесь кто-то
    /// стоит», а на точке ещё предстоит построить экстрактор. Кого на неё пускать,
    /// решает WorldGrid.CanPlace.
    /// </summary>
    public MetalSpot SpawnMetalSpot(Vector2I cell)
    {
        var spot = new MetalSpot();

        int id = _gm.NewId();
        spot.Init(id, cell);

        _gm.Playground.Add(WorldLayer.Deposits, spot);
        _gm.Grid.MarkMetal(cell);
        _gm.Entities.Add(id, spot);
        _gm.Index.Add(spot);

        return spot;
    }

    /// <summary>
    /// Какой класс поднять под определение. Раньше ответ лежал в отдельной сцене на каждый
    /// вид: файл на восемь строк, где всё содержание — имя скрипта и ссылка на справочник.
    /// Теперь вид называет свой класс сам, а перечень проверяется компилятором.
    ///
    /// Сцена вернётся тогда, когда сущности понадобится собственное дерево нод —
    /// спрайты, кости, области. Пока такой сущности нет, и заводить её впрок незачем.
    /// </summary>
    private static Building NewBuilding(UnitClass kind) => kind switch
    {
        UnitClass.Factory => new Factory(),
        UnitClass.Turret => new Turret(),
        UnitClass.Assembler => new Assembler(),
        _ => new Building(),
    };

    private static Unit NewUnit(UnitClass kind) => kind switch
    {
        UnitClass.Commander => new Commander(),
        _ => new Bot(),
    };
}
