using Godot;

/// <summary>
/// Запас прочности сущности. Обычный C#-объект, которым владеет нода —
/// отдельную ноду на характеристику не заводим.
///
/// Урон сюда приходит только из DamageSystem, разбирающей документы DamageDealt:
/// снаряд не бьёт цель напрямую, он публикует факт попадания.
///
/// Неуязвимость — не «бесконечное здоровье», а отдельный признак: коммандер урон
/// накапливает и показывает, но не умирает. Полученное считаем всегда, даже если броня
/// не пробивается — на этом и держится счётчик у коммандера.
/// </summary>
public sealed class Health
{
    public Health(float max, bool invulnerable = false)
    {
        Max = Mathf.Max(1f, max);
        Current = Max;
        Invulnerable = invulnerable;
    }

    public float Max { get; }

    public float Current { get; private set; }

    /// <summary>Сколько урона сущность получила за жизнь — считается и у неуязвимых.</summary>
    public float TotalTaken { get; private set; }

    public bool Invulnerable { get; set; }

    public bool IsDead => !Invulnerable && Current <= 0f;

    public float Ratio => Mathf.Clamp(Current / Max, 0f, 1f);

    public void Take(float amount)
    {
        if (amount <= 0f)
            return;

        TotalTaken += amount;

        if (!Invulnerable)
            Current = Mathf.Max(0f, Current - amount);
    }

    /// <summary>Задел под ремонт: чинить выше максимума нельзя.</summary>
    public void Repair(float amount) => Current = Mathf.Min(Max, Current + Mathf.Max(0f, amount));
}

/// <summary>
/// Всё, во что можно попасть: постройки, каркасы, юниты, враги.
///
/// Реализующие себя ноды состоят в группе <see cref="Targeting.Group"/> — по ней
/// и снаряды ищут попадание, и системы выбирают ближайшую цель, не перебирая полмира.
/// </summary>
public interface IDamageable
{
    /// <summary>Стабильный id сущности: документы носят его, а не ссылку на ноду.</summary>
    int EntityId { get; }

    Health Health { get; }

    Vector2 GlobalPosition { get; }

    /// <summary>Радиус попадания в пикселях — по нему снаряд решает, задел ли он цель.</summary>
    float HitRadius { get; }

    /// <summary>Чья сторона. Снаряд бьёт только по чужим.</summary>
    Faction Faction { get; }

    /// <summary>Прочность кончилась: убрать себя из игры, освободив сетку и реестр.</summary>
    void OnDestroyed();
}
