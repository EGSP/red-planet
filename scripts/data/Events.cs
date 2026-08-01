using Godot;

/// <summary>
/// Вид ресурса. Названо Kind, потому что слово Resource занято классом Godot.
///
/// Ресурса ровно два, и роли у них разные. Метал — материал: из него состоят постройки,
/// он копится и тратится. Энергия — не материал, а пропускная способность: она ничего
/// не образует, но без неё метал не потратить. Так же устроена экономика PA.
/// </summary>
public enum ResourceKind
{
    Metal,
    Energy,
}

/// <summary>Ресурс поступил в общее хранилище базы.</summary>
[TransientEvent]
public struct ResourceGained : IEventRecord
{
    public int SequenceId { get; set; }
    public ResourceKind Kind;
    public float Amount;
}

/// <summary>Ресурс списан из общего хранилища.</summary>
[TransientEvent]
public struct ResourceSpent : IEventRecord
{
    public int SequenceId { get; set; }
    public ResourceKind Kind;
    public float Amount;
}

/// <summary>Поставлен каркас будущей постройки.</summary>
[TransientEvent]
public struct BlueprintPlaced : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника, а не ссылка на ресурс — так запись переживёт сохранение.</summary>
    public string DefinitionId;

    public Vector2I Cell;
}

/// <summary>
/// Постройка появилась в мире. Публикуется фабрикой сущностей — единственным местом,
/// где постройки рождаются, поэтому документ ловит их все: и достроенные каркасом,
/// и поставленные сразу, вроде стартовой базы.
///
/// Отдельно от ConstructionCompleted намеренно: та запись про то, что стройка ЗАВЕРШЕНА,
/// а эта — про то, что сущность ЕСТЬ. Ёмкость хранилища зависит от второго, а не от первого.
/// </summary>
[TransientEvent]
public struct BuildingSpawned : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2I Cell;
}

/// <summary>Каркас достроен и превратился в готовую сущность.</summary>
[TransientEvent]
public struct ConstructionCompleted : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2I Cell;
}

/// <summary>
/// Снаряд задел цель. Прочность правит не снаряд, а DamageSystem по этому документу:
/// так у каждого попадания есть след, и никто не удаляет ноду посреди чужого обхода.
/// </summary>
[TransientEvent]
public struct DamageDealt : IEventRecord
{
    public int SequenceId { get; set; }
    public int TargetId;
    public int SourceId;
    public float Amount;
    public Vector2 Pos;
}

/// <summary>Прочность кончилась — сущность выбыла из игры.</summary>
[TransientEvent]
public struct EntityDestroyed : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника: по нему хранилище узнаёт, сколько ёмкости ушло вместе с постройкой.</summary>
    public string DefinitionId;

    public Faction Side;
    public Vector2 Pos;
}

/// <summary>
/// Замер наблюдаемой величины на момент времени. Документ транзиентный, как и все прочие:
/// хранит ряд не журнал, а <see cref="TimeSeriesProjection"/>.
///
/// ЭТО НЕ ФАКТ, А СРЕЗ, и разница существенна. Прочие документы отвечают на вопрос
/// «что случилось», этот — на вопрос «сколько было в такой-то момент». В терминах 1С
/// первое есть документ, второе — периодический регистр сведений. Смешать их значило бы
/// превратить журнал из шины в хранилище, а он для этого не предназначен: чистится каждый
/// кадр и растёт без предела, если чистку отключить.
/// </summary>
[TransientEvent]
public struct MetricSampled : IEventRecord
{
    public int SequenceId { get; set; }

    /// <summary>Имя ряда: terror.raw, terror.production, income.metal.</summary>
    public string Channel;

    public float Value;

    /// <summary>
    /// Номер замера от начала партии. Он же отметка времени: шаг постоянен, поэтому
    /// хранить секунды отдельно незачем — время равно номеру, умноженному на шаг.
    /// </summary>
    public int Tick;
}

/// <summary>Враг вышел на карту.</summary>
[TransientEvent]
public struct EnemySpawned : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника, а не ссылка на ресурс — так запись переживёт сохранение.</summary>
    public string DefinitionId;

    public Vector2 Pos;
}
