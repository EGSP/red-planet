using Godot;

/// <summary>
/// Ремонт — это стройка уже стоящей постройки, поэтому вся его механика сведена сюда,
/// а зовут её одинаково и подвижный фабрикатор, и неподвижная башня-сборщик.
///
/// ЕДИНЫЙ ИНСТРУМЕНТ. Отдельной «ремонтной мощности» нет: чинит та же `BuildPower`,
/// что и строит, и жрёт столько же энергии. Как в PA, где build power одна на всё.
///
/// КУРС ВЫВОДИТСЯ ИЗ ПОСТРОЙКИ: `MaxHealth / CostMetal` прочности за единицу метала.
/// Отсюда сразу два приятных следствия, ради которых курс и не задают руками:
/// починить здание с нуля стоит ровно столько же, сколько построить, и занимает столько же
/// времени. Балансировать отдельно ремонт не нужно — он балансируется сам, вместе с ценой.
/// </summary>
public interface IRepairable
{
    Health Health { get; }

    /// <summary>
    /// Сколько прочности даёт единица метала. Ноль — чинить нечем: у сущности нет цены,
    /// а значит и курса. Коммандеру цену задали именно ради курса — строить его всё равно
    /// негде, а починить нужно.
    /// </summary>
    float HealthPerMetal { get; }

    Vector2 GlobalPosition { get; }
}

public static class Repair
{
    /// <summary>Заявка на тик: та же, что у стройки с такой же мощностью.</summary>
    public static void Declare(EconomyLedger ledger, float buildPower, float energyPerPower)
    {
        if (buildPower <= 0f)
            return;

        ledger.Request(ResourceKind.Metal, buildPower);
        ledger.Request(ResourceKind.Energy, buildPower * energyPerPower);
    }

    /// <summary>
    /// Отработать тик ремонта: списать ресурсы и восстановить прочность.
    /// Возвращает, сколько прочности удалось вернуть.
    /// </summary>
    public static float Run(IRepairable target, float buildPower, float energyPerPower,
        double dt, EconomyRates rates)
    {
        if (target?.Health == null || buildPower <= 0f)
            return 0f;

        var events = GameManager.I.Events;

        // Энергия инструмента — по своей доле: луч светит на полную, даже если метала нет
        float energy = buildPower * energyPerPower * (float)dt * rates.Energy;
        if (energy > 0f)
            events.Append(new ResourceSpent { Kind = ResourceKind.Energy, Amount = energy });

        float rate = target.HealthPerMetal;
        if (rate <= 0f)
            return 0f;

        float missing = target.Health.Max - target.Health.Current;
        if (missing <= 0f)
            return 0f;

        // Метал идёт по темпу работы, как и в стройке, а прочность — по курсу постройки
        float metal = buildPower * (float)dt * rates.Work;
        float healed = Mathf.Min(metal * rate, missing);

        if (healed <= 0f)
            return 0f;

        target.Health.Repair(healed);

        if (target is CanvasItem canvas)
            canvas.QueueRedraw();

        events.Append(new ResourceSpent { Kind = ResourceKind.Metal, Amount = healed / rate });

        return healed;
    }
}
