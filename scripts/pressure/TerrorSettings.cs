using Godot;

/// <summary>
/// Настройка террора: всё, что подбирается на партиях, собрано в один ресурс.
///
/// ПОЧЕМУ НЕ TOML, КАК ОСТАЛЬНОЕ СОДЕРЖИМОЕ. Разделение проведено по способу настройки,
/// а не по роду данных. Вес отдельной постройки правится текстом и потому лежит в её
/// .toml рядом с прочими её числами. Кривая насыщения текстом не правится — её рисуют
/// мышью, и редактор для этого в движке уже есть. Заводить свой значило бы писать то,
/// что и так работает в инспекторе.
///
/// ЗАЧЕМ КРИВЫЕ ВООБЩЕ. Не ради того, чтобы гасить прибавку. База в базостроительной
/// игре растёт примерно экспоненциально: каждый новый генератор ускоряет постройку
/// следующего. Вогнутая кривая переводит экспоненциальный рост входа в примерно линейный
/// рост давления — иначе поздняя партия получила бы волны, несоизмеримые с возможностями
/// игрока, а показатель перестал бы что-либо различать.
///
/// ПОЧЕМУ У КРИВОЙ ЕСТЬ ХВОСТ. Ресурс Curve определён на ограниченном домене. Если просто
/// зажать вход в отрезок, показатель получит горизонтальную асимптоту — то есть после
/// некоторого размера расширение станет бесплатным, а это отменяет решение «расширяться
/// или нет». Поэтому за опорным значением кривая продолжается прямой с наклоном Tail:
/// видимая часть остаётся редактируемой, поведение за её пределами определено и не имеет
/// потолка.
/// </summary>
[GlobalClass]
public partial class TerrorSettings : Resource
{
    // ── Ход времени ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Шаг пересчёта и замера, секунд игрового времени. Он же шаг ряда в регистре сведений,
    /// поэтому по ходу партии меняться не может: номер замера там служит отметкой времени.
    /// </summary>
    [Export] public float SampleInterval = 1f;

    /// <summary>
    /// Постоянная сглаживания, секунд. Давление читает не мгновенный показатель, а следующий
    /// за ним с задержкой, и вот почему: показатель есть уровень, а не накопитель, он падает
    /// вместе с потерянными постройками. Без задержки снос собственного форпоста перед волной
    /// облегчал бы её, и появилась бы вырожденная тактика. Рост при этом учитывается почти
    /// сразу — задержка растягивает только падение, потому что расти показателю есть куда,
    /// а падать он может рывком.
    /// </summary>
    [Export] public float SmoothingSeconds = 60f;

    // ── Приведение метала и энергии ───────────────────────────────────────────────

    /// <summary>
    /// Сколько энергии стоит единица метала. Курс не выдуман: это курс экстрактора
    /// (18 энергии на 5 метала), объявленный в дизайне выгодным и целевым. Курс синтезатора
    /// сюда не годится — он намеренно невыгоден и означает сброс излишков, а не обмен.
    /// </summary>
    [Export] public float EnergyPerMetal = 3.6f;

    // ── Зоны удалённости ──────────────────────────────────────────────────────────

    /// <summary>Радиус зоны высадки в клетках: всё внутри считается ближним.</summary>
    [Export] public float NearRadiusCells = 8f;

    /// <summary>Внешний радиус средней зоны в клетках. Дальше — фронтир.</summary>
    [Export] public float MidRadiusCells = 15f;

    [Export] public float NearZone = 0.2f;
    [Export] public float MidZone = 1f;
    [Export] public float FarZone = 2.5f;

    // ── Производство ──────────────────────────────────────────────────────────────

    [ExportGroup("Производство")]
    [Export] public Curve ProductionCurve;

    /// <summary>Величина входа, которая считается полной. На ней кривая доходит до конца.</summary>
    [Export] public float ProductionReference = 200f;

    /// <summary>Вклад слагаемого в очках террора при полном входе.</summary>
    [Export] public float ProductionWeight = 20f;

    /// <summary>Наклон прямой за опорным значением, в долях кривой на единицу входа.</summary>
    [Export] public float ProductionTail = 0.15f;

    // ── Экспансия ─────────────────────────────────────────────────────────────────

    [ExportGroup("Экспансия")]
    [Export] public Curve ExpansionCurve;
    [Export] public float ExpansionReference = 150f;
    [Export] public float ExpansionWeight = 45f;
    [Export] public float ExpansionTail = 0.15f;

    // ── Армия ─────────────────────────────────────────────────────────────────────

    [ExportGroup("Армия")]
    [Export] public Curve ArmyCurve;
    [Export] public float ArmyReference = 60f;
    [Export] public float ArmyWeight = 35f;
    [Export] public float ArmyTail = 0.15f;

    // ── Запасная кривая ───────────────────────────────────────────────────────────

    [ExportGroup("Запасная кривая")]

    /// <summary>
    /// Показатель степени, по которому считается вклад, если кривая не назначена.
    /// Нужен не ради удобства: без него незаполненное поле в инспекторе роняло бы
    /// подсчёт целиком, а так игра запускается и ведёт себя предсказуемо.
    /// </summary>
    [Export] public float FallbackExponent = 0.6f;

    /// <summary>Коэффициент постройки по расстоянию от точки высадки, в клетках.</summary>
    public float ZoneCoefficient(float distanceCells)
    {
        if (distanceCells <= NearRadiusCells)
            return NearZone;

        return distanceCells <= MidRadiusCells ? MidZone : FarZone;
    }

    public float Production(float raw) =>
        Shape(ProductionCurve, raw, ProductionReference, ProductionWeight, ProductionTail);

    public float Expansion(float raw) =>
        Shape(ExpansionCurve, raw, ExpansionReference, ExpansionWeight, ExpansionTail);

    public float Army(float raw) =>
        Shape(ArmyCurve, raw, ArmyReference, ArmyWeight, ArmyTail);

    /// <summary>
    /// Сырая величина в очки террора: нормировать опорным значением, провести через кривую,
    /// умножить на вес. За опорным значением кривая продолжается прямой — см. заголовок класса.
    /// </summary>
    private float Shape(Curve curve, float raw, float reference, float weight, float tail)
    {
        if (reference <= 0f || raw <= 0f)
            return 0f;

        float t = raw / reference;

        float shaped = t <= 1f
            ? Sample(curve, t)
            : Sample(curve, 1f) + (t - 1f) * tail;

        return shaped * weight;
    }

    private float Sample(Curve curve, float t) =>
        curve != null ? curve.Sample(t) : Mathf.Pow(t, FallbackExponent);
}
