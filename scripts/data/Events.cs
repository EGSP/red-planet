using Godot;

/// <summary>
/// Вид ресурса. Названо Kind, потому что слово Resource занято классом Godot.
///
/// Ресурса ровно два, и роли у них разные. Метал — материал: из него состоят постройки,
/// он копится и тратится. Энергия — не материал, а пропускная способность: она ничего
/// не образует, но без неё метал не потратить. Так же устроена экономика PA.
/// </summary>
public enum ResourceKind
{
    Metal,
    Energy,
}

/// <summary>Ресурс поступил в общее хранилище базы.</summary>
[TransientEvent]
public struct ResourceGained : IEventRecord
{
    public int SequenceId { get; set; }
    public ResourceKind Kind;
    public float Amount;
}

/// <summary>Ресурс списан из общего хранилища.</summary>
[TransientEvent]
public struct ResourceSpent : IEventRecord
{
    public int SequenceId { get; set; }
    public ResourceKind Kind;
    public float Amount;
}

/// <summary>
/// Размечен план постройки — намерение игрока, из которого потом вырастет каркас.
///
/// Отдельно от <see cref="BlueprintPlaced"/> намеренно: план и каркас появляются в разное
/// время и по разным поводам. План — по щелчку игрока, каркас — когда исполнитель дошёл
/// до места. План может не стать каркасом вовсе, если место к тому времени заняли.
/// </summary>
[TransientEvent]
public struct BuildPlanned : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2 Pos;
    public float Facing;
}

/// <summary>
/// План снят: место к приходу исполнителя оказалось занято, либо игрок отказался от
/// приказа строить и план остался без нацеленных. Очередь идёт дальше сама, а след
/// остаётся здесь: молчаливое исчезновение размеченного иначе выглядело бы отказом
/// без причины.
/// </summary>
[TransientEvent]
public struct BuildPlanCancelled : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2 Pos;
}

/// <summary>Поставлен каркас будущей постройки.</summary>
[TransientEvent]
public struct BlueprintPlaced : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника, а не ссылка на ресурс — так запись переживёт сохранение.</summary>
    public string DefinitionId;

    /// <summary>Центр занятого места. Клеток больше нет: постановка свободная.</summary>
    public Vector2 Pos;

    /// <summary>Угол, под которым каркас поставили, в радианах.</summary>
    public float Facing;
}

/// <summary>
/// Постройка появилась в мире. Публикуется фабрикой сущностей — единственным местом,
/// где постройки создаются, поэтому документ ловит их все: и достроенные каркасом,
/// и те, что когда-либо будут выставлены в мир сразу, помимо стройки.
///
/// Отдельно от ConstructionCompleted намеренно: та запись про то, что стройка ЗАВЕРШЕНА,
/// а эта — про то, что сущность ЕСТЬ. Ёмкость хранилища зависит от второго, а не от первого.
/// </summary>
[TransientEvent]
public struct BuildingSpawned : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2 Pos;

    /// <summary>Угол корпуса в радианах: постройка, восстановленная по документам, стоит так же.</summary>
    public float Facing;
}

/// <summary>Каркас достроен и превратился в готовую сущность.</summary>
[TransientEvent]
public struct ConstructionCompleted : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;
    public string DefinitionId;
    public Vector2 Pos;
}

/// <summary>
/// Ствол выстрелил. Документ отделён от рождения снаряда намеренно: снаряд есть предмет
/// симуляции, а выстрел — факт, на который откликаются показ и звук, и откликаться им
/// нужно на выстрел любого рода, включая тот, что снаряда не порождает.
/// </summary>
[TransientEvent]
public struct WeaponFired : IEventRecord
{
    public int SequenceId { get; set; }

    /// <summary>Кто выстрелил.</summary>
    public int EntityId;

    /// <summary>Ключ справочника инструмента: у носителя стволов бывает несколько.</summary>
    public string ToolId;

    /// <summary>Срез ствола в мировых координатах.</summary>
    public Vector2 Pos;

    /// <summary>Направление выстрела в радианах, уже с учётом разброса.</summary>
    public float Facing;
}

/// <summary>
/// Снаряд задел цель. Прочность правит не снаряд, а DamageSystem по этому документу:
/// так у каждого попадания есть след, и никто не удаляет ноду посреди чужого обхода.
/// </summary>
[TransientEvent]
public struct DamageDealt : IEventRecord
{
    public int SequenceId { get; set; }
    public int TargetId;
    public int SourceId;

    /// <summary>
    /// Ключ справочника инструмента, которым нанесён урон. Пусто у урона, у которого
    /// инструмента нет вовсе, — у сноса и у взрыва области.
    ///
    /// Читает поле только показ: вид вспышки попадания принадлежит оружию, а у носителя
    /// стволов бывает несколько. То же поле и по той же надобности есть
    /// у <see cref="WeaponFired"/>.
    /// </summary>
    public string ToolId;

    public float Amount;
    public Vector2 Pos;

    /// <summary>
    /// Направление полёта снаряда в момент попадания, в радианах. Нужно показу:
    /// вспышка попадания разворачивается против хода снаряда, а не рисуется одинаково
    /// с любой стороны.
    /// </summary>
    public float Facing;

    /// <summary>
    /// Попадание досталось от взрыва, а не от прямого выстрела. Читает это только показ:
    /// у взрыва есть собственный эффект в его середине, и вспышка на каждой задетой цели
    /// сверх него означала бы десяток вспышек на один разрыв.
    ///
    /// На расчёт признак не влияет никак: урон есть урон, кем бы он ни был нанесён.
    /// </summary>
    public bool FromSplash;
}

/// <summary>
/// Требование раздать урон по области. Публикует его тот, кто область породил, — снаряд
/// при попадании, а впредь и топчущий юнит, и движущаяся зона огнемёта; разворачивает
/// требование в отдельные попадания <c>SplashSystem</c>.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ДОКУМЕНТ, А НЕ ПЕРЕБОР ЦЕЛЕЙ ПРЯМО В СНАРЯДЕ. Во-первых, поиск целей
/// вокруг точки не имеет отношения к полёту: снаряд отвечает за то, куда он летит и во что
/// уткнулся, а раздача урона есть отдельное действие, у которого свои правила отбора.
/// Во-вторых, повод бывает не только у снаряда, и все они выражаются одним документом.
/// В-третьих, у взрыва остаётся собственный след в журнале: по нему видно и то, где рвануло,
/// и то, кому сколько досталось, поскольку сам урон уходит обычными <see cref="DamageDealt"/>.
/// </summary>
[TransientEvent]
public struct SplashRequested : IEventRecord
{
    public int SequenceId { get; set; }

    /// <summary>Кто причина взрыва. Уходит в документы попаданий как источник урона.</summary>
    public int SourceId;

    /// <summary>
    /// Ключ инструмента, породившего взрыв. Пусто у взрыва без оружия. Читает поле только
    /// показ: у оружия со своей вспышкой попадания взрыв отдельным эффектом не показывается,
    /// иначе в одной точке играются два эффекта разом.
    /// </summary>
    public string ToolId;

    /// <summary>
    /// Сторона ВИНОВНИКА, а не жертвы. Стороны жертв выводятся из неё: чужая задевается
    /// всегда, своя — только при <see cref="FriendlyFire"/>.
    /// </summary>
    public Faction Side;

    /// <summary>Задевает ли область своих. Свойство оружия, а не общее правило игры.</summary>
    public bool FriendlyFire;

    /// <summary>Середина области в мировых координатах.</summary>
    public Vector2 Pos;

    /// <summary>Радиус области в пикселях. Дальше него не достаётся никому.</summary>
    public float Radius;

    /// <summary>
    /// Урон в середине области. К краю он убывает по общей кривой
    /// (<see cref="CombatSettings.SplashFalloff"/>), поэтому это верхняя граница, а не
    /// то, что получит каждый.
    /// </summary>
    public float Amount;

    /// <summary>
    /// Кто уже получил прямое попадание и потому в раздачу не входит: иначе цель, в которую
    /// снаряд угодил, получала бы и полный урон снаряда, и урон области сверх него.
    /// Ноль означает, что таких нет, — так будет у взрыва без прямой цели.
    /// </summary>
    public int DirectId;
}

/// <summary>Прочность кончилась — сущность выбыла из игры.</summary>
[TransientEvent]
public struct EntityDestroyed : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника: по нему хранилище узнаёт, сколько ёмкости ушло вместе с постройкой.</summary>
    public string DefinitionId;

    public Faction Side;
    public Vector2 Pos;
}

/// <summary>
/// Замер наблюдаемой величины на момент времени. Документ транзиентный, как и все прочие:
/// хранит ряд не журнал, а <see cref="TimeSeriesProjection"/>.
///
/// ЭТО НЕ ФАКТ, А СРЕЗ, и разница существенна. Прочие документы отвечают на вопрос
/// «что случилось», этот — на вопрос «сколько было в такой-то момент». В терминах 1С
/// первое есть документ, второе — периодический регистр сведений. Смешать их значило бы
/// превратить журнал из шины в хранилище, а он для этого не предназначен: чистится каждый
/// кадр и растёт без предела, если чистку отключить.
/// </summary>
[TransientEvent]
public struct MetricSampled : IEventRecord
{
    public int SequenceId { get; set; }

    /// <summary>Имя ряда: terror.raw, terror.production, income.metal.</summary>
    public string Channel;

    public float Value;

    /// <summary>
    /// Номер замера от начала партии. Он же отметка времени: шаг постоянен, поэтому
    /// хранить секунды отдельно незачем — время равно номеру, умноженному на шаг.
    /// </summary>
    public int Tick;
}

/// <summary>
/// Волна отобрана и набрана. Записывается НАМЕРЕНИЕ: чем подсистема руководствовалась
/// и что решила выставить.
///
/// Факт отдельным событием не пишется: каждая созданная сущность и без того попадает
/// в <see cref="EnemySpawned"/> с признаком <see cref="PressureOrigin.Wave"/>, откуда
/// видны и вид, и точка появления. Разделение всё равно сохраняется — намерение и факт
/// расходятся, когда набор не потратил бюджет целиком.
/// </summary>
[TransientEvent]
public struct WaveStarted : IEventRecord
{
    public int SequenceId { get; set; }

    /// <summary>Ключ справочника волн.</summary>
    public string WaveId;

    /// <summary>Сглаженный террор, по которому шёл отбор и считался бюджет.</summary>
    public float Terror;

    /// <summary>Бюджет волны в единицах боевой мощи.</summary>
    public float Budget;

    /// <summary>Сколько мощи набор израсходовал. Меньше бюджета — остаток некому было занять.</summary>
    public float Spent;

    /// <summary>Состав перечислением видов с количествами.</summary>
    public string Composition;

    /// <summary>Направление первого очага от точки высадки, градусов.</summary>
    public float CenterAngleDegrees;

    public int Groups;

    /// <summary>Назначенный отдых до следующей волны, секунд. С множителем и разбросом.</summary>
    public float ChillSeconds;
}

/// <summary>Враг вышел на карту.</summary>
[TransientEvent]
public struct EnemySpawned : IEventRecord
{
    public int SequenceId { get; set; }
    public int EntityId;

    /// <summary>Ключ справочника, а не ссылка на ресурс — так запись переживёт сохранение.</summary>
    public string DefinitionId;

    public Vector2 Pos;

    /// <summary>Фон или волна. По разбору партии видно, чем именно было создано напряжение.</summary>
    public PressureOrigin Origin;
}
