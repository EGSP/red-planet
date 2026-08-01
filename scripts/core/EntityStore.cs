using System.Collections.Generic;
using Godot;

/// <summary>
/// Живые сущности игры по их EntityId.
///
/// ЗАЧЕМ. Документы носят идентификаторы, а не ссылки на ноды — так они переживают
/// смерть сущности и сохранение. Но система, отреагировавшая на документ, обычно хочет
/// живую ноду. Этот перевод id в ноду и делает хранилище.
///
/// Третий контейнер с суффиксом Store, и различаются они только ключом:
/// EventStore — потоки по типу документа, ProjectionStore — проекции по типу,
/// EntityStore — сущности по числовому id.
///
/// Заполняется только через Spawner: он единственный, кто создаёт сущности,
/// поэтому руками сюда добавлять не нужно. Снятие тоже делает Spawner — по подписке
/// на выбытие из индекса, а не OnDestroyed каждой сущности.
/// </summary>
public sealed class EntityStore
{
    private readonly Dictionary<int, Node> _byId = new();

    public void Add(int id, Node node) => _byId[id] = node;

    public void Remove(int id) => _byId.Remove(id);

    /// <summary>
    /// Нода по id или null, если сущности уже нет.
    /// Мёртвые записи отсеиваются здесь, поэтому вызывающему проверять живость не нужно.
    /// </summary>
    public Node Get(int id) =>
        _byId.TryGetValue(id, out var node) && Alive.Is(node) ? node : null;

    public T Get<T>(int id) where T : Node => Get(id) as T;
}
