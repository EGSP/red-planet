using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// Как подвижная сущность попадает туда, куда её послали: следование по пути, локальный
/// обход соседей и жёсткие ограничения.
///
/// ТРИ СЛОЯ, И ПОРЯДОК МЕЖДУ НИМИ ЗНАЧИМ.
/// Первый — глобальный: <see cref="PathfindingSystem"/> даёт направление, обходящее здания.
/// Второй — локальный: силы boids правят курс, чтобы сущности не слипались и расходились
/// со встречными. Третий — ограничения: выталкивание из зданий и расталкивание перекрывшихся.
/// Третий слой применяется ПОСЛЕ интегрирования и решает задачу достоверно, тогда как
/// сила решала бы её вероятностно. При перекрытии свой идущий полностью смещает своего
/// стоящего — это и есть проталкивание сквозь союзников. Физического движка в проекте
/// нет, поэтому оба ограничения написаны здесь руками.
///
/// КУДА СУЩНОСТЬ ЕДЕТ. Не туда, куда её просят, а туда, куда повёрнут корпус: рулевой
/// вектор задаёт лишь угол, к которому корпус доворачивается со своей скоростью, а скорость
/// хода направлена по оси корпуса и урезана оставшимся расхождением
/// (<see cref="UnitDefinition.SpeedFactorFor"/>). Отсюда берутся дуга на повороте и разворот
/// на месте перед движением назад; отсюда же следует, что скорость вращения корпуса —
/// величина игровая, а не украшение. Оттуда же берётся и обратное правило: ход ограничен
/// так, чтобы цель не попадала внутрь окружности разворота (<see cref="Turning"/>).
///
/// ПЕРВЫЙ СЛОЙ ОТКЛЮЧАЕМ. Намерение бывает двух видов: строгое идёт по найденному пути,
/// свободное (<see cref="Movement.Fluid"/>) — прямо на цель, оставляя всё расхождение
/// локальному слою. Второй и третий слои от этого не меняются, поэтому различие сводится
/// к одному условию: запрашивать путь или не запрашивать.
///
/// ЧЕГО СИСТЕМА НЕ ДЕЛАЕТ. Не выбирает цель и не завершает приказы: обработчик приказа
/// каждый кадр объявляет намерение через <see cref="Movement.Seek"/>, а сам решает,
/// дошёл ли исполнитель. Не подтверждённое намерение гаснет в конце шага.
/// </summary>
public partial class MovementSystem : GameSystem
{
    /// <summary>
    /// Во сколько радиусов сущность замечает соседей.
    ///
    /// Совпадает с <see cref="PushSense"/> намеренно: обход соседей один на всех
    /// потребителей, и его радиус есть большее из чутья и зоны расхождения. Зона
    /// расхождения меньше двух с половиной радиусов быть не может — иначе сосед,
    /// с которым корпуса уже пересеклись, останется незамеченным, — поэтому чутьё
    /// ниже этой величины перебора не сокращает, а лишь ослабляет локальный слой.
    /// </summary>
    [Export] public float SenseFactor = 3.5f;

    /// <summary>
    /// Сколько соседей учитывается, прочие в этом шаге пропускаются. Ноль и меньше
    /// означают «всех».
    ///
    /// ОТБОР ПРОИЗВОЛЕН. Обход идёт по корзинам сетки, то есть в порядке раскладки,
    /// а не по близости, поэтому предел отбрасывает не далёких, а случайных. Величина
    /// заведена для опыта: она показывает, насколько поведение толпы вообще зависит
    /// от полноты выборки, и насколько дорого обходится эта полнота.
    /// </summary>
    [Export] public int NeighbourLimit = 1;

    [Export] public float AvoidWeight = 1.4f;

    [Export] public float AlignWeight = 0.35f;

    /// <summary>Вес отклонения от стен. Заведомо больше веса обхода соседей: см. <see cref="Walls"/>.</summary>
    [Export] public float WallWeight = 2.2f;

    /// <summary>
    /// На сколько радиусов корпуса сущность начинает отклоняться от стены. Расстояние
    /// меряется от поверхности строения до края корпуса, а не до его середины.
    /// </summary>
    [Export] public float WallMargin = 2.2f;

    /// <summary>
    /// Насколько соль меняет веса локального слоя, долей от веса. Четверть означает,
    /// что крайние по соли сущности расходятся в весах в полтора раза.
    /// </summary>
    [Export] public float SaltWeight = 0.25f;

    /// <summary>
    /// Насколько соль сдвигает порог выбора стороны обхода. Величина сравнивается
    /// с векторным произведением единичных векторов, поэтому лежит в тех же пределах,
    /// что и оно: единица означала бы, что сторона выбрана солью и только ею.
    /// </summary>
    [Export] public float SaltSide = 0.2f;

    /// <summary>
    /// Во сколько сумм радиусов простирается мягкая зона расталкивания. Внутри неё сущности
    /// начинают расходиться ЕЩЁ ДО касания, слабой добавкой к сдвигу.
    ///
    /// ЗАЧЕМ. Жёсткий порог по сумме радиусов есть ступенька: сосед либо не влияет вовсе,
    /// либо влияет сразу заметно. Ступенька видна рывком на том кадре, где перекрытие
    /// возникло, и она же делает исход чувствительным к тому, что положения соседей взяты
    /// на начало шага. Мягкая зона снимает и то, и другое ценой небольшого постоянного
    /// зазора между стоящими вплотную.
    /// </summary>
    [Export] public float SoftMargin = 1.25f;

    /// <summary>Доля сдвига в мягкой зоне: во сколько раз слабее жёсткого разведения.</summary>
    [Export] public float SoftPush = 0.25f;

    /// <summary>
    /// Во сколько радиусов корпуса сущность ищет тех, с кем расталкивается. Величина
    /// покрывает мягкую зону пары равных: сумма радиусов есть два радиуса, мягкая зона
    /// растягивает её ещё на четверть.
    /// </summary>
    [Export] public float PushSense = 2.5f;

    /// <summary>
    /// Ниже какого сдвига за шаг расхождение не применяется, пикселей. Порог общий
    /// для расхождения и для удержания позиции: <c>Movement.Drift</c> ниже него равен нулю,
    /// и удержание видит, что юнита не двигали, — см. <see cref="Settle"/>.
    /// </summary>
    [Export] public float PushFloor = 0.5f;

    /// <summary>Сколько секунд без продвижения считается застреванием.</summary>
    [Export] public float StuckTimeout = 1.5f;

    /// <summary>
    /// Сколько секунд ждать выезда из корпуса завода, прежде чем телепортировать
    /// юнита на ближайшую проходимую клетку у точки rolloff.
    /// </summary>
    [Export] public float ExitTimeout = 5f;

    private readonly List<IMobile> _actors = new();
    private readonly List<Obb> _walls = new();
    private readonly BoidSalt _salt = new();

    /// <summary>
    /// Снимок скоростей и положений на начало шага, по порядковым номерам из <see cref="Collect"/>.
    ///
    /// ЗАЧЕМ СНИМОК. Обход соседей читает их скорости — выравнивание складывает их, а
    /// расталкивание предсказывает по ним положение к концу шага. Скорость же назначается
    /// в этом самом обходе, поэтому без снимка сущность видела бы у соседей с меньшим номером
    /// новые скорости, а у прочих — старые, и исход зависел бы от порядка обхода. Снимок
    /// делает фазу порядко-независимой, а тем самым и пригодной к разбиению по потокам.
    ///
    /// Положения в этой фазе никто не правит, поэтому они читаются прямо у сущностей;
    /// снимок положений нужен лишь для того, чтобы в конце шага измерить пройденное.
    /// </summary>
    private Vector2[] _velocity = System.Array.Empty<Vector2>();

    private Vector2[] _before = System.Array.Empty<Vector2>();

    /// <summary>Предсказанное к концу шага положение — по нему меряется перекрытие.</summary>
    private Vector2[] _ahead = System.Array.Empty<Vector2>();

    /// <summary>Радиус корпуса. У юнита это свойство идёт через определение, здесь — поле.</summary>
    private float[] _radius = System.Array.Empty<float>();

    /// <summary>
    /// Собственная доля расхождения, накопленная за обход соседей. Применяется после
    /// интегрирования, в <see cref="Settle"/>.
    /// </summary>
    private Vector2[] _push = System.Array.Empty<Vector2>();

    /// <summary>Сколько слотов соли уже роздано. По нему назначается очередной.</summary>
    private int _salted;

    /// <summary>
    /// Сколько соседей нашлось в зоне — сумма, наибольшее и число спросивших за шаг.
    /// Нужно подбору <see cref="NeighbourLimit"/>: без этих чисел предел ставится наугад,
    /// поскольку кандидатов из корзин впятеро больше, чем соседей в самой зоне.
    /// </summary>
    private int _senseSum;

    private int _senseMax;

    private int _senseSeen;

    private PathfindingSystem _pathfinding;
    private int _active;
    private int _settled;
    private int _blocked;
    private int _leaving;

    protected override void OnLink() => _pathfinding = GM.System<PathfindingSystem>();

    public override void Step(double dt)
    {
        Collect(dt);

        _senseSum = 0;
        _senseMax = 0;
        _senseSeen = 0;

        // Обход соседей один на шаг: в нём считаются и силы локального слоя, и собственная
        // доля расхождения. Порядок фаз — см. Steer и Settle
        for (int i = 0; i < _actors.Count; i++)
            Steer(i, dt);

        for (int i = 0; i < _actors.Count; i++)
            Integrate(_actors[i], dt);

        for (int i = 0; i < _actors.Count; i++)
            Settle(i, dt);

        // Счётчики снимаются до очистки Active: после неё диагностический снимок уже
        // не смог бы отличить двигавшиеся сущности от удерживавших позицию.
        _active = 0;
        _settled = 0;
        _blocked = 0;
        _leaving = 0;

        foreach (var actor in _actors)
        {
            var movement = actor.Movement;

            if (movement.Active)
                _active++;

            if (movement.Settled)
                _settled++;

            if (movement.Blocked)
                _blocked++;

            if (movement.Leaving)
                _leaving++;

            // Намерение живёт один кадр: подтвердит его обработчик приказа — сущность
            // пойдёт дальше, не подтвердит — она сама собой станет удерживающей позицию.
            actor.Movement.Active = false;
        }
    }

    /// <summary>
    /// Список подвижных в порядке обхода. Пересобирается каждый кадр.
    ///
    /// Раскладку по клеткам система больше не ведёт: она общая на весь мир и живёт
    /// в <see cref="WorldSpace"/> — тем же вопросом «кто рядом» пользуется выбор цели.
    /// Здесь остаётся лишь порядковый номер, по которому пара сущностей расталкивается
    /// один раз, а не дважды.
    /// </summary>
    private void Collect(double dt)
    {
        _actors.Clear();
        GM.Space.ReadyMobiles();

        foreach (var mobile in GM.Index.All<IMobile>())
        {
            if (mobile.Definition == null)
            {
                // Без определения сущность в обходе не участвует: у неё нет ни скорости,
                // ни габарита. Отрицательный номер выводит её и из соседства
                mobile.Movement.Slot = -1;
                continue;
            }

            // Слот соли назначается при первом появлении, а не при рождении сущности:
            // порядок раздачи от этого не меняется, зато о соли не нужно помнить ни заводу,
            // ни редактору содержимого, ни коду появления волн
            if (mobile.Movement.Salt < 0)
                mobile.Movement.Salt = _salted++ % BoidSalt.Slots;

            mobile.Movement.Slot = _actors.Count;
            _actors.Add(mobile);
        }

        if (_velocity.Length < _actors.Count)
        {
            int size = Mathf.Max(_actors.Count * 2, 64);

            _velocity = new Vector2[size];
            _before = new Vector2[size];
            _ahead = new Vector2[size];
            _radius = new float[size];
            _push = new Vector2[size];
        }

        // Предсказанное положение и радиус кладутся в снимок готовыми, а не считаются
        // в обходе: обход трогает соседа десятки раз за шаг, а сущность здесь — по разу
        for (int i = 0; i < _actors.Count; i++)
        {
            var actor = _actors[i];
            var at = actor.GlobalPosition;
            var velocity = actor.Movement.Velocity;

            _velocity[i] = velocity;
            _before[i] = at;
            _ahead[i] = at + velocity * (float)dt;
            _radius[i] = actor.HitRadius;
            _push[i] = Vector2.Zero;
        }
    }

    // ── управление ────────────────────────────────────────────────────────────────

    private void Steer(int index, double dt)
    {
        var mobile = _actors[index];
        var movement = mobile.Movement;
        var definition = mobile.Definition;

        var position = mobile.GlobalPosition;
        float radius = mobile.HitRadius;

        // Выезд из корпуса — до проверки Active: у только что выпущенного юнита приказа
        // может не быть, и Halt иначе остановил бы его прямо внутри завода.
        //
        // Обход соседей делается и здесь, и у остановленного: локальный слой им не нужен,
        // а расходиться корпусами обязаны все без изъятия
        if (movement.Leaving)
        {
            SteerExit(mobile, movement, definition, dt);
            Scan(index, mobile, position, radius, Vector2.Zero);
            return;
        }

        if (!movement.Active || definition.SpeedPx <= 0f)
        {
            movement.Settled = false;
            Halt(movement);
            Scan(index, mobile, position, radius, Vector2.Zero);
            return;
        }

        // Свободное движение пути не запрашивает вовсе: направление берётся прямо на цель,
        // а разойтись с соседями — дело локального слоя. Забытый кеш чистится сам, по сроку
        // невостребованности, поэтому снимать путь при смене режима не требуется
        var handle = movement.Fluid
            ? null
            : _pathfinding?.Request(mobile, position, movement.Goal, radius);

        // Остаток пути и признак прибытия считаются по ЗАДАННОЙ цели, а не по концу
        // ломаной. Разница существенна для подхода к бою: цель боя — центр постройки,
        // он лежит внутри неё, и путь ведёт к её краю. Мерить остановку по краю значило бы
        // встать на добрую сотню пикселей дальше, чем требует дальность ствола, — ровно
        // тот дефект, когда юнит доходит до линии огня и не стреляет.
        float remaining = position.DistanceTo(movement.Goal) - movement.StopDistance;

        movement.Blocked = handle is { Status: PathStatus.Unreachable };
        movement.Settled = movement.Blocked
                           || remaining <= 0f
                           || Circling(mobile, definition, position, movement, remaining)
                           || Exhausted(handle, position, radius)
                           || Crowded(mobile, position, radius, movement.Goal);

        if (movement.Settled)
        {
            Halt(movement);
            Scan(index, mobile, position, radius, Vector2.Zero);
            return;
        }

        var seek = Follow(handle, position, movement, radius);

        if (seek == Vector2.Zero)
        {
            Halt(movement);
            Scan(index, mobile, position, radius, Vector2.Zero);
            return;
        }

        // Единственный обход соседей за шаг: отсюда берутся обход помех, выравнивание,
        // ослабление курса и доля расхождения
        var sensed = Scan(index, mobile, position, radius, seek);

        var avoid = AvoidForce(movement, seek, in sensed);
        var align = AlignForce(in sensed);
        var wall = Walls(movement, position, seek, radius, remaining);

        movement.SeekForce = seek;
        movement.AvoidForce = avoid;
        movement.AlignForce = align;
        movement.WallForce = wall;
        movement.SeekScale = sensed.Scale;
        movement.Neighbours = sensed.Count;

        // Веса солятся встречными знаками: так различается не сила локального слоя в целом,
        // а СООТНОШЕНИЕ обхода и выравнивания, то есть само поведение в толпе. Одинаковый
        // знак у обоих давал бы просто более резвую и более вялую сущность
        float salt = _salt.Of(movement.Salt) * SaltWeight;

        var steer = seek * movement.SeekScale
                    + avoid * (AvoidWeight * (1f + salt))
                    + align * (AlignWeight * (1f - salt))
                    + wall * WallWeight;

        // Ход ограничен дважды: тормозным путём до цели и радиусом разворота. Первое
        // отвечает за то, чтобы встать в точке, второе — за то, чтобы вообще суметь в неё
        // попасть
        float speed = Mathf.Min(Approach(definition, remaining),
            Turning(mobile, definition, position, movement, remaining));

        var desired = steer.LengthSquared() > 0.000001f
            ? Drive(mobile, definition, steer, speed, dt)
            : Vector2.Zero;

        Accelerate(movement, definition, desired, dt);
    }

    /// <summary>
    /// Перевести рулевой вектор в скорость с учётом того, что сущность едет ТУДА, КУДА
    /// ПОВЁРНУТА, а не туда, куда её просят.
    ///
    /// ЗАЧЕМ НЕГОЛОНОМНАЯ МОДЕЛЬ. Прежде направление скорости бралось прямо из рулевого
    /// вектора, а поворот корпуса лишь догонял её для вида; из этого следовало, что юнит
    /// с медленным разворотом при резкой смене приказа мгновенно ехал боком. Здесь скорость
    /// направлена строго по оси корпуса, поэтому смена курса выходит дугой, а разворот назад
    /// требует сперва развернуться, — и радиус дуги задан не подобранным числом,
    /// а отношением скорости к скорости вращения.
    ///
    /// ДВА ДЕЙСТВИЯ, И ПОРЯДОК МЕЖДУ НИМИ ЗНАЧИМ. Сперва корпус доворачивается к рулевому
    /// вектору, потом от ОСТАВШЕГОСЯ расхождения считается доля хода: иначе сущность
    /// тормозила бы из-за расхождения, которое в этом же кадре уже устранила.
    /// </summary>
    private static Vector2 Drive(IMobile mobile, UnitDefinition definition, Vector2 steer,
        float speed, double dt)
    {
        float wanted = steer.Angle();

        mobile.Rotation = Heading.TurnToward(mobile.Rotation, wanted,
            definition.TurnSpeed * (float)dt);

        float error = Mathf.Abs(Heading.Delta(mobile.Rotation, wanted));
        float factor = definition.SpeedFactorFor(error);

        return factor <= 0f
            ? Vector2.Zero
            : Heading.Forward(mobile.Rotation) * (speed * factor);
    }

    /// <summary>
    /// Принудительный отрезок из корпуса завода к точке rolloff. Путь не ищется,
    /// соседей юнит не обходит: иначе толпа у ворот затолкнула бы его обратно внутрь.
    /// </summary>
    private void SteerExit(IMobile mobile, Movement movement, UnitDefinition definition, double dt)
    {
        movement.ExitFor += (float)dt;
        movement.Settled = false;
        movement.Blocked = false;
        movement.SeekForce = Vector2.Zero;
        movement.AvoidForce = Vector2.Zero;
        movement.AlignForce = Vector2.Zero;
        movement.WallForce = Vector2.Zero;
        movement.SeekScale = 1f;
        movement.Neighbours = 0;
        movement.AvoidSide = 0;
        movement.WallSide = 0;

        if (definition.SpeedPx <= 0f)
        {
            Halt(movement);
            return;
        }

        var delta = movement.ExitPoint - mobile.GlobalPosition;
        float remaining = delta.Length();

        if (remaining < 0.001f)
        {
            Halt(movement);
            return;
        }

        var direction = delta / remaining;

        // Выезд остаётся ПРЯМЫМ отрезком, а не дугой: неголономная модель заставила бы
        // юнита, выпущенного носом в стену, наматывать круги внутри корпуса завода, тогда
        // как весь смысл послабления — вывести его наружу кратчайшим путём. Корпус при этом
        // всё же доворачивается к направлению выезда, иначе он выезжал бы боком
        mobile.Rotation = Heading.TurnToward(mobile.Rotation, direction.Angle(),
            definition.TurnSpeed * (float)dt);

        Accelerate(movement, definition, direction * definition.SpeedPx, dt);
        movement.SeekForce = direction;
    }

    /// <summary>
    /// Скорость на подходе к цели.
    ///
    /// У шагающего торможения нет вовсе: он бежит с постоянной скоростью и встаёт разом.
    /// Так устроены боты в PA — у всех до единого в разделе navigation стоит brake = −1,
    /// тогда как у машин, кораблей и авиации там конечное число. Отсюда и правило:
    /// отрицательное торможение означает «останавливается мгновенно», и оно же
    /// умолчание для всего в этой игре.
    ///
    /// У колёсного торможение конечное, и скорость на подходе ограничена той, с которой
    /// он ещё успеет встать: v = √(2·a·s). Это не подгонка коэффициента, а само определение
    /// равнозамедленного движения, поэтому юнит останавливается ровно там, где нужно,
    /// при любых числах в справочнике.
    /// </summary>
    private static float Approach(UnitDefinition definition, float remaining)
    {
        if (definition.BrakePx < 0f)
            return definition.SpeedPx;

        float braked = Mathf.Sqrt(2f * definition.BrakePx * Mathf.Max(remaining, 0f));
        return Mathf.Min(definition.SpeedPx, braked);
    }

    /// <summary>
    /// Скорость, при которой цель ещё лежит вне окружности разворота.
    ///
    /// КАКОЙ ДЕФЕКТ ЭТО ЛЕЧИТ. Сущность едет туда, куда повёрнут корпус, поэтому радиус
    /// её разворота равен скорости, делённой на скорость вращения. Цель, заданная сбоку
    /// ближе этого радиуса, оказывается ВНУТРИ окружности разворота: сколько бы юнит
    /// ни доворачивал, дуга проходит мимо, и он наматывает круги вокруг точки назначения.
    /// Наблюдается это при приказе, отданном щелчком рядом с юнитом, но сбоку от его оси.
    ///
    /// УСЛОВИЕ ТОЧНОЕ, А НЕ ПОДОБРАННОЕ. Пусть до цели расстояние d, а расхождение оси
    /// корпуса с направлением на неё — угол t. Середина окружности разворота отстоит
    /// от корпуса на R перпендикулярно оси; подставив её в квадрат расстояния до цели
    /// и потребовав, чтобы цель лежала не ближе R, получаем d ≥ 2·R·|sin t|. Отсюда
    /// предельный радиус R = d / (2·|sin t|) и предельная скорость v = w·R.
    ///
    /// Цель прямо по курсу либо прямо позади даёт синус около нуля и ограничения не даёт:
    /// в первом случае поворачивать не нужно вовсе, во втором любая дуга рано или поздно
    /// выводит на цель. Наибольшее ограничение приходится на цель точно сбоку, где и
    /// возникает наблюдаемое кружение.
    /// </summary>
    private static float Turning(IMobile mobile, UnitDefinition definition, Vector2 position,
        Movement movement, float remaining)
    {
        if (definition.TurnSpeed <= 0.001f)
            return definition.SpeedPx;

        var delta = movement.Goal - position;

        if (delta.LengthSquared() < 0.0001f)
            return definition.SpeedPx;

        float lateral = Mathf.Abs(Mathf.Sin(Heading.Delta(mobile.Rotation, delta.Angle())));

        if (lateral < 0.001f)
            return definition.SpeedPx;

        // Меряем по ОСТАТКУ хода, а не по расстоянию до цели: стрелку достаточно выйти
        // на окружность дальности ствола, и требовать от него дуги до самой цели значило бы
        // заставить его ползти всю дорогу
        return definition.TurnSpeed * Mathf.Max(remaining, 0f) / (2f * lateral);
    }

    /// <summary>
    /// Цель ближе радиуса разворота и лежит не по курсу: подойти к ней точнее нельзя,
    /// а попытка означала бы круг вокруг неё.
    ///
    /// ЗАЧЕМ ЭТО ПОМИМО ОГРАНИЧЕНИЯ СКОРОСТИ. Ограничение (<see cref="Turning"/>) заставляет
    /// сущность вписываться в дугу, замедляясь тем сильнее, чем ближе цель. У самой точки
    /// назначения предельная скорость падает почти до нуля, и остаток пути занял бы время,
    /// несоразмерное оставшемуся расстоянию. Поэтому радиус прибытия расширен до радиуса
    /// разворота на полном ходу: попав в него, сущность считает ход законченным, и приказ
    /// завершается вместо медленного доворота на месте.
    ///
    /// Правило действует только при расхождении больше свободного угла поворота. Цель
    /// по курсу достигается прямо, и расширять для неё радиус прибытия значило бы
    /// останавливать отряд, не доходя до назначенной точки.
    ///
    /// ШИРОКАЯ ОСТАНОВКА СЮДА НЕ ВХОДИТ — по тому же основанию, что и у <see cref="Crowded"/>,
    /// и это существенно. Кружение возможно вокруг ТОЧКИ: цель, лежащая внутри окружности
    /// разворота, дугой не достаётся. Дальность ствола или инструмента задаёт не точку,
    /// а широкое кольцо остановки, попасть в которое дуга не мешает, и ограничения скорости
    /// (<see cref="Turning"/>) для этого достаточно.
    ///
    /// КАКОЙ ДЕФЕКТ ЭТО ЛЕЧИТ. Признак останавливает сущность до <see cref="Drive"/>, а угол
    /// корпуса меняет только Drive. Значит расхождение, при котором признак выставлен,
    /// само собой не сокращается никогда: сущность стоит, не поворачивается и потому
    /// остаётся кандидатом на ту же остановку в следующем кадре. Приказу движения это
    /// безразлично — он считает себя исполненным и снимается, — а приказ работы или боя
    /// не кончается ничем: исполнитель замирает, хотя цель достижима и путь до неё найден.
    /// Наблюдалось это на строителях, у которых следующий каркас лежал ЗА только что
    /// достроенным: направление на него уходило далеко от оси корпуса, а остаток хода
    /// был мал, потому что дистанция остановки у инструмента велика.
    /// </summary>
    private static bool Circling(IMobile mobile, UnitDefinition definition, Vector2 position,
        Movement movement, float remaining)
    {
        if (definition.TurnSpeed <= 0.001f)
            return false;

        if (movement.StopDistance > mobile.HitRadius * 2f)
            return false;

        var delta = movement.Goal - position;

        if (delta.LengthSquared() < 0.0001f)
            return false;

        float error = Mathf.Abs(Heading.Delta(mobile.Rotation, delta.Angle()));

        if (error <= definition.TurnFreeAngle)
            return false;

        return remaining <= definition.SpeedPx / definition.TurnSpeed;
    }

    /// <summary>
    /// Изменить скорость в пределах разгона и торможения. Разгон и торможение — разные
    /// числа: разогнаться шагающий может не мгновенно, а встать может.
    /// </summary>
    private static void Accelerate(Movement movement, UnitDefinition definition,
        Vector2 desired, double dt)
    {
        bool slowing = desired.LengthSquared() < movement.Velocity.LengthSquared();

        // Мгновенное торможение означает мгновенную ОСТАНОВКУ, а не мгновенное СБАВЛЕНИЕ хода.
        // Прежде разница не имела значения: скорость либо держалась полной, либо обращалась
        // в ноль. С ограничением по радиусу разворота (см. Turning) она меняется и в середине
        // пути, и всякое такое изменение читалось рывком — ход падал за кадр, а возвращался
        // за десятую долю секунды разгоном. Сбавление идёт разгонным темпом, остановка
        // по-прежнему мгновенна
        if (slowing && definition.BrakePx < 0f && desired.LengthSquared() < 0.0001f)
        {
            movement.Velocity = desired;
            return;
        }

        float rate = slowing && definition.BrakePx >= 0f
            ? definition.BrakePx
            : definition.AccelerationPx;
        movement.Velocity = movement.Velocity.MoveToward(desired, rate * (float)dt);
    }

    private static void Halt(Movement movement)
    {
        movement.Velocity = Vector2.Zero;
        movement.SeekForce = Vector2.Zero;
        movement.AvoidForce = Vector2.Zero;
        movement.AlignForce = Vector2.Zero;
        movement.WallForce = Vector2.Zero;
        movement.AvoidSide = 0;
        movement.WallSide = 0;
        movement.SeekScale = 1f;
        movement.StuckFor = 0f;
        movement.Neighbours = 0;
    }

    /// <summary>
    /// Путь пройден до конца, и сущность стоит в его последней точке. Ближе к заданной
    /// цели физически не подойти: она либо внутри постройки, либо за её краем.
    ///
    /// Проверка нужна отдельно от расстояния до цели, потому что цель могла оказаться
    /// недостижимой вплотную. Без неё юнит, посланный внутрь здания, давил бы в стену.
    /// </summary>
    private static bool Exhausted(PathHandle handle, Vector2 position, float radius) =>
        handle is { Arrived: true } && position.DistanceTo(handle.Goal) <= radius;

    /// <summary>
    /// Направление по пути. Системы поиска может не быть в сцене — тогда идём напрямую
    /// и препятствий не замечаем: это вырожденный случай, а не рабочий режим.
    /// </summary>
    private static Vector2 Follow(PathHandle handle, Vector2 position, Movement movement,
        float radius)
    {
        if (handle == null)
        {
            var direct = movement.Goal - position;
            return direct.LengthSquared() > 0.0001f ? direct.Normalized() : Vector2.Zero;
        }

        // Точку считаем пройденной, не доходя до неё вплотную: ломаная идёт по центрам
        // ячеек, и требовать попадания в центр значило бы вилять на каждом повороте
        handle.Advance(position, Mathf.Max(radius, NavGrid.CellPx * 0.75f));

        return handle.Direction(position);
    }

    /// <summary>
    /// Синхронная остановка: сущность у самой цели и упирается в своего, который стоит
    /// к ней ближе. Значит ближе не пройти, и дальше идти незачем.
    ///
    /// Правило действует только в зоне прибытия. Без этого ограничения отряд встал бы
    /// растянутой цепочкой: каждый останавливался бы, едва упёршись в идущего впереди.
    ///
    /// Широкая остановка (дальность ствола или инструмента) сюда не входит: у такой цели
    /// хватает места нескольким юнитам, и ранняя остановка из‑за союзника ближе к точке
    /// оставляла бы помощника и стрелка за пределами досягаемости.
    /// </summary>
    private bool Crowded(IMobile mobile, Vector2 position, float radius, Vector2 target)
    {
        var movement = mobile.Movement;
        float remaining = position.DistanceTo(target);

        if (movement.StopDistance > radius * 2f)
            return false;

        if (remaining - movement.StopDistance > radius * 3f)
            return false;

        // Обход свой, а не из общего прохода: проверка нужна до того, как выяснится
        // направление движения, а по обоим условиям выше она почти всегда отсекается
        // ещё до обращения к сетке
        int limit = NeighbourLimit > 0 ? NeighbourLimit : int.MaxValue;
        int seen = 0;

        foreach (var items in GM.Space.ReadyMobiles().Around(position, radius * PushSense))
        {
            for (int k = 0; k < items.Count && seen < limit; k++)
            {
                var other = items[k];

                if (ReferenceEquals(other, mobile) || other.Movement.Slot < 0)
                    continue;

                float gap = position.DistanceTo(other.GlobalPosition);

                // Предел, как и в общем обходе, считается по соседям в зоне
                if (gap > radius * PushSense)
                    continue;

                seen++;

                if (other.Faction != mobile.Faction)
                    continue;

                if (gap > radius + other.HitRadius + 2f)
                    continue;

                if (other.GlobalPosition.DistanceTo(target) < remaining)
                    return true;
            }

            if (seen >= limit)
                break;
        }

        return false;
    }

    // ── обход соседей ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Всё, что даёт один обход соседей. Правила остались раздельными — каждое живёт
    /// в своём методе и правит свои поля, — но выборка у них общая и читается однократно.
    ///
    /// ПОЧЕМУ НЕ СПИСОК СОСЕДЕЙ. Прежде обход складывал найденных в список, по которому
    /// затем проходили три правила, а расталкивание собирало свой список ещё дважды.
    /// Выборка при этом всякий раз была вложена в наибольшую, то есть сетка опрашивалась
    /// четырежды ради одних и тех же соседей, да ещё и с копированием ссылок.
    /// </summary>
    private struct Sensed
    {
        /// <summary>Считать ли локальный слой. У остановленного и выезжающего он не нужен.</summary>
        public bool Boids;

        public Vector2 Push;
        public Vector2 AlignSum;
        public int AlignCount;
        public float AvoidTotal;
        public float AvoidStrongest;
        public int AvoidPick;

        /// <summary>Множитель стремления к цели — см. <see cref="Scale"/>.</summary>
        public float Scale;

        /// <summary>Сколько соседей просмотрено. Читает панель отладки.</summary>
        public int Count;
    }

    /// <summary>
    /// Обойти соседей и посчитать по ним всё сразу. Положения соседей берутся прямо
    /// у сущностей — в этой фазе их никто не правит; скорости и предсказанные положения
    /// берутся из снимка, поэтому исход не зависит от порядка обхода.
    /// </summary>
    private Sensed Scan(int index, IMobile mobile, Vector2 position, float radius,
        Vector2 seek)
    {
        var movement = mobile.Movement;
        bool boids = seek != Vector2.Zero;
        float sense = radius * SenseFactor;

        var sensed = new Sensed { Boids = boids, Scale = 1f };

        // Локальному слою нужно чутьё, расталкиванию — мягкая зона; берётся большее,
        // а лишних отсеет каждое правило по своему порогу
        float range = boids ? Mathf.Max(sense, radius * PushSense) : radius * PushSense;

        var ahead = _ahead[index];
        float senseSquared = sense * sense;
        float rangeSquared = range * range;
        int limit = NeighbourLimit > 0 ? NeighbourLimit : int.MaxValue;

        foreach (var items in GM.Space.ReadyMobiles().Around(position, range))
        {
            for (int k = 0; k < items.Count && sensed.Count < limit; k++)
            {
                var other = items[k];
                int slot = other.Movement.Slot;

                // Оставшийся без номера в обход не входит: у него нет ни определения,
                // ни места в снимке. Себя сущность узнаёт по номеру, а не по ссылке
                if (slot < 0 || slot == index)
                    continue;

                var delta = other.GlobalPosition - position;
                float squared = delta.LengthSquared();

                // ПРЕДЕЛ СЧИТАЕТСЯ ПО ТЕМ, КТО В ЗОНУ ПОПАЛ, а не по кандидатам из корзин.
                // Корзина много крупнее зоны — на два с половиной радиуса корпуса
                // приходится вчетверо большая площадь просмотра, — и счёт по кандидатам
                // отбрасывал бы в первую очередь тех, кто ближе всех
                if (squared > rangeSquared)
                    continue;

                sensed.Count++;

                Repel(ref sensed, index, mobile, ahead, radius, other, slot);

                if (!boids || squared < 0.000001f)
                    continue;

                float contact = radius + _radius[slot] + radius * 0.5f;
                float distance = Mathf.Sqrt(squared);

                Scale(ref sensed, mobile, contact, seek, other, delta, distance);

                if (squared > senseSquared)
                    continue;

                Avoid(ref sensed, mobile, movement, seek, other, delta / distance, distance, sense);
                Align(ref sensed, mobile, other, slot);
            }

            if (sensed.Count >= limit)
                break;
        }

        _push[index] = sensed.Push;

        _senseSum += sensed.Count;
        _senseSeen++;

        if (sensed.Count > _senseMax)
            _senseMax = sensed.Count;

        return sensed;
    }

    /// <summary>
    /// Собственная доля расхождения с одним соседом.
    ///
    /// РЕАКЦИЯ, А НЕ ВОЗДЕЙСТВИЕ. Прежде пара разбиралась один раз, и тот, чей номер меньше,
    /// двигал обоих — то есть писал в чужое поле положения. Из этого следовало, что исход
    /// зависел от порядка обхода, а сама фаза была принципиально последовательной. Здесь
    /// каждая сторона считает пару сама и правит только себя, а доли двух сторон в сумме
    /// дают единицу (<see cref="Share"/>), поэтому прежние правила приоритета сохранены
    /// в точности, а запись в соседей исчезла.
    ///
    /// ПЕРЕКРЫТИЕ МЕРЯЕТСЯ ПО ПРЕДСКАЗАННЫМ ПОЛОЖЕНИЯМ. Расхождение считается до
    /// интегрирования, а применяется после него, и без поправки на ход этого шага оно
    /// отставало бы на кадр. Обе стороны предсказывают одинаково — по снимку скоростей, —
    /// поэтому согласие в паре не нарушается.
    /// </summary>
    private void Repel(ref Sensed sensed, int index, IMobile mobile, Vector2 ahead,
        float radius, IMobile other, int slot)
    {
        float wanted = radius + _radius[slot];
        float soft = wanted * SoftMargin;

        // Отбраковка по квадрату расстояния идёт ПЕРВОЙ. Соседей в чутье втрое больше,
        // чем в мягкой зоне, и разбор доли с корнем на каждого из них стоил бы дороже
        // самого расхождения
        var delta = _ahead[slot] - ahead;
        float squared = delta.LengthSquared();

        if (squared >= soft * soft)
            return;

        float share = Share(mobile.Movement, mobile.Faction, other);

        if (share <= 0f)
            return;

        float distance = Mathf.Sqrt(squared);

        // Совпавшие в точке расходятся по углу, выведенному из номера: направления у них
        // разные, а значит пара расцепится
        var push = distance > 0.001f
            ? delta / distance
            : Vector2.Right.Rotated(index * 2.399f);

        // Жёсткое разведение плюс слабая добавка мягкой зоны. Сумма непрерывна в точке
        // касания: на границе перекрытия первое слагаемое обращается в ноль, а второе
        // подходит к нему с того же значения
        float overlap = Mathf.Max(0f, wanted - distance) + SoftPush * (soft - distance);

        sensed.Push -= push * (overlap * share);
    }

    /// <summary>
    /// Какую долю расхождения сущность берёт на себя. Доли двух сторон в сумме дают единицу,
    /// поэтому пара расходится ровно так же, как расходилась при обоюдной записи.
    ///
    /// Выезжающего из корпуса завода не двигают вовсе: иначе толпа у ворот затолкала бы его
    /// обратно внутрь. Свой идущий полностью смещает своего стоящего — это и есть
    /// проталкивание сквозь союзников. Прочие пары делят расхождение поровну.
    /// </summary>
    private static float Share(Movement movement, Faction side, IMobile other)
    {
        var theirs = other.Movement;

        if (movement.Leaving && !theirs.Leaving)
            return 0f;

        if (!movement.Leaving && theirs.Leaving)
            return 1f;

        if (other.Faction == side && movement.Active != theirs.Active)
            return movement.Active ? 0f : 1f;

        return 0.5f;
    }

    /// <summary>
    /// Обход, а не расталкивание. Сила отталкивания в скоплении гасит стремление к цели —
    /// они направлены навстречу и взаимно уничтожаются, отчего сущность просто стоит.
    /// Обход правит курс мимо соседа и такого вырождения не даёт.
    ///
    /// Обходят не всех: своего, который сам идёт, обходить не нужно — с ним разберутся
    /// выравнивание и расхождение. Обходят удерживающего позицию и чужого.
    /// </summary>
    private void Avoid(ref Sensed sensed, IMobile mobile, Movement movement, Vector2 seek,
        IMobile other, Vector2 direction, float distance, float sense)
    {
        if (other.Faction == mobile.Faction && !other.Movement.HoldGround)
            return;

        float ahead = seek.Dot(direction);

        if (ahead < 0.2f)
            return;

        float weight = (1f - distance / sense) * ahead;
        sensed.AvoidTotal += weight;

        if (weight <= sensed.AvoidStrongest)
            return;

        sensed.AvoidStrongest = weight;

        // Уходим В СТОРОНУ, ПРОТИВОПОЛОЖНУЮ соседу. Знак выведен из определения
        // Orthogonal(): для курса (1,0) она даёт (0,−1). Сосед снизу, направление (0,1),
        // даёт положительное векторное произведение — значит уводить надо
        // положительным множителем, то есть вверх. Обратный знак разворачивал бы
        // юнита прямо в соседа, и обход читался бы как притяжение.
        //
        // Порог сдвинут солью. У встречных лоб в лоб векторное произведение около нуля,
        // и без сдвига обе сущности выбирают сторону по одному и тому же неустойчивому
        // знаку — то есть чаще всего одну и ту же, что и есть затор. Сдвиг постоянен
        // у сущности, поэтому в такой паре стороны почти всегда оказываются разными
        sensed.AvoidPick = seek.Cross(direction) > _salt.Of(movement.Salt) * SaltSide ? 1 : -1;
    }

    /// <summary>
    /// Сила обхода из накопленного. Сторона фиксируется, пока сила действует: иначе курс
    /// дребезжит между двумя соседями по разные стороны от направления движения.
    /// Освободился путь — фиксация снимается, и сущность возвращается к цели,
    /// а не идёт вдоль скопления дальше.
    /// </summary>
    private static Vector2 AvoidForce(Movement movement, Vector2 seek, in Sensed sensed)
    {
        if (sensed.AvoidTotal <= 0.001f)
        {
            movement.AvoidSide = 0;
            return Vector2.Zero;
        }

        if (movement.AvoidSide == 0)
            movement.AvoidSide = sensed.AvoidPick == 0 ? 1 : sensed.AvoidPick;

        return seek.Orthogonal() * movement.AvoidSide * sensed.AvoidTotal;
    }

    /// <summary>
    /// Отклонение от стен. Считается по геометрии строений, а не по растру навигации:
    /// растр огрубляет границу до ячейки, и сила, выведенная из него, дёргалась бы
    /// на ступеньках растеризации.
    ///
    /// ЗАЧЕМ ОНА НУЖНА ПРИ ЖЁСТКОМ ВЫТАЛКИВАНИИ. Выталкивание не даёт войти в здание,
    /// но и только: сущность прижимается к стене вплотную и едет вдоль неё, а всякое
    /// столкновение с соседом у самой стены оборачивается упором в угол. Отсюда следует,
    /// что расходиться со строением нужно ЗАРАНЕЕ, силой, — тогда и место для расхождения
    /// с соседями остаётся, и угол обходится по дуге.
    ///
    /// ДВЕ СОСТАВЛЯЮЩИЕ. Первая направлена по нормали от поверхности и растёт по мере
    /// приближения к ней. Одной её мало: у сущности, идущей прямо в стену, нормаль
    /// направлена навстречу стремлению к цели, и обе взаимно уничтожаются — это то самое
    /// вырождение, из-за которого локальный слой построен на обходе, а не на отталкивании.
    /// Поэтому добавляется вторая составляющая, направленная вдоль стены; её величина равна
    /// тому, насколько точно курс направлен в стену.
    ///
    /// СТОРОНА ДВИЖЕНИЯ ВДОЛЬ СТЕНЫ ФИКСИРУЕТСЯ — по тому же основанию, что и сторона обхода
    /// соседа (<see cref="Avoidance"/>), но здесь оно жёстче. У сущности, упёршейся в стену
    /// поперёк, проекция стремления к цели на стену близка к нулю, и её знак определяется
    /// случайными мелочами: толчком соседа, пересчётом пути, поворотом корпуса на градус.
    /// Без фиксации отряд у длинной стены поэтому ходит вдоль неё то в одну сторону,
    /// то в другую. Выбранная сторона держится, пока стена в полосе действия силы,
    /// и снимается, когда сущность от стены отошла.
    ///
    /// СИЛА ГАСНЕТ У САМОЙ ЦЕЛИ. Строитель работает вплотную к каркасу, а стрелок с малой
    /// дальностью подходит к постройке ближе полосы отклонения; если бы сила действовала
    /// и там, оба кружили бы вокруг цели вместо работы. Множитель выведен из остатка хода,
    /// поэтому отдельного перечня исключений не требуется.
    /// </summary>
    private Vector2 Walls(Movement movement, Vector2 position, Vector2 seek,
        float radius, float remaining)
    {
        float margin = radius * WallMargin;
        float fade = margin > 0.001f
            ? Mathf.Clamp(remaining / (radius + margin), 0f, 1f)
            : 0f;

        if (fade <= 0.001f)
        {
            movement.WallSide = 0;
            return Vector2.Zero;
        }

        GM.Obstacles.Nearby(position, radius + margin, _walls, movement.Exit);

        var push = Vector2.Zero;
        var facing = Vector2.Zero;
        float strongest = 0f;
        float into = 0f;

        foreach (var shape in _walls)
        {
            var closest = shape.ClosestPoint(position);
            var delta = position - closest;
            float distance = delta.Length();

            // Центр внутри корпуса означает, что жёсткое ограничение ещё не отработало
            // в этом кадре. Направления выхода здесь нет, и назначать его наугад незачем:
            // выталкивание разберётся достовернее любой силы
            if (distance < 0.001f)
                continue;

            float gap = distance - radius;

            if (gap >= margin)
                continue;

            var normal = delta / distance;
            float strength = 1f - Mathf.Max(gap, 0f) / margin;

            push += normal * strength;

            // Вдоль чего ехать, решает ОДНА стена — ближайшая. Складывать касательные
            // нескольких значило бы получать в углу между двумя строениями направление,
            // не идущее вдоль ни одного из них
            if (strength <= strongest)
                continue;

            strongest = strength;
            facing = normal;
            into = -seek.Dot(normal);
        }

        if (strongest <= 0.001f)
        {
            movement.WallSide = 0;
            return Vector2.Zero;
        }

        if (into <= 0f)
            return push * fade;

        var tangent = facing.Orthogonal();

        if (movement.WallSide == 0)
            movement.WallSide = WallSide(tangent, seek, movement.Velocity);

        return (push + tangent * (movement.WallSide * into * strongest)) * fade;
    }

    /// <summary>
    /// В какую сторону вдоль стены ехать. Основной признак — проекция стремления к цели
    /// на стену: она указывает, с какой стороны препятствие короче обойти. Когда курс
    /// направлен в стену почти перпендикулярно, проекция мала и знак её недостоверен;
    /// тогда сторона берётся по нынешней скорости, то есть сущность продолжает то движение,
    /// которое уже начала, а в группе — то, к которому её привело выравнивание.
    /// </summary>
    private static int WallSide(Vector2 tangent, Vector2 seek, Vector2 velocity)
    {
        float bySeek = tangent.Dot(seek);

        if (Mathf.Abs(bySeek) > 0.2f)
            return bySeek > 0f ? 1 : -1;

        float byVelocity = tangent.Dot(velocity);

        if (Mathf.Abs(byVelocity) > 0.001f)
            return byVelocity > 0f ? 1 : -1;

        return bySeek >= 0f ? 1 : -1;
    }

    /// <summary>
    /// Выравнивание скорости со своими. Единственная сила сплочения, которая здесь нужна:
    /// притяжение к центру группы пришлось бы отключать при встречном движении, а сила,
    /// которую сразу отключают, не нужна вовсе.
    /// </summary>
    private void Align(ref Sensed sensed, IMobile mobile, IMobile other, int slot)
    {
        if (other.Faction != mobile.Faction || !other.Movement.Active)
            return;

        // Скорость берётся из снимка, а не у соседа: свою он мог получить в этом же обходе,
        // и без снимка сумма зависела бы от порядка
        sensed.AlignSum += _velocity[slot];
        sensed.AlignCount++;
    }

    /// <summary>Направление выравнивания из накопленного.</summary>
    private static Vector2 AlignForce(in Sensed sensed) =>
        sensed.AlignCount == 0 || sensed.AlignSum.LengthSquared() < 0.0001f
            ? Vector2.Zero
            : sensed.AlignSum.Normalized();

    /// <summary>
    /// Правило «кто кого толкает»: стремление к цели ослабляется тем сильнее, чем точнее
    /// оно направлено в близкого соседа. Насколько именно — зависит от того, кто сосед.
    ///
    /// Отсюда же берётся окружение: цель, к которой прижались враги, перестаёт их
    /// расталкивать и остаётся на месте.
    /// </summary>
    private static void Scale(ref Sensed sensed, IMobile mobile, float contact, Vector2 seek,
        IMobile other, Vector2 delta, float distance)
    {
        if (distance > contact)
            return;

        float into = seek.Dot(delta / distance);

        if (into <= 0f)
            return;

        // Стоящий свой ослабляет seek слабо: идущий обязан проталкиваться сквозь него,
        // а не гасить курс. Сильный коэффициент оставляем врагу и окружению цели.
        float weight = other.Faction != mobile.Faction ? 0.9f
            : other.Movement.HoldGround ? 0.15f
            : 0.1f;

        float proximity = 1f - distance / contact;

        sensed.Scale = Mathf.Clamp(Mathf.Min(sensed.Scale, 1f - into * weight * proximity), 0f, 1f);
    }

    // ── интегрирование и ограничения ──────────────────────────────────────────────

    private void Integrate(IMobile mobile, double dt)
    {
        var movement = mobile.Movement;

        if (movement.Velocity.LengthSquared() < 0.0001f)
            return;

        // Поворота корпуса здесь нет намеренно: он назначается в Drive, ДО того как
        // из него выведена скорость. Доворачивать корпус по уже посчитанной скорости
        // значило бы замкнуть круг — скорость выводится из угла, а угол из скорости
        mobile.GlobalPosition += movement.Velocity * (float)dt;
    }

    /// <summary>
    /// Сущность не продвигается. Через порог путь выбрасывается из кеша, и следующий запрос
    /// посчитает его заново — уже от нынешнего положения и по нынешнему растру. Без этого
    /// любая недоработка локального слоя оборачивается вечно стоящим юнитом.
    /// </summary>
    private void Stall(IMobile mobile, Movement movement, double dt)
    {
        movement.StuckFor += (float)dt;

        if (movement.StuckFor < StuckTimeout)
            return;

        movement.StuckFor = 0f;
        movement.AvoidSide = 0;

        // Сторона движения вдоль стены здесь НЕ сбрасывается, хотя сторона обхода соседа
        // сбрасывается. Различие в том, чем кончается ошибочный выбор. Обойдя соседа не с той
        // стороны, сущность упирается в него же, и попытка с другой стороны — единственный
        // выход. Поехав не в ту сторону вдоль стены, она всё же едет и рано или поздно стену
        // минует; сброс же по сроку означал бы, что отряд у длинной стены разворачивается
        // каждые StuckTimeout секунд, — это и есть наблюдаемое хождение туда и обратно
        _pathfinding?.Release(mobile);
    }

    /// <summary>
    /// Свести шаг: применить накопленное расхождение и жёсткие ограничения — наружу
    /// из зданий, внутрь границ мира.
    ///
    /// ПОЧЕМУ ОТДЕЛЬНОЙ ФАЗОЙ. Расхождение посчитано по положениям на начало шага, а ход
    /// этого шага прибавлен интегрированием; применить одно к другому можно только после
    /// того, как оба готовы. Выталкивание же обязано быть последним: оно решает задачу
    /// достоверно, тогда как всякая сила решала бы её вероятностно.
    /// </summary>
    private void Settle(int index, double dt)
    {
        var mobile = _actors[index];
        var movement = mobile.Movement;
        float radius = mobile.HitRadius;

        var push = _push[index];

        // МЕЛКИЙ СДВИГ НЕ ПРИМЕНЯЕТСЯ ВОВСЕ. Мягкая зона расхождения даёт стоящим вплотную
        // небольшую постоянную поправку; сама по себе она безобидна, но всякий сдвиг ставит
        // сущность в очередь на запись положения в узел, а это переход границы C#↔Godot.
        // По замеру на стоящей армии, где двигались единицы из полутора тысяч, записи
        // положения занимали 11,7 % времени главного потока. Ниже порога поправка
        // отбрасывается вместе с признаком вытеснения: расселившийся отряд замирает
        // полностью, вместо того чтобы вечно подрагивать
        if (push.LengthSquared() < PushFloor * PushFloor)
            push = Vector2.Zero;

        var position = mobile.GlobalPosition + push;

        // Вытеснение соседями объявляется удержанию позиции: место, с которого юнита
        // отодвинули, переезжает вместе с ним — см. Movement.Drift
        movement.Drift = push;

        position = GM.Obstacles.PushOut(position, radius, movement.Exit);

        var bounds = World.ArenaBounds;
        position.X = Mathf.Clamp(position.X, bounds.Position.X + radius, bounds.End.X - radius);
        position.Y = Mathf.Clamp(position.Y, bounds.Position.Y + radius, bounds.End.Y - radius);

        mobile.GlobalPosition = position;

        // Продвижение меряется здесь, а не сразу после интегрирования: там оно совпадало
        // с ожидаемым всегда, поскольку положение только что было туда и записано, отчего
        // застревание не обнаруживалось вовсе. Итог шага знает лишь эта фаза — она видит
        // и расхождение с соседями, и упор в стену
        float expected = movement.Velocity.Length() * (float)dt;

        if (expected > 0.0001f && _before[index].DistanceTo(position) < expected * 0.2f)
            Stall(mobile, movement, dt);
        else
            movement.StuckFor = 0f;

        if (movement.Leaving)
            FinishExit(mobile, movement, position, radius);
    }

    /// <summary>
    /// Снять Leaving по любому из трёх оснований: корпус покинут, вышло время,
    /// завод снесён. Телепорт — только по таймауту.
    /// </summary>
    private void FinishExit(IMobile mobile, Movement movement, Vector2 position, float radius)
    {
        var exit = movement.Exit;

        if (exit == null)
            return;

        // Завод снесён посреди выезда либо корпус уже покинут — оба основания означают
        // одно и то же: держать послабление больше не на чем
        if (!GM.Obstacles.Contains(exit) || !GM.Obstacles.CircleHits(exit, position, radius))
        {
            movement.EndExit();
            return;
        }

        if (movement.ExitFor < ExitTimeout)
            return;

        var cell = GM.Nav.NearestPassable(NavGrid.ToCell(movement.ExitPoint), radius);
        mobile.GlobalPosition = NavGrid.ToWorld(cell);
        movement.EndExit();
    }

    /// <summary>Сколько подвижных сущностей обслужено в прошлом кадре. Читает панель отладки.</summary>
    public int Tracked => _actors.Count;

    public override void CaptureSnapshot(JsonObject data)
    {
        data["tracked"] = Tracked;
        data["active"] = _active;
        data["settled"] = _settled;
        data["blocked"] = _blocked;
        data["leaving"] = _leaving;
        data["soft_margin"] = SoftMargin;
        data["neighbour_limit"] = NeighbourLimit;
        data["neighbours_avg"] = _senseSeen > 0 ? (float)_senseSum / _senseSeen : 0f;
        data["neighbours_max"] = _senseMax;
        data["bucket_px"] = GM.Space.Mobiles.BucketPx;
    }

}
