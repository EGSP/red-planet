using System.Collections.Generic;
using Godot;

/// <summary>
/// Создание сущностей в мире: выдать EntityId, поднять сцену, инициализировать, занять
/// место на карте препятствий, положить в реестр по id и в индекс.
///
/// Зачем вынесено сюда: раньше эти шаги дословно повторялись в пяти местах —
/// достройка каркаса, постановка каркаса, спавн руды, стартовая база и коммандер.
/// Правило «кто занимает место и кто попадает в реестр» должно жить в одном месте,
/// иначе рассинхрон неизбежен: где-то забыли занять место, где-то не зарегистрировали id.
///
/// Здесь же единственное место, где сущность входит в индекс — и выходит из реестров.
/// Раньше каждая прописывала себя сама, перечисляя строками свои группы, — и списки
/// эти жили в семи разных файлах. Теперь состав разрезов выводится из того, что сущность
/// собой представляет, а сама она про индекс не знает вовсе. Выход из индекса вызова
/// не требует: удалённую ноду он отличает сам. Снятие с карты препятствий и из EntityStore
/// тоже не зовут вручную: подписка на выбытие из индекса делает это по снимку, сделанному
/// при регистрации. Поля ноды в момент уведомления читать нельзя — обёртка обычно уже
/// освобождена движком, поэтому всё нужное для разборки запоминается здесь.
///
/// ПОЧЕМУ КАРТА ПРЕПЯТСТВИЙ ПРАВИТСЯ ЗДЕСЬ, А НЕ ПОДПИСКОЙ НА ИНДЕКС. Состав индекса
/// меняется на границе кадра, а место должно быть занято немедленно: правило постановки
/// спрашивают тут же, в том же кадре, и отложенная запись пустила бы два каркаса на одно
/// место. Снятие, наоборот, откладывать можно и нужно — оно идёт через подписку.
///
/// Почему это НЕ метод каталога: каталог — справочник только для чтения, он отвечает
/// на вопрос «чем является завод». Создание же трогает карту препятствий, реестр
/// и дерево сцены. Смешаешь — и справочник потащит за собой полмира.
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

    /// <summary>
    /// Снимок регистрации: при выбытии из индекса нода уже недоступна, поэтому снятие
    /// с реестров опирается только на эти данные.
    /// </summary>
    private readonly struct Enrollment
    {
        public readonly int Id;
        public readonly IObstacle Obstacle;

        public Enrollment(int id, IObstacle obstacle)
        {
            Id = id;
            Obstacle = obstacle;
        }
    }

    private readonly Dictionary<object, Enrollment> _enrolled = new(ByReference.Instance);

    public Spawner(GameManager gm)
    {
        _gm = gm;

        // Spawner живёт ровно столько же, сколько индекс: отдельной отписки не нужно.
        // Подписка на Node, а не на конкретный род: запись в словаре есть только у тех,
        // кого Enroll занёс; снаряды сюда не попадают и дают быстрый выход.
        gm.Index.Watch<Node>(null, OnRetired);
    }

    /// <summary>
    /// Готовая постройка: занимает место по форме справочника, повёрнутое на заданный угол.
    /// Угол не задан — берётся из справочника: так появляются постройки, которых никто
    /// не ставил, вроде стартовой базы.
    /// </summary>
    public Building SpawnBuilding(UnitDefinition def, Vector2 center, float? facing = null)
    {
        var building = NewBuilding(def.Class);

        int id = _gm.NewId();
        building.Init(id, def, center, facing ?? Mathf.DegToRad(def.FacingDegrees));

        _gm.Playground.Add(WorldLayer.Structures, building);
        Occupy(building, def);
        _gm.Entities.Add(id, building);
        _gm.Index.Add(building);
        Enroll(building, id, def.Occupies ? building : null);

        // Факт «постройка есть в мире» публикуем здесь, а не в каркасе: стартовая база
        // появляется без всякой стройки, а ёмкость хранилища она поднимать обязана
        _gm.Events.Append(new BuildingSpawned
        {
            EntityId = id,
            DefinitionId = def.Id,
            Pos = center,
            Facing = building.BodyFacing,
        });

        return building;
    }

    /// <summary>Юнит ходит по миру и места не занимает.</summary>
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
        Enroll(unit, id, null);

        return unit;
    }

    /// <summary>Каркас занимает место на время стройки — его нельзя перекрыть.</summary>
    public Blueprint SpawnBlueprint(PackedScene scene, UnitDefinition def, Vector2 center,
        float facing)
    {
        var blueprint = scene.Instantiate<Blueprint>();

        int id = _gm.NewId();
        blueprint.Init(id, def, center, facing);

        _gm.Playground.Add(WorldLayer.Structures, blueprint);
        Occupy(blueprint, def);
        _gm.Entities.Add(id, blueprint);
        _gm.Index.Add(blueprint);
        Enroll(blueprint, id, def.Occupies ? blueprint : null);

        return blueprint;
    }

    /// <summary>Враг ходит по миру и места не занимает — как и юнит игрока.</summary>
    public Enemy SpawnEnemy(UnitDefinition def, Vector2 position)
    {
        var enemy = new Enemy();

        int id = _gm.NewId();
        enemy.Init(id, def, position);

        _gm.Playground.Add(WorldLayer.Actors, enemy);
        _gm.Entities.Add(id, enemy);
        _gm.Index.Add(enemy);
        Enroll(enemy, id, null);

        return enemy;
    }

    /// <summary>
    /// Занять место, если сущность его вообще занимает. Форма пуста у того, что стоит
    /// на карте, ничему не мешая, — сейчас таких нет, но признак существует в справочнике,
    /// и решать за него здесь нельзя.
    /// </summary>
    private void Occupy(IObstacle obstacle, UnitDefinition def)
    {
        if (def.Occupies)
            _gm.Obstacles.Add(obstacle);
    }

    /// <summary>
    /// Снаряд. Единственная сущность мира без EntityId: их за бой тысячи, они живут доли
    /// секунды, и ссылаться на снаряд из документа некому — попадание носит id стрелка и цели.
    /// В словарь регистрации не заносится: клеток и EntityStore у него нет.
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
    /// Точка метала. Места она не занимает, потому что занятость означает «здесь кто-то
    /// стоит», а на точке ещё предстоит построить экстрактор. Кого на неё пускать,
    /// решает <see cref="Placement.CanPlace"/>.
    ///
    /// Точка метала — свойство местности и переживает то, что на ней построено. Способа
    /// убрать её с карты пока нет; если появится, правило снятия описывается здесь же.
    /// </summary>
    public MetalSpot SpawnMetalSpot(Vector2 position)
    {
        var spot = new MetalSpot();

        int id = _gm.NewId();
        spot.Init(id, position);

        _gm.Playground.Add(WorldLayer.Deposits, spot);
        _gm.Entities.Add(id, spot);
        _gm.Index.Add(spot);
        Enroll(spot, id, null);

        return spot;
    }

    private void Enroll(Node node, int id, IObstacle obstacle) =>
        _enrolled[node] = new Enrollment(id, obstacle);

    /// <summary>
    /// Выбытие из индекса — единственное место снятия с карты препятствий и EntityStore.
    /// Зовётся в конце кадра: место после гибели остаётся занятым до Sweep, кроме достройки
    /// каркаса, которая освобождает его немедленно. Повторное снятие безвредно.
    /// </summary>
    private void OnRetired(Node node)
    {
        if (!_enrolled.Remove(node, out var enrollment))
            return;

        if (enrollment.Obstacle != null)
            _gm.Obstacles.Remove(enrollment.Obstacle);

        _gm.Entities.Remove(enrollment.Id);
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
