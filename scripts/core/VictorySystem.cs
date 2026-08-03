using Godot;

/// <summary>
/// Победа и поражение: достроенный портал и минута удержания — победа; гибель крепости —
/// поражение. Система появляется вместе с сессией и больше нигде не живёт.
///
/// ПОЧЕМУ НЕ «ПОБЕДА В МОМЕНТ ДОСТРОЙКИ». Чеклист требует, чтобы портал был обязательством,
/// а не кнопкой. Минута ожидания после достройки — цена удержания: враг ещё бьёт, крепость
/// ещё может пасть, и победа засчитывается только тем, кто дожил.
///
/// Счётчик порталов, а не привязка к одному id: если портал снесли во время ожидания,
/// отсчёт сбрасывается; если построили снова — начинается заново. Крепость одна, и её гибель
/// сразу завершает партию поражением, даже если отсчёт уже шёл.
/// </summary>
public partial class VictorySystem : GameSystem
{
    public const string PortalId = "portal";
    public const string FortressId = "fortress";

    /// <summary>Сколько секунд нужно удержать достроенный портал до победы.</summary>
    [Export] public float VictoryHoldSeconds = 60f;

    /// <summary>Сколько достроенных порталов сейчас в мире.</summary>
    public int PortalCount { get; private set; }

    /// <summary>
    /// Секунды с тех пор, как в мире появился хотя бы один портал.
    /// Ноль, пока порталов нет или партия уже кончилась.
    /// </summary>
    public float HoldElapsed { get; private set; }

    /// <summary>Доля отсчёта 0..1 — для полосы ожидания.</summary>
    public float HoldRatio
    {
        get
        {
            float need = Mathf.Max(0.001f, VictoryHoldSeconds);
            return Mathf.Clamp(HoldElapsed / need, 0f, 1f);
        }
    }

    protected override void OnLink()
    {
        var events = GM.Events;

        events.Stream<BuildingSpawned>().Appended += OnBuildingSpawned;
        events.Stream<EntityDestroyed>().Appended += OnEntityDestroyed;
    }

    public override void Step(double dt)
    {
        if (GM.GetParent() is not Session session || session.Outcome != SessionOutcome.None)
            return;

        if (PortalCount <= 0)
        {
            HoldElapsed = 0f;
            return;
        }

        HoldElapsed += (float)dt;

        if (HoldElapsed >= VictoryHoldSeconds)
            session.End(SessionOutcome.Victory);
    }

    private void OnBuildingSpawned(BuildingSpawned record)
    {
        if (record.DefinitionId != PortalId)
            return;

        PortalCount++;

        // Первый портал начинает отсчёт с нуля; дополнительные не сбрасывают уже накопленное
        if (PortalCount == 1)
            HoldElapsed = 0f;
    }

    private void OnEntityDestroyed(EntityDestroyed record)
    {
        if (GM.GetParent() is not Session session || session.Outcome != SessionOutcome.None)
            return;

        if (record.DefinitionId == FortressId)
        {
            session.End(SessionOutcome.Defeat);
            return;
        }

        if (record.DefinitionId != PortalId)
            return;

        PortalCount = Mathf.Max(0, PortalCount - 1);

        if (PortalCount == 0)
            HoldElapsed = 0f;
    }
}
