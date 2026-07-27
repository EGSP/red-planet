using System.Collections.Generic;
using Godot;

/// <summary>
/// Общее хранилище базы — доступно отовсюду, никаких личных инвентарей.
/// Проекция потоков прихода и расхода ресурсов.
///
/// КАК ПРИМЕНЯТЬ. Читать через Get, а писать — НЕЛЬЗЯ: у проекции нет и не должно быть
/// методов «добавить» или «списать». Чтобы ресурс изменился, публикуй документ:
///
///     events.Append(new ResourceGained { Kind = ResourceKind.Ore, Amount = 5f });
///
/// Так у каждого изменения запаса есть след в журнале, и правило «состояние меняется
/// только через документы» не обходится втихую.
///
/// Суффикс Projection в имени намеренный: у событий природу типа выдаёт прошедшее время
/// (ResourceSpent), а существительное вроде Stockpile само по себе ничего не говорит —
/// без суффикса тип читался бы как «просто склад».
/// </summary>
public sealed class StockpileProjection : Projection
{
    private readonly Dictionary<ResourceKind, float> _amount = new()
    {
        [ResourceKind.Ore] = 0f,
        [ResourceKind.Metal] = 0f,
    };

    public float Get(ResourceKind kind) => _amount.TryGetValue(kind, out var value) ? value : 0f;

    public override void Subscribe(EventStore events)
    {
        events.Stream<ResourceGained>().Appended += record =>
        {
            _amount[record.Kind] = Get(record.Kind) + record.Amount;
            NotifyChanged();
        };

        events.Stream<ResourceSpent>().Appended += record =>
        {
            // Ниже нуля не опускаемся: расход всегда проверяется до публикации,
            // но накопленная погрешность float не должна давать отрицательный запас
            _amount[record.Kind] = Mathf.Max(0f, Get(record.Kind) - record.Amount);
            NotifyChanged();
        };
    }

    /// <summary>
    /// Какая доля запрошенного объёма реально доступна, от 0 до 1.
    ///
    /// ЗАЧЕМ ИМЕННО ДОЛЯ. Каркас за тик хочет ресурсы сразу нескольких видов
    /// и не может взять их частично по отдельности — иначе стройка съедала бы руду,
    /// стоя без метала. Он спрашивает долю одним запросом, масштабирует на неё
    /// и прогресс, и списание. Не хватает ресурсов — стройка идёт медленнее,
    /// а не встаёт колом и не портит баланс.
    /// </summary>
    public float AvailableFraction(float needOre, float needMetal)
    {
        float fraction = 1f;

        if (needOre > 0f)
            fraction = Mathf.Min(fraction, Get(ResourceKind.Ore) / needOre);

        if (needMetal > 0f)
            fraction = Mathf.Min(fraction, Get(ResourceKind.Metal) / needMetal);

        return Mathf.Clamp(fraction, 0f, 1f);
    }
}
