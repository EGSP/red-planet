using System.Collections.Generic;
using Godot;

/// <summary>
/// Сведения о сущности под курсором, приведённые к списку строк «название — значение».
///
/// ЗАЧЕМ ОТДЕЛЬНЫМ МЕСТОМ, А НЕ МЕТОДОМ У СУЩНОСТИ. Справка складывается почти целиком
/// из справочника, а не из состояния: скорость, дальность, урон, цена — всё это свойства
/// вида, одинаковые у любого экземпляра. Спрашивать их у ноды значило бы завести у юнита
/// и у постройки по одинаковому набору методов ради того, что и так лежит в определении.
/// Изменчивого здесь ровно два: прочность и готовность каркаса.
///
/// СОСТАВ СТРОК ЗАВИСИТ ОТ ЗАПОЛНЕННЫХ СЕКЦИЙ, а не от рода сущности. Определение одно
/// на всех (см. <see cref="UnitDefinition"/>), и роль в нём выражается тем, что заполнено:
/// есть ствол — будет строка урона, есть переработка — строка выхода метала. Отдельной
/// раскладки под юнита, постройку и каркас поэтому не требуется.
/// </summary>
public static class HoverFacts
{
    /// <summary>Одна строка справки. Пустое название означает строку без подписи.</summary>
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

    /// <summary>Чья сторона. Строка идёт первой: она важнее любых чисел.</summary>
    public static string SideOf(IDamageable target) =>
        target.Faction == Faction.Player ? "своя сторона" : "противник";

    /// <summary>
    /// Определение сущности. Через справочник по идентификатору не берётся: у каркаса
    /// ключ намеренно пуст (см. <see cref="Blueprint.DefinitionId"/>), а определение у него
    /// есть, и справка о начатой стройке нужна не меньше, чем о готовой постройке.
    /// </summary>
    public static UnitDefinition DefinitionOf(IDamageable target) => target switch
    {
        Unit unit => unit.Definition,
        Building building => building.Definition,
        Blueprint frame => frame.Definition,
        _ => GameManager.I?.Catalog.Unit(target.DefinitionId),
    };

    /// <summary>Весь набор строк по сущности, в порядке показа.</summary>
    public static List<Fact> Collect(IDamageable target)
    {
        var facts = new List<Fact>();
        var definition = DefinitionOf(target);

        AddCondition(facts, target);

        if (definition == null)
            return facts;

        AddMobility(facts, definition);
        AddWeapon(facts, definition.Weapon);
        AddWorkTool(facts, definition.BuildTool);
        AddEconomy(facts, definition);
        AddCost(facts, definition);

        return facts;
    }

    /// <summary>Прочность и, у каркаса, готовность стройки.</summary>
    private static void AddCondition(List<Fact> facts, IDamageable target)
    {
        var health = target.Health;

        if (health != null)
            facts.Add(new Fact("Прочность",
                health.Invulnerable
                    ? $"{Mathf.RoundToInt(health.Current)} (неуязвим)"
                    : $"{Mathf.RoundToInt(health.Current)} / {Mathf.RoundToInt(health.Max)}"));

        if (target is Blueprint frame)
            facts.Add(new Fact("Готовность", $"{Mathf.FloorToInt(frame.Ratio * 100f)}%"));
    }

    private static void AddMobility(List<Fact> facts, UnitDefinition definition)
    {
        if (definition.Speed > 0f)
            facts.Add(new Fact("Скорость", $"{definition.Speed:0.##} кл/с"));

        if (definition.VisionRange > 0f)
            facts.Add(new Fact("Обзор", $"{definition.VisionRange:0.#} кл"));
    }

    /// <summary>
    /// Ствол. Урон подписан вместе с уроном в секунду: одиночное число ничего не говорит
    /// о том, кто опаснее, — редкий тяжёлый выстрел и частый лёгкий сравниваются только так.
    /// </summary>
    private static void AddWeapon(List<Fact> facts, WeaponDefinition weapon)
    {
        if (weapon == null)
            return;

        float perSecond = weapon.FireInterval > 0f ? weapon.Damage / weapon.FireInterval : 0f;

        facts.Add(new Fact("Оружие", weapon.DisplayName));
        facts.Add(new Fact("Урон", $"{weapon.Damage:0.#} ({perSecond:0.#}/с)"));
        facts.Add(new Fact("Дальность огня", $"{weapon.Range:0.#} кл"));
    }

    private static void AddWorkTool(List<Fact> facts, WorkToolDefinition tool)
    {
        if (tool == null || !tool.CanWork)
            return;

        facts.Add(new Fact("Сборка", $"{tool.Power:0.#} мет/с"));
        facts.Add(new Fact("Дальность работы", $"{tool.Range:0.#} кл"));
    }

    /// <summary>Что сущность даёт базе и чего ей стоит содержание.</summary>
    private static void AddEconomy(List<Fact> facts, UnitDefinition definition)
    {
        if (definition.EnergyProduction > 0f)
            facts.Add(new Fact("Энергия", $"+{definition.EnergyProduction:0.#}/с"));

        if (definition.MetalProduction > 0f)
            facts.Add(new Fact("Метал", $"+{definition.MetalProduction:0.#}/с"));

        if (definition.Conversion is { } conversion)
        {
            if (conversion.EnergyDrain > 0f)
                facts.Add(new Fact("Расход энергии", $"−{conversion.EnergyDrain:0.#}/с"));

            if (conversion.MetalOutput > 0f)
                facts.Add(new Fact("Выход метала", $"+{conversion.MetalOutput:0.#}/с"));
        }

        if (definition.Plant is { } plant)
            facts.Add(new Fact("Мощность завода", $"{plant.BuildPower:0.#} мет/с"));

        if (definition.MetalStorage > 0f)
            facts.Add(new Fact("Склад метала", $"+{definition.MetalStorage:0}"));

        if (definition.EnergyStorage > 0f)
            facts.Add(new Fact("Склад энергии", $"+{definition.EnergyStorage:0}"));
    }

    private static void AddCost(List<Fact> facts, UnitDefinition definition)
    {
        if (definition.Assembly is { CostMetal: > 0f } assembly)
            facts.Add(new Fact("Стоимость", $"{assembly.CostMetal:0} метала"));
    }
}
