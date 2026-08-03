using Godot;

/// <summary>
/// Распределение задач на стороне игрока: чем занять того, кому игрок ничего не приказал.
/// Порядок предпочтений — строить → чинить → бить противника в пределах обзора → идти
/// за коммандером.
///
/// ОДНА СИСТЕМА НА ПОДВИЖНЫХ И НЕПОДВИЖНЫХ. Башня-сборщик обслуживается здесь же, а не
/// отдельной системой: она есть тот же фабрикатор, только здание. Порядок предпочтений
/// у них общий, поиск работы общий (см. <see cref="Jobs"/>), а различаются они лишь
/// пределами поиска, и те выводятся из определения — из подвижности и дальности инструмента.
/// Отдельная система для башни свелась к трём строкам и повторяла решение, которое
/// принимала сама башня, то есть держала одно правило сразу в двух слоях.
///
/// Сопровождение — не работа, а отсутствие работы: юнит с таким приказом считается свободным
/// и бросит его, как только появится что-то полезное. Поэтому юниты не толкутся на месте
/// и не убегают от базы, оставшись без дела.
///
/// Ремонт, наоборот, до конца: начатое лечение не прерывается ради появившегося каркаса.
/// Дёргать исполнителя между целями хуже, чем чуть позже начать стройку.
///
/// ПАРА К <see cref="EnemyAiSystem"/>, А НЕ ЧАСТНЫЙ СЛУЧАЙ ОБЩЕЙ СИСТЕМЫ. Класс юнита один
/// на обе стороны, но задачи сторонам раздают две равноправные системы, названные по стороне:
/// принципы выдачи у них разные и будут расходиться дальше.
///
/// Исполнение приказа ни одной из них не принадлежит: приказ отрабатывает сама сущность,
/// а зовёт её единственная <see cref="OrderSystem"/>.
///
/// Коммандер сюда не входит: он единственный, кем игрок распоряжается напрямую,
/// и подставлять ему занятия помимо приказов нельзя.
/// </summary>
public partial class PlayerAiSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var worker in GM.Index.All<IWorker>())
        {
            if (worker.Definition == null || worker.Faction != Faction.Player
                || worker is Commander)
                continue;

            if (worker is IMobile { Movement.Leaving: true })
                continue;

            // Отсчёт до переигровки цели ведём у всех подвижных: он нужен и тому,
            // кто сейчас идёт следом, — иначе цель искалась бы каждый кадр
            if (worker is Unit unit)
                unit.TickRetarget(dt);

            if (!IsFree(worker))
                continue;

            var order = ChooseJob(worker);

            // Недопустимый вид приказа очередь не примет — второй сети не нужно.
            // Именно поэтому башне не достанется ни атака, ни сопровождение
            if (order != null)
                worker.Orders.TrySet(order);
        }
    }

    /// <summary>
    /// Свободен тот, у кого нет приказа или кто просто идёт следом. У неподвижного
    /// сопровождения не бывает, поэтому для него условие сводится к отсутствию приказа.
    /// </summary>
    private static bool IsFree(IWorker worker)
    {
        var orders = worker.Orders;

        return orders.Idle || orders.Current.Kind == OrderKind.Follow;
    }

    private Order ChooseJob(IWorker worker)
    {
        var from = worker.GlobalPosition;
        var def = worker.Definition;

        if (def.CanBuild)
        {
            var blueprint = Jobs.NearestBlueprint(from, BuildReach(def));
            if (blueprint != null)
                return Order.Work(OrderKind.Build, blueprint);
        }

        if (def.CanRepair)
        {
            var damaged = Jobs.NearestDamaged(from, RepairReach(def), def.CanRepairUnits);

            if (damaged != null)
                return Order.Repair(damaged);
        }

        if (worker is Unit unit && ChooseTarget(unit) is { } attack)
            return attack;

        var commander = GM.Commander;

        // Неподвижному идти некуда, а уже идущему следом приказ переигрывать незачем:
        // точка обновляется сама
        if (!def.IsMobile || commander == null
            || worker.Orders.Current?.Kind == OrderKind.Follow)
            return null;

        return Order.Follow(commander);
    }

    /// <summary>
    /// Докуда искать стройку. Подвижный ищет по всей карте — он к работе идёт; неподвижный
    /// не дальше своего манипулятора, поскольку дотянуться дальше ему нечем.
    /// </summary>
    private static float BuildReach(UnitDefinition def) =>
        def.IsMobile ? float.MaxValue : def.WorkRangePx;

    /// <summary>
    /// Докуда искать повреждённое. У подвижного предел уже свой — радиус обзора: бегать через
    /// полкарты чинить забор не то поведение, которого от него ждёшь. У неподвижного предел
    /// тот же, что и у стройки, — длина манипулятора.
    /// </summary>
    private static float RepairReach(UnitDefinition def) =>
        def.IsMobile ? def.VisionRadiusPx : def.WorkRangePx;

    /// <summary>
    /// Кого бить, если работы нет. Цель ищется ТОЛЬКО В ПРЕДЕЛАХ ОБЗОРА — этим выдача задач
    /// на стороне игрока и отличается от выдачи у противника, который идёт к базе через всю
    /// карту. Без предела оборона разбежалась бы за одиночками по всему полю, и рубеж держать
    /// стало бы некому.
    ///
    /// ЧЕГО ЗДЕСЬ ПОКА НЕТ. Цель, вышедшая из обзора, приказ не обрывает: приказ атаки держит
    /// живую ссылку и ведёт юнита за целью сколько угодно далеко. Правильное поведение —
    /// доехать до последней замеченной точки и там остановиться, то есть преследование должно
    /// быть подвидом приказа движения с обновляемой точкой, а не приказом атаки. Сделать это
    /// предстоит вместе с приказом «охранять»; здесь помечено, чтобы разница между обзором
    /// и глубиной преследования не считалась случайной.
    /// </summary>
    private Order ChooseTarget(Unit unit)
    {
        if (unit.Weapon == null || !unit.NeedsTarget)
            return null;

        var victim = Targeting.Nearest(unit.GlobalPosition, Faction.Hostile,
            unit.Definition.VisionRadiusPx);

        if (victim is not Node2D node)
            return null;

        unit.NoteTargeted();

        return Order.Attack(node);
    }
}
