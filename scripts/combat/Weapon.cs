using Godot;

/// <summary>
/// Состояние ствола: всё, что меняется у оружия по ходу боя. Сам справочник (WeaponDefinition)
/// неизменяем и общий на всех носителей, поэтому перезарядку держим здесь — рядом с носителем.
/// </summary>
public sealed class WeaponState
{
    public float Cooldown { get; private set; }

    public bool Ready => Cooldown <= 0f;

    public void Tick(double dt) => Cooldown = Mathf.Max(0f, Cooldown - (float)dt);

    /// <summary>Выстрелить, если ствол готов. Вернула true — выстрел состоялся.</summary>
    public bool TryFire(float interval)
    {
        if (Cooldown > 0f)
            return false;

        Cooldown = Mathf.Max(0.01f, interval);
        return true;
    }
}

/// <summary>
/// Носитель оружия: враг, турель или коммандер. WeaponSystem обходит разрез по этому
/// признаку и для каждого решает одно и то же — есть ли цель, довёрнут ли ствол,
/// готов ли выстрел.
///
/// Сущность не стреляет сама: она лишь отвечает на вопросы системы и умеет доворачиваться.
/// </summary>
public interface IArmed
{
    int EntityId { get; }

    Faction Faction { get; }

    /// <summary>Справочник оружия. Null — сущность безоружна, система её пропустит.</summary>
    WeaponDefinition Weapon { get; }

    WeaponState Gun { get; }

    /// <summary>
    /// Ось прицеливания в радианах. У турели это поворот башни; у подвижного —
    /// направление инструмента, если он наводится отдельно от корпуса.
    /// </summary>
    float Facing { get; }

    Vector2 GlobalPosition { get; }

    /// <summary>Позволено ли открывать огонь сейчас: коммандер стреляет только без приказов.</summary>
    bool CanFire { get; }

    /// <summary>
    /// Своя цель, если сущность её уже выбрала (враг идёт именно к ней).
    /// Null — система сама найдёт ближайшую в радиусе.
    /// </summary>
    IDamageable FireTarget { get; }

    /// <summary>
    /// Довернуть ось прицеливания к точке за этот кадр, не быстрее скорости вращения.
    /// У подвижного с независимым инструментом крутится ствол или манипулятор,
    /// а не корпус: корпус на ходу принадлежит системе движения.
    /// </summary>
    void AimAt(Vector2 point, double dt);
}
