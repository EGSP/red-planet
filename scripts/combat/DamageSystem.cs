using Godot;

/// <summary>
/// Единственное место, где прочность реально убывает. Разбирает документы о попаданиях
/// пачкой в фазе реакции — уже после того, как отработали движение, стрельба и полёт.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ ФАЗА. Снаряд не может убить цель прямо посреди своего шага: в этот
/// момент по группам ходят другие системы, и удаление ноды из-под них рождает висячие ссылки.
/// Здесь же кадр уже отработал, и вывести сущность из игры безопасно.
///
/// Смерть — это документ EntityDestroyed. Сама сущность лишь убирает себя из мира
/// (освобождает клетки, отпускает исполнителей), а кто ещё среагирует — её не касается.
/// </summary>
public partial class DamageSystem : GameSystem
{
    public override void Step(double dt)
    {
        var events = GM.Events;

        foreach (var record in events.Stream<DamageDealt>().Records)
        {
            var node = GM.Entities.Get(record.TargetId);
            if (node is not IDamageable target || !Targeting.IsValid(node))
                continue;

            target.Health.Take(record.Amount);

            // Статичные сущности сами себя не перерисовывают — толкаем их только по факту урона
            if (node is CanvasItem canvas)
                canvas.QueueRedraw();

            if (!target.Health.IsDead)
                continue;

            events.Append(new EntityDestroyed
            {
                EntityId = target.EntityId,
                Side = target.Faction,
                Pos = target.GlobalPosition,
            });

            target.OnDestroyed();
        }
    }
}
