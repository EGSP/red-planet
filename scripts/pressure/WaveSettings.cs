using Godot;

/// <summary>
/// Форма появления волны с подставленными умолчаниями: все числа готовы к употреблению,
/// незаданного здесь уже нет.
///
/// ФОРМА ЗАДАНА ДВУМЯ ДУГАМИ, а не рамкой в мировых координатах. Появление привязано
/// к окружности вокруг точки высадки, поэтому прямоугольник на разных углах означал бы
/// разное. Две дуги — это та же рамка, только её стороны названы по существу: ширина
/// по углу у ближнего к базе края и у дальнего, глубина по радиусу между ними.
///
/// Когда ближняя дуга уже дальней, наступление сходится к одной точке обороны: остриё
/// упирается в базу, тыл растянут. Когда шире — первое касание идёт широким фронтом,
/// а тыл сужается. Равные дуги дают ровную полосу кольцевого сектора.
/// </summary>
public readonly struct WaveShape
{
    public readonly float NearArcDegrees;
    public readonly float FarArcDegrees;
    public readonly float RadiusOffsetMultiplier;
    public readonly float RadiusDepthMultiplier;
    public readonly float SpacingCells;
    public readonly int Groups;
    public readonly float GroupsArcDegrees;

    public WaveShape(float nearArc, float farArc, float radiusOffset, float radiusDepth,
        float spacing, int groups, float groupsArc)
    {
        NearArcDegrees = Mathf.Max(nearArc, 0f);
        FarArcDegrees = Mathf.Max(farArc, 0f);
        RadiusOffsetMultiplier = Mathf.Max(radiusOffset, 0.01f);
        RadiusDepthMultiplier = Mathf.Max(radiusDepth, 0f);
        SpacingCells = Mathf.Max(spacing, 0.1f);
        Groups = Mathf.Max(groups, 1);
        GroupsArcDegrees = groupsArc;
    }

    /// <summary>Радиус ближней к базе дуги в пикселях.</summary>
    public float NearRadiusPx => Const.EnemySpawnRadiusPx * RadiusOffsetMultiplier;

    /// <summary>Глубина формы в пикселях: расстояние между дугами.</summary>
    public float DepthPx => Const.EnemySpawnRadiusPx * RadiusDepthMultiplier;

    public float SpacingPx => SpacingCells * Const.Unit;

    /// <summary>Ширина формы в радианах на глубине t от нуля у ближней дуги до единицы у дальней.</summary>
    public float ArcAt(float t) =>
        Mathf.DegToRad(Mathf.Lerp(NearArcDegrees, FarArcDegrees, t));
}

/// <summary>
/// Настройка подсистемы волн: то, что действует на все волны разом и подбирается один раз
/// на партиях, а не пишется в каждом файле волны.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ САМИХ ВОЛН. Разделение проведено по способу настройки, как
/// и у прочего содержимого. Отдельная волна есть замысел боя — её правят текстом рядом
/// с прочими её числами, и файлов таких будут десятки. Константа отдыха и умолчания формы
/// правятся однажды и мышью в инспекторе, поэтому лежат ресурсом.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ <see cref="PressureSettings"/>. Постоянное давление отвечает
/// на вопрос, сколько противника стоит на карте, волны — на вопрос, что происходит
/// в этот момент. Величины подбираются порознь и по разным признакам, а держать их
/// в одном ресурсе значило бы подбирать вместе.
/// </summary>
[GlobalClass]
public partial class WaveSettings : Resource
{
    // ── Ход времени ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Постоянная отдыха между волнами, секунд. Волна умножает её своим
    /// <see cref="WaveDefinition.ChillIntervalMultiplier"/>, поэтому здесь задаётся
    /// обиходный случай, а не наибольший и не наименьший.
    /// </summary>
    [Export] public float ChillInterval = 90f;

    /// <summary>Отсрочка первой волны — время развернуть базу и встретить фон.</summary>
    [Export] public float FirstDelay = 120f;

    /// <summary>
    /// Доля разброса интервала отдыха. Полученный интервал сдвигается на случайную величину
    /// в пределах этой доли в обе стороны.
    ///
    /// Разброс нужен затем, чтобы игрок готовился к волне, а не жил по секундомеру: точный
    /// интервал превращает подготовку в расчёт, тогда как приблизительный требует держать
    /// запас прочности. Отдельная волна может задать свою долю, если ей нужна другая.
    /// </summary>
    [Export] public float ChillIntervalOffset = 0.1f;

    // ── Умолчания формы появления ─────────────────────────────────────────────────

    [ExportGroup("Форма появления")]

    /// <summary>Угол ближней к базе дуги, градусов.</summary>
    [Export] public float NearArcDegrees = 30f;

    /// <summary>Угол дальней дуги, градусов.</summary>
    [Export] public float FarArcDegrees = 40f;

    /// <summary>Положение ближней дуги в долях радиуса появления фона.</summary>
    [Export] public float RadiusOffsetMultiplier = 1f;

    /// <summary>Глубина формы в долях радиуса появления фона.</summary>
    [Export] public float RadiusDepthMultiplier = 0.12f;

    /// <summary>
    /// Промежуток между соседями и между рядами, в клетках. Он же задаёт плотность
    /// наступления: чем меньше, тем теснее строй.
    ///
    /// Меряется между центрами мест, поэтому нижняя граница определяется самым крупным
    /// видом: при радиусе корпуса в 0.45 клетки безопасный минимум составляет 0.9.
    /// </summary>
    [Export] public float SpacingCells = 1f;

    /// <summary>Число очагов появления. Единица — волна приходит с одной стороны.</summary>
    [Export] public int Groups = 1;

    /// <summary>Угловое расстояние между соседними очагами, градусов.</summary>
    [Export] public float GroupsArcDegrees = 90f;

    /// <summary>
    /// Форма волны: заданное волной поверх умолчаний. Волна описывает отличия, а не
    /// повторяет весь набор, поэтому здесь и происходит слияние.
    /// </summary>
    public WaveShape ShapeOf(WaveShapeOverrides over)
    {
        over ??= new WaveShapeOverrides();

        return new WaveShape(
            over.NearArcDegrees ?? NearArcDegrees,
            over.FarArcDegrees ?? FarArcDegrees,
            over.RadiusOffsetMultiplier ?? RadiusOffsetMultiplier,
            over.RadiusDepthMultiplier ?? RadiusDepthMultiplier,
            over.SpacingCells ?? SpacingCells,
            over.Groups ?? Groups,
            over.GroupsArcDegrees ?? GroupsArcDegrees);
    }

    /// <summary>Интервал отдыха после волны, с её множителем и с разбросом.</summary>
    public float ChillAfter(WaveDefinition wave, RandomNumberGenerator rng)
    {
        float interval = Mathf.Max(ChillInterval, 1f) * Mathf.Max(wave.ChillIntervalMultiplier, 0f);
        float offset = Mathf.Clamp(wave.ChillIntervalOffset ?? ChillIntervalOffset, 0f, 1f);

        return Mathf.Max(interval * (1f + rng.RandfRange(-offset, offset)), 1f);
    }
}
