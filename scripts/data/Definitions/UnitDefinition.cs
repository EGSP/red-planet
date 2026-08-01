using System.Collections.Generic;
using Godot;

/// <summary>Какой класс поднимает Spawner. Поведение задаётся кодом, числа — определением.</summary>
public enum UnitClass
{
    /// <summary>Бот игрока: ходит, сам ищет работу, приказы игрока не слушает.</summary>
    Bot,

    /// <summary>Коммандер: единственный, кто слушает приказы, и единственный неуязвимый.</summary>
    Commander,

    /// <summary>Постройка без собственного поведения: склад, генератор, стена.</summary>
    Structure,

    /// <summary>
    /// Переработка: жжёт энергию и даёт метал. Так устроены и синтезатор, и экстрактор —
    /// механика у них одна, а различаются они курсом и тем, что экстрактор требует
    /// под собой точку метала.
    /// </summary>
    Factory,

    /// <summary>Турель: постройка со стволом и вращающейся башней.</summary>
    Turret,

    /// <summary>Башня-сборщик: строит и чинит в радиусе обзора, с места не сходит.</summary>
    Assembler,

    /// <summary>Противник.</summary>
    Enemy,
}

/// <summary>Что нужно, чтобы сущность построить. Секции нет — сущность не строится вовсе.</summary>
public sealed class AssemblyDefinition
{
    /// <summary>
    /// Полная стоимость в метале. Она же объём работы: время стройки равно стоимости,
    /// делённой на суммарную мощность строителей, как в PA.
    /// </summary>
    public float CostMetal;

    /// <summary>
    /// Прочность каркаса, пока идёт стройка. Отдельным числом от прочности готовой сущности:
    /// раньше оба значения назывались MaxHealth и лежали в разных файлах одного юнита,
    /// из-за чего по файлам было не понять, что это две разные величины.
    ///
    /// Не задано — берётся прочность готовой сущности. Так и было у построек, и повторять
    /// одно число в каждом файле незачем: своё значение нужно там, где каркас крепче
    /// или слабее того, что из него получится.
    /// </summary>
    public float FrameHealth;
}

/// <summary>Переработка одного ресурса в другой: сейчас это только синтезатор метала.</summary>
public sealed class ConversionDefinition
{
    /// <summary>Сколько энергии в секунду потребляет при полной производительности.</summary>
    public float EnergyDrain;

    /// <summary>Сколько метала в секунду выдаёт при полной производительности.</summary>
    public float MetalOutput;
}

/// <summary>
/// Определение сущности мира: бота, постройки или противника. Одно на всех, потому что
/// разница между ними — это набор заполненных секций, а не разные виды данных.
///
/// РАНЬШЕ БЫЛО ТРИ ОПРЕДЕЛЕНИЯ И РАЗЪЕЗЖАЛИСЬ. Фабрикатор описывался тремя файлами:
/// BuildableDef с ценой и формой, UnitDef с ходовыми числами и сценой, которая их связывала.
/// Имя и цвет были записаны дважды дословно, а ключ MaxHealth в двух файлах означал разное —
/// прочность каркаса и прочность собранного юнита. Здесь и то, и другое названо своими
/// именами: MaxHealth и Assembly.FrameHealth.
///
/// СЕКЦИИ ВМЕСТО ПРИЗНАКОВ. Признака «это юнит» больше нет: сущность занимает клетки,
/// если у неё задана форма (Rows), и строится, если задана секция сборки. Роль выводится
/// из того, чем сущность снабжена, а не из отдельного переключателя, который можно
/// выставить вразрез с остальными полями.
///
/// Размеры и скорости заданы в юнитах мира (1 юнит = клетка), перевод в пиксели —
/// свойства с суффиксом Px.
/// </summary>
public sealed class UnitDefinition
{
    public string Id = "";
    public string DisplayName = "";

    public UnitClass Class = UnitClass.Structure;

    /// <summary>Категории, которыми определение помечает себя. См. TagRegistry.</summary>
    public TagSet Tags = TagSet.Empty;

    public Color Color = new(0.6f, 0.6f, 0.65f);

    public float MaxHealth = 100f;

    /// <summary>Радиус корпуса подвижной сущности в юнитах: и рисуется по нему, и попадают в него.</summary>
    public float Radius = 0.35f;

    /// <summary>Радиус обзора в юнитах. Он же рабочая зона для башни-сборщика.</summary>
    public float VisionRange = 8f;

    /// <summary>Скорость хода в юнитах в секунду. Ноль — сущность неподвижна.</summary>
    public float Speed;

    /// <summary>Скорость вращения корпуса или башни в градусах в секунду.</summary>
    public float TurnSpeedDegrees = 180f;

    /// <summary>
    /// Форма в клетках: '#' — занятая клетка, '.' — пустая. ["##", "##"] — квадрат 2x2,
    /// ["###", ".#."] — Т-образная форма. Пусто — сущность клеток не занимает, она ходит.
    /// </summary>
    public string[] Rows = System.Array.Empty<string>();

    /// <summary>
    /// Куда смотрит неподвижная сущность, в градусах (0 — вправо). Здание ноду не крутит:
    /// визуал привязан к клеткам сетки. Ось нужна ради единого правила — направление
    /// есть у любой сущности мира.
    /// </summary>
    public float FacingDegrees;

    /// <summary>
    /// Ставится только на клетку с точкой метала. Признак принадлежит месту, а не постройке,
    /// поэтому и живёт рядом с формой: и то, и другое отвечает на вопрос «куда это встанет».
    /// Проверяет его WorldGrid.CanPlace, причём в обе стороны — постройка без этого признака
    /// на точку не встанет вовсе, чтобы не занять её безвозвратно.
    /// </summary>
    public bool RequiresMetalSpot;

    /// <summary>
    /// Чем сущность снабжена, ссылками из файла. Строки живут только до сборки:
    /// компилятор превращает их в Tools и больше к ним не возвращается.
    /// </summary>
    public string[] ToolIds = System.Array.Empty<string>();

    /// <summary>Чем сущность снабжена: ствол, строительная рука, бур. Заполняет компилятор.</summary>
    public ToolDefinition[] Tools = System.Array.Empty<ToolDefinition>();

    /// <summary>Как построить. Null — сущность в мире появляется помимо стройки.</summary>
    public AssemblyDefinition Assembly;

    /// <summary>Переработка ресурсов. Null — сущность ничего не перерабатывает.</summary>
    public ConversionDefinition Conversion;

    /// <summary>Собственное производство, единиц в секунду.</summary>
    public float EnergyProduction;

    public float MetalProduction;

    /// <summary>Насколько сущность поднимает потолок хранилища.</summary>
    public float MetalStorage;

    public float EnergyStorage;

    /// <summary>
    /// Идентификатор строительной панели из resources/buildbars/. Пусто у того, кто строить
    /// не умеет; у строителя панель и есть ответ на вопрос «что ему доступно» — отдельного
    /// списка построек определение не держит.
    /// </summary>
    public string Buildbar = "";

    /// <summary>
    /// Вклад постройки в экспансию — до умножения на коэффициент зоны удалённости.
    /// Не задано — выводится из рода: постройка весит единицу, подвижная сущность ноль.
    ///
    /// Задаётся там, где умолчание неверно. Забор весит около 0.05: их много, места они
    /// занимают столько же, сколько завод, а угрозы не несут — и без отдельного веса
    /// периметр из заборов раздувал бы показатель на ровном месте.
    /// </summary>
    public float? ExpansionWeight;

    /// <summary>
    /// Вклад сущности в мощь армии. Не задано — единица у подвижного со стволом, ноль
    /// у всех прочих: строитель армией не является, а турель считается по экспансии,
    /// иначе она вошла бы в показатель дважды.
    /// </summary>
    public float? ArmyWeight;

    /// <summary>
    /// Доля вида в спавне противника относительно остальных. Ноль — вид в игре есть,
    /// но сам не заводится: пригодится для боссов и скриптовых волн.
    /// </summary>
    public float SpawnWeight;

    /// <summary>
    /// На какой доле дальности оружия противник останавливается. Меньше единицы,
    /// чтобы цель не выпадала из радиуса от любого шага в сторону.
    /// </summary>
    public float StandoffFraction = 0.75f;

    /// <summary>
    /// Род при выделении рамкой. Выводится компилятором из тега structure, а не задаётся
    /// в файле: род — следствие того, чем сущность является, и расходиться с тегами он не должен.
    /// </summary>
    public SelectionGroup SelectionGroup = SelectionGroup.Bots;

    /// <summary>Ствол. Найден компилятором среди инструментов, здесь лежит готовым.</summary>
    public WeaponDefinition Weapon;

    /// <summary>Строительная рука. Ею же идёт ремонт.</summary>
    public WorkToolDefinition BuildTool;

    public bool CanBuild => BuildTool != null;

    /// <summary>Чинит тот же инструмент, что и строит: отдельной ремонтной мощности нет.</summary>
    public bool CanRepair => BuildTool != null;

    /// <summary>Берётся ли строитель за юнитов, а не только за постройки.</summary>
    public bool CanRepairUnits => BuildTool is { RepairsUnits: true };

    /// <summary>
    /// На какое расстояние юнит вообще дотягивается работой. Нужно там, где занятие ещё
    /// не выбрано, — например, безоружному, которому приказали атаковать: подойти он должен
    /// хотя бы на длину того, чем располагает.
    /// </summary>
    public float WorkRangePx => BuildTool?.RangePx ?? Const.Unit;

    /// <summary>
    /// Постройка или подвижная сущность. Выводится из класса, и только из него: тот же
    /// вопрос решает Spawner, выбирая, какой узел поднять, и два ответа обязаны совпадать.
    ///
    /// По форме это определять НЕЛЬЗЯ, хотя соблазн есть. Форма нужна и юниту: его каркас
    /// занимает клетку, пока идёт сборка, иначе на одно место встанет десяток каркасов,
    /// да и приказать «достроить вот этот» будет некому — цель под курсором ищется
    /// по занятости сетки. Клетку юнит освобождает, когда собран, а не когда поставлен.
    /// </summary>
    public bool IsStructure => Class is UnitClass.Structure or UnitClass.Factory
        or UnitClass.Turret or UnitClass.Assembler;

    /// <summary>Занимает ли сущность клетки: у каркаса — на время стройки, у постройки — всегда.</summary>
    public bool OccupiesCells => Rows.Length > 0;

    public bool IsMobile => Speed > 0f;

    /// <summary>
    /// Вес в экспансии с подставленным умолчанием. Считается при обращении, а не при сборке:
    /// умолчание зависит от рода, а род известен только после связывания определения.
    /// </summary>
    public float TerrorExpansion => ExpansionWeight ?? (IsStructure ? 1f : 0f);

    /// <summary>Вес в мощи армии с подставленным умолчанием.</summary>
    public float TerrorArmy => ArmyWeight ?? (IsMobile && Weapon != null ? 1f : 0f);

    public int Width => Rows.Length > 0 ? Rows[0].Length : 1;

    public int Height => Rows.Length > 0 ? Rows.Length : 1;

    public Vector2I Size => new(Width, Height);

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public float RadiusPx => Radius * Const.Unit;

    public float VisionRadiusPx => VisionRange * Const.Unit;

    public float SpeedPx => Speed * Const.Unit;

    /// <summary>Полный объём работы. Меряется металом: мощность строителя — это метал в секунду.</summary>
    public float TotalWork => Assembly?.CostMetal ?? 0f;

    public bool IsGenerator => EnergyProduction > 0f;

    /// <summary>
    /// Сколько прочности даёт единица метала при ремонте. Курс не выдуманный: он выведен
    /// из самой сущности, поэтому починить её с нуля стоит ровно столько же, сколько
    /// построить, и занимает столько же времени — инструмент-то один и тот же.
    ///
    /// Ноль означает «ремонту не подлежит»: у сущности без цены курса быть не может.
    /// Так отсекается коммандер, которого никто не строил.
    /// </summary>
    public float HealthPerMetal => TotalWork > 0f ? MaxHealth / TotalWork : 0f;

    public bool CanBeRepaired => HealthPerMetal > 0f;

    /// <summary>Прочность каркаса на время стройки.</summary>
    public float FrameHealth => Assembly?.FrameHealth ?? 100f;

    /// <summary>Клетки, занимаемые сущностью при начале в origin.</summary>
    public IEnumerable<Vector2I> Cells(Vector2I origin)
    {
        for (int y = 0; y < Rows.Length; y++)
        {
            string row = Rows[y];
            for (int x = 0; x < row.Length; x++)
                if (row[x] == '#')
                    yield return new Vector2I(origin.X + x, origin.Y + y);
        }
    }
}
