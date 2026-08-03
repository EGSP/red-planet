using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Как список видов участвует в наборе состава волны.
///
/// Режим и доля бюджета — величины разной природы, и потому это разные ключи. Режим
/// отвечает на вопрос, что вообще допустимо; доля — на вопрос, сколько бюджета на это
/// уйдёт. Слитое перечисление вида «whitelist, blacklist, atleast» не позволило бы
/// выразить «только стрелки и толстяки, причём толстяков не менее трети»: пришлось бы
/// выбирать между отбором и квотой, хотя нужны обе.
/// </summary>
public enum UnitListMode
{
    /// <summary>
    /// Сужает допустимое. Если списков с этим режимом нет ни одного, допустимы все виды
    /// противника; если есть хоть один, допустимо только объединение таких списков.
    /// </summary>
    Allow,

    /// <summary>Вычитается из допустимого после того, как оно собрано режимом Allow.</summary>
    Deny,

    /// <summary>
    /// На отбор не влияет вовсе, задаёт только целевую долю. Нужен затем, чтобы отвести
    /// части бюджета конкретным видам, не выписывая при этом всех прочих поимённо.
    /// </summary>
    Limit,
}

/// <summary>Список видов волны со своим режимом и своей долей бюджета.</summary>
public sealed class WaveUnitList
{
    public UnitListMode Mode = UnitListMode.Allow;

    /// <summary>Ссылки строками из файла. Живут до связывания, дальше не используются.</summary>
    public string[] UnitIds = Array.Empty<string>();

    /// <summary>
    /// Целевая доля бюджета волны, приходящаяся на виды этого списка. Ноль означает,
    /// что доля не задана и список участвует только в отборе.
    ///
    /// Именно ЦЕЛЕВАЯ, а не наименьшая: доля выполняется не всегда — самый дешёвый вид
    /// списка может оказаться дороже отведённой ему квоты. Слово «не менее» обещало бы
    /// больше, чем сборщик способен дать.
    /// </summary>
    public float TargetBudgetShare;

    /// <summary>Разрешённые ссылки. Заполняет ContentCompiler при связывании.</summary>
    public List<UnitDefinition> Units = new();
}

/// <summary>
/// Переопределения формы появления. Незаданное берётся из <see cref="WaveSettings"/>,
/// поэтому все поля необязательны: волна описывает, чем отличается, а не повторяет
/// умолчания целиком.
/// </summary>
public sealed class WaveShapeOverrides
{
    public float? NearArcDegrees;
    public float? FarArcDegrees;
    public float? RadiusOffsetMultiplier;
    public float? RadiusDepthMultiplier;
    public float? SpacingCells;
    public int? Groups;
    public float? GroupsArcDegrees;
}

/// <summary>
/// Волна — преднастроенное событие давления: сколько мощи приходит разом, из кого она
/// набирается, какой формой появляется и когда после неё приходит следующая.
///
/// ТРИ ВЕЛИЧИНЫ НЕЗАВИСИМЫ, И ЭТО НАМЕРЕННО. <see cref="TerrorMin"/> с <see cref="TerrorMax"/>
/// определяют, применима ли волна вообще, то есть служат условием, а не мерой.
/// <see cref="ArmyPowerBudget"/> определяет её размер и по умолчанию от террора не зависит:
/// связь появляется только там, где автор волны задал <see cref="ArmyPowerPerTerror"/>
/// отличным от нуля. <see cref="ChillIntervalMultiplier"/> определяет темп, то есть паузу
/// перед следующей волной. Смешивать их не нужно: применимость, размер и темп подбираются
/// по разным признакам и на разных партиях.
///
/// СОБСТВЕННОГО ВЕСА ОТБОРА У ВОЛНЫ НЕТ. Среди применимых выбор равновероятен, а перевес
/// дают теги, запрошенные предыдущей волной через <see cref="PreferNext"/>. Отдельное число
/// было бы вторым регулятором частоты, и согласовывать его пришлось бы с первым.
/// </summary>
public sealed class WaveDefinition
{
    public string Id = "";
    public string DisplayName = "";

    /// <summary>
    /// Чем эта волна является: swarm, heavy_group и тому подобное. Теги волн держатся
    /// строками и в общий реестр из tags.toml не входят: тот описывает сущности мира,
    /// а здесь размечаются события давления. Защиту от опечатки даёт проверка связности —
    /// тег, которого нет ни у одной волны, становится ошибкой сборки.
    /// </summary>
    public string[] Tags = Array.Empty<string>();

    /// <summary>Теги, которым дать перевес при отборе следующей волны.</summary>
    public string[] PreferNext = Array.Empty<string>();

    /// <summary>Нижняя граница террора. Отрицательное значение означает «безразлично».</summary>
    public float TerrorMin = -1f;

    /// <summary>Верхняя граница террора. Отрицательное значение означает «безразлично».</summary>
    public float TerrorMax = -1f;

    /// <summary>Бюджет волны в единицах боевой мощи.</summary>
    public float ArmyPowerBudget = 10f;

    /// <summary>
    /// Прибавка к бюджету за очко сглаженного террора. Ноль — размер волны от показателя
    /// не зависит, и это умолчание.
    ///
    /// Множителя от террора рядом нет намеренно: он выражается через эту же прибавку,
    /// поскольку budget * (1 + k * terror) есть то же самое, что budget + (budget * k) * terror.
    /// Два ключа для одной величины породили бы вопрос, что делать, когда заданы оба.
    /// </summary>
    public float ArmyPowerPerTerror;

    /// <summary>
    /// Множитель к константе отдыха подсистемы. Больше единицы — следующая волна нескоро,
    /// меньше — накат за накатом.
    /// </summary>
    public float ChillIntervalMultiplier = 1f;

    /// <summary>
    /// Доля разброса интервала отдыха. Не задано — берётся константа подсистемы.
    ///
    /// Разброс задан долей, а не числом секунд: при интервале в две минуты и в полминуты
    /// один и тот же абсолютный сдвиг значил бы совсем разное.
    /// </summary>
    public float? ChillIntervalOffset;

    public List<WaveUnitList> UnitLists = new();

    public WaveShapeOverrides Shape = new();

    /// <summary>Подходит ли волна к этому показателю террора.</summary>
    public bool Fits(float terror) =>
        (TerrorMin < 0f || terror >= TerrorMin) && (TerrorMax < 0f || terror <= TerrorMax);

    /// <summary>Бюджет волны при этом показателе террора.</summary>
    public float Budget(float terror) =>
        Mathf.Max(ArmyPowerBudget + ArmyPowerPerTerror * terror, 0f);

    public bool HasTag(string tag)
    {
        foreach (string own in Tags)
            if (own == tag)
                return true;

        return false;
    }
}
