using Godot;

/// <summary>
/// Вид ресурса. Названо Kind, потому что слово Resource занято классом Godot.
/// </summary>
public enum ResourceKind
{
    Ore,
    Metal,
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
    public string DefId;

    public Vector2I Cell;
}

/// <summary>Каркас достроен и превратился в готовую сущность.</summary>
[TransientEvent]
public struct ConstructionCompleted : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefId;
    public Vector2I Cell;
}

/// <summary>Месторождение выработано, слот руды освободился.</summary>
[TransientEvent]
public struct OreDepleted : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public Vector2I Cell;
}
