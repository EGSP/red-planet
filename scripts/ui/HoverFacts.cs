using System.Collections.Generic;
using Godot;

/// <summary>
/// Чем подписана сущность под курсором: название, прочность, обмен с экономикой и вклад
/// в террор.
///
/// ЧЕГО ЗДЕСЬ НЕТ И ПОЧЕМУ. Первый заход выкладывал в панель всё, что нашлось в определении, —
/// скорость, обзор, урон, дальности, цену. Читать это при беглом наведении некогда, а места
/// справка занимала больше панели выделения, над которой стоит. Разбор характеристик
/// принадлежит отдельному экрану, а не подсказке, живущей до следующего движения мыши.
/// Оставлено то, что отвечает на вопрос «во что мне обходится вот это»: прочность, потоки
/// ресурсов и цена внимания противника.
///
/// СТРОКА С НУЛЁМ НЕ ПОКАЗЫВАЕТСЯ. Стена не производит энергии, а генератор не потребляет
/// метала, и подпись «0/с» у обоих означала бы, что величина к сущности вообще применима.
///
/// ЗАЧЕМ ОТДЕЛЬНЫМ МЕСТОМ, А НЕ МЕТОДОМ У СУЩНОСТИ. Почти всё берётся из справочника
/// и одинаково у любого экземпляра вида. Спрашивать это у ноды значило бы завести у юнита,
/// постройки и каркаса по одинаковому набору методов ради того, что и так лежит
/// в определении.
/// </summary>
public static class HoverFacts
{
    /// <summary>Одна строка справки: подпись слева, значение справа.</summary>
    public readonly struct Fact
    {
        public readonly string Label;
        public readonly string Value;

        public Fact(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>Как сущность подписана. У каркаса к названию добавляется его состояние.</summary>
    public static string TitleOf(IDamageable target)
    {
        var definition = DefinitionOf(target);
        string name = definition?.DisplayName ?? "сущность";

        return target is Blueprint ? $"{name} (каркас)" : name;
    }

    /// <summary>
    /// Цвет заголовка — цвет самой сущности из справочника. Так подпись и корпус на карте
    /// опознаются как одно и то же, без сверки по названию.
    /// </summary>
    public static Color ColorOf(IDamageable target) =>
        DefinitionOf(target)?.Color ?? new Color(0.75f, 0.78f, 0.82f);

    /// <summary>Весь набор строк по сущности, в порядке показа.</summary>
    public static List<Fact> Collect(IDamageable target)
    {
        var facts = new List<Fact>();
        var definition = DefinitionOf(target);

        AddHealth(facts, target);

        if (definition == null)
            return facts;

        AddResources(facts, definition);
        AddTerror(facts, target, definition);

        return facts;
    }

    /// <summary>
    /// Прочность. Неуязвимому предел не пишем: делить на максимум, до которого урон всё равно
    /// не доводит, значило бы обещать гибель, которой не будет.
    /// </summary>
    private static void AddHealth(List<Fact> facts, IDamageable target)
    {
        var health = target.Health;

        if (health == null)
            return;

        facts.Add(new Fact("Прочность",
            health.Invulnerable
                ? $"{Mathf.RoundToInt(health.Current)} (неуязвим)"
                : $"{Mathf.RoundToInt(health.Current)} / {Mathf.RoundToInt(health.Max)}"));
    }

    /// <summary>
    /// Обмен с экономикой, по строке на ресурс.
    ///
    /// ПРОИЗВОДСТВО И ПОТРЕБЛЕНИЕ ОДНОГО РЕСУРСА СВЕДЕНЫ В ОДНО ЧИСЛО СО ЗНАКОМ. Двумя
    /// строками на ресурс подпись «Энергия» стояла бы у синтезатора дважды — раз с приходом,
    /// раз с расходом, — и читателю пришлось бы вычитать одно из другого самому. Разными
    /// ресурсами обмениваются в разные стороны, поэтому сальдо ничего не скрывает:
    /// генератор даёт энергию, экстрактор — метал, синтезатор жжёт первое ради второго.
    ///
    /// РАСХОД ИНСТРУМЕНТА СТОИТ ОТДЕЛЬНОЙ СТРОКОЙ, потому что он условный: строитель тратит
    /// энергию, только пока работает, и складывать его с постоянным потоком значило бы
    /// приписывать простаивающему фабрикатору расход, которого нет.
    /// </summary>
    private static void AddResources(List<Fact> facts, UnitDefinition definition)
    {
        float energy = definition.EnergyProduction - (definition.Conversion?.EnergyDrain ?? 0f);
        float metal = definition.MetalProduction + (definition.Conversion?.MetalOutput ?? 0f);

        AddRate(facts, "Энергия", energy);
        AddRate(facts, "Метал", metal);

        AddRate(facts, "Расход в работе", -WorkDrain(definition));
    }

    /// <summary>
    /// Сколько энергии в секунду съедает работа на полную мощность: рука строителя либо
    /// сборочная линия завода. Ноль означает, что работать сущности нечем.
    /// </summary>
    private static float WorkDrain(UnitDefinition definition)
    {
        if (definition.Plant is { } plant)
            return plant.BuildPower * plant.EnergyPerPower;

        return definition.BuildTool is { CanWork: true } tool ? tool.EnergyDrain : 0f;
    }

    /// <summary>
    /// Вклад сущности в террор — со множителем кольца, в котором она стоит, если
    /// определение не отключило модификаторы через <see cref="UnitDefinition.IgnoreTerrorModifiers"/>.
    ///
    /// ЧТО ЗА ЧИСЛО. Это сырое слагаемое до кривой насыщения и до веса, с которыми
    /// <see cref="TerrorSystem"/> сводит четыре слагаемых в один показатель. Долю в готовом
    /// показателе назвать нельзя: кривая нелинейна, и вклад одной и той же постройки зависит
    /// от того, сколько их уже стоит. Сравнивать сущности между собой сырое число позволяет,
    /// а больше от него ничего и не требуется.
    ///
    /// У КАРКАСА И У ПРОТИВНИКА ЕГО НЕТ. Каркас в подсчёт экспансии не входит: он ещё ничего
    /// не занял и отменяется в любой миг. Показатель меряет игрока, поэтому сущности
    /// противника в него не входят вовсе.
    /// </summary>
    private static void AddTerror(List<Fact> facts, IDamageable target, UnitDefinition definition)
    {
        float terror = target switch
        {
            Building building => definition.ExpansionPower *
                (definition.IgnoreTerrorModifiers ? 1f : RingMultiplier(building.Position)),
            Unit { Faction: Faction.Player } => definition.ArmyPower,
            _ => 0f,
        };

        if (terror > 0f)
            facts.Add(new Fact("Террор", $"{terror:0.##}"));
    }

    /// <summary>Множитель кольца мира по удалению от точки высадки, в клетках.</summary>
    private static float RingMultiplier(Vector2 position) =>
        World.Settings.TerrorMultiplierAt(position.DistanceTo(Const.LandingPoint) / Const.Unit);

    /// <summary>Поток ресурса со знаком. Ноль строкой не показывается.</summary>
    private static void AddRate(List<Fact> facts, string label, float rate)
    {
        if (Mathf.IsZeroApprox(rate))
            return;

        facts.Add(new Fact(label, $"{(rate > 0f ? "+" : "−")}{Mathf.Abs(rate):0.##}/с"));
    }

    /// <summary>
    /// Определение сущности. Через справочник по идентификатору не берётся: у каркаса
    /// ключ намеренно пуст (см. <see cref="Blueprint.DefinitionId"/>), а определение у него
    /// есть, и подпись начатой стройки нужна не меньше, чем подпись готовой постройки.
    /// </summary>
    public static UnitDefinition DefinitionOf(IDamageable target) => target switch
    {
        Unit unit => unit.Definition,
        Building building => building.Definition,
        Blueprint frame => frame.Definition,
        _ => GameManager.I?.Catalog.Unit(target.DefinitionId),
    };
}
