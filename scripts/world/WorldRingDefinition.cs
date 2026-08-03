using Godot;

/// <summary>
/// Одно кольцо мира: толщина пояса от предыдущего края, раскладка кластеров руды
/// и множитель террора для построек внутри пояса.
///
/// Кольца идут подряд. Внутренний радиус равен сумме <see cref="Radius"/> всех
/// предыдущих; внешний — внутренний плюс собственный <see cref="Radius"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldRingDefinition : Resource
{
    // ── Кольцо ────────────────────────────────────────────────────────────────────

    [ExportGroup("Кольцо")]

    /// <summary>Толщина кольца в клетках карты.</summary>
    [Export(PropertyHint.Range, "1,128,1")] public int Radius = 10;

    /// <summary>
    /// Множитель экспансии для построек, лежащих в этом кольце. Ближе к центру —
    /// меньше вклад, на краю — больше.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.01")] public float TerrorMultiplier = 1f;

    // ── Кластеры ──────────────────────────────────────────────────────────────────

    [ExportGroup("Кластеры")]

    /// <summary>Нижняя граница числа кластеров в кольце.</summary>
    [Export(PropertyHint.Range, "0,64,1")] public int ClusterCountMin = 2;

    /// <summary>Верхняя граница числа кластеров в кольце.</summary>
    [Export(PropertyHint.Range, "0,64,1")] public int ClusterCountMax = 4;

    /// <summary>Нижняя граница числа рудников в одном кластере.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int SpotsInClusterMin = 1;

    /// <summary>
    /// Верхняя граница числа рудников в одном кластере. По ней же считается габарит
    /// области кластера — место под худший случай, даже если выпало меньше точек.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1")] public int SpotsInClusterMax = 2;

    /// <summary>
    /// Вес размещения центра кластера вдоль кольца: 0 — внутренний край, 1 — внешний.
    /// Не задана — используется колокол с центром посередине пояса.
    /// </summary>
    [Export] public Curve ClusterProbabilityCurve;
}
