using System.Collections.Generic;
using Godot;

/// <summary>
/// Общее хранилище базы — доступно отовсюду, никаких личных инвентарей.
/// Проекция потоков прихода и расхода ресурсов.
///
/// КАК ПРИМЕНЯТЬ. Читать через Get, а писать — НЕЛЬЗЯ: у проекции нет и не должно быть
/// методов «добавить» или «списать». Чтобы ресурс изменился, публикуй документ:
///
///     events.Append(new ResourceGained { Kind = ResourceKind.Metal, Amount = 5f });
///
/// Так у каждого изменения запаса есть след в журнале, и правило «состояние меняется
/// только через документы» не обходится втихую.
///
/// ЁМКОСТЬ. Хранилище не бездонное: сверх потолка приход пропадает, как в PA.
/// Потолок складывается из базового запаса и вклада живых построек, а меняется дельтой
/// по документам о достройке и гибели — обходить постройки каждый кадр не нужно.
/// Именно ёмкость делает экономику устойчивой: пока в запасе что-то есть, короткие
/// всплески спроса не роняют производительность базы.
///
/// Суффикс Projection в имени намеренный: у событий природу типа выдаёт прошедшее время
/// (ResourceSpent), а существительное вроде Stockpile само по себе ничего не говорит —
/// без суффикса тип читался бы как «просто склад».
/// </summary>
public sealed class StockpileProjection : Projection
{
    private readonly Dictionary<ResourceKind, float> _amount = new()
    {
        [ResourceKind.Metal] = 0f,
        [ResourceKind.Energy] = 0f,
    };

    private readonly Dictionary<ResourceKind, float> _capacity = new()
    {
        [ResourceKind.Metal] = Const.BaseMetalCapacity,
        [ResourceKind.Energy] = Const.BaseEnergyCapacity,
    };

    public float Get(ResourceKind kind) => _amount.TryGetValue(kind, out var value) ? value : 0f;

    public float Capacity(ResourceKind kind) =>
        _capacity.TryGetValue(kind, out var value) ? value : 0f;

    /// <summary>Насколько полно хранилище, 0..1 — для полоски в интерфейсе.</summary>
    public float Fullness(ResourceKind kind)
    {
        float capacity = Capacity(kind);
        return capacity <= 0f ? 0f : Mathf.Clamp(Get(kind) / capacity, 0f, 1f);
    }

    public override void Subscribe(EventStore events)
    {
        events.Stream<ResourceGained>().Appended += record =>
        {
            // Сверх потолка приход пропадает: лишняя энергия не копится, а сгорает
            _amount[record.Kind] = Mathf.Min(Capacity(record.Kind), Get(record.Kind) + record.Amount);
            NotifyChanged();
        };

        events.Stream<ResourceSpent>().Appended += record =>
        {
            // Ниже нуля не опускаемся: расход всегда ужат производительностью,
            // но накопленная погрешность float не должна давать отрицательный запас
            _amount[record.Kind] = Mathf.Max(0f, Get(record.Kind) - record.Amount);
            NotifyChanged();
        };

        events.Stream<BuildingSpawned>().Appended += record =>
            ChangeCapacity(record.DefinitionId, +1f);

        events.Stream<EntityDestroyed>().Appended += record =>
            ChangeCapacity(record.DefinitionId, -1f);
    }

    /// <summary>Постройка появилась или пропала — потолок меняется дельтой, без пересчёта.</summary>
    private void ChangeCapacity(string defId, float sign)
    {
        if (string.IsNullOrEmpty(defId))
            return;

        var def = GameManager.I.Catalog.Unit(defId);
        if (def == null || (def.MetalStorage <= 0f && def.EnergyStorage <= 0f))
            return;

        _capacity[ResourceKind.Metal] = Mathf.Max(Const.BaseMetalCapacity,
            Capacity(ResourceKind.Metal) + sign * def.MetalStorage);

        _capacity[ResourceKind.Energy] = Mathf.Max(Const.BaseEnergyCapacity,
            Capacity(ResourceKind.Energy) + sign * def.EnergyStorage);

        // Потолок упал ниже запаса — лишнее пропадает вместе с разрушенным складом
        foreach (var kind in new[] { ResourceKind.Metal, ResourceKind.Energy })
            _amount[kind] = Mathf.Min(Get(kind), Capacity(kind));

        NotifyChanged();
    }
}
