using Godot;

/// <summary>
/// Турель: постройка со стволом. С места не сходит, но башню крутит — и потому
/// единственная постройка, у которой ось «вперёд» живёт в Rotation ноды, а не в справочнике.
///
/// Ведёт себя в бою ровно как все вооружённые: реализует IArmed, цель ей находит
/// WeaponSystem, доворот и конус — там же. Своего кода стрельбы у неё нет.
///
/// Параметры ствола заданы на самой сцене, как у завода: скорость переработки у Factory
/// живёт там же. Оружие вынесено в справочник, чтобы башня и коммандер настраивались одинаково.
/// </summary>
public partial class Turret : Building, IArmed
{
    /// <summary>
    /// Ствол турели — такой же инструмент, как строительная рука фабрикатора, и лежит
    /// в том же списке. Раньше он был отдельным полем сцены, из-за чего турель настраивалась
    /// не там, где все остальные, а её числа не попадали в справочник вовсе.
    /// </summary>
    public WeaponDefinition Weapon => Definition?.Weapon;

    /// <summary>
    /// Ось башни. Складывается из угла постановки и доворота ствола в его секторе,
    /// поэтому турель с <c>aim_arc_degrees</c> меньше 180 держит сектор обстрела,
    /// отсчитанный от того угла, под которым её поставили.
    /// </summary>
    public override float Facing => Aim.PrimaryWeapon?.World(BodyFacing) ?? BodyFacing;

    public bool CanFire => true;

    /// <summary>
    /// Приказали цель — бьём её, приказали область — ближайшую в ней, не приказали —
    /// ближайшую в радиусе найдёт система стрельбы. Своего кода наведения у турели
    /// по-прежнему нет.
    /// </summary>
    public IDamageable FireTarget => Orders.Current switch
    {
        { Kind: OrderKind.Attack } order => order.Entity as IDamageable,
        { Kind: OrderKind.AttackArea } order => AreaTarget(order),
        _ => null,
    };

    /// <summary>
    /// Приказ по области у неподвижной постройки означает не подход, а выбор цели: круг
    /// задаёт, кого башня предпочитает всем прочим. Подходить ей некуда, поэтому весь приказ
    /// сводится к одному решению — есть ли в круге кого бить.
    ///
    /// Пустой круг исчерпывает приказ сразу, тогда как подвижный исполнитель сперва обязан
    /// дойти до центра области. Различие следует из самой неподвижности: дойти башня
    /// не может никогда, и требовать от неё прихода значило бы, что приказ висит в очереди
    /// вечно — он не кончается ни гибелью цели, ни приходом в точку.
    /// </summary>
    public override void RunOrder(Order order, double dt)
    {
        if (order.Kind != OrderKind.AttackArea)
        {
            base.RunOrder(order, dt);
            return;
        }

        if (AreaTarget(order) == null)
            Orders.DropCurrent();
    }

    private IDamageable AreaTarget(Order order) =>
        Targeting.Nearest(order.Pos, Faction.Opposite(), order.Radius);

    public override void Init(int id, UnitDefinition def, Vector2 center, float facing)
    {
        // Угол корпуса башни: игрок, разворачивая турель при постановке, показывает,
        // откуда ждёт противника. Он же — середина сектора обстрела, если сектор ограничен.
        //
        // Угол из справочника означает, что игрок его не задавал — постановка была щелчком
        // без протаскивания. Тогда остаётся прежнее правило: смотреть наружу от базы,
        // чтобы первый разворот не тратился на полкруга
        bool own = Mathf.IsEqualApprox(facing, Mathf.DegToRad(def.FacingDegrees));
        bool outward = own && !center.IsZeroApprox();

        base.Init(id, def, center, outward ? center.Angle() : facing);

        Rotation = BodyFacing;
    }

    /// <summary>
    /// Ось башни живёт в <see cref="Node2D.Rotation"/>: по ней рисуются запасной силуэт
    /// и полоса прочности, а корпус модели снимает поворот ноды обратно — см.
    /// <see cref="Building.SyncModel"/>.
    /// </summary>
    protected override void AfterAim() => Rotation = Facing;

    /// <summary>
    /// Точка вылета из модели, если турель ею снабжена. Правило то же, что у подвижных:
    /// решение стрелять принимается от центра, а срез ствола задаёт лишь место рождения
    /// снаряда — см. <see cref="IArmed.MuzzlePosition"/>.
    /// </summary>
    public Vector2 MuzzlePosition =>
        Aim.PrimaryWeapon?.Muzzle(GlobalPosition) ?? GlobalPosition;

    /// <summary>
    /// Довернуть стволы. Требование к развороту корпуса стенд накапливает, но турель его
    /// не исполняет: основание вкопано, и цель за пределами сектора для неё недостижима.
    /// </summary>
    public void AimAt(Vector2 point, double dt) =>
        Aim.AimWeapons(BodyFacing, GlobalPosition, point, dt);

    public override void _Draw()
    {
        if (Definition == null)
            return;

        float half = Const.Unit * 0.5f;

        UnitGizmos.Draw(this, GizmoTools.From(Definition), Faction,
            selected: GizmoGate.IsSelected(this),
            armedStructure: true);

        // Модель рисует и основание, и башню сама: доворот ствола ей задаёт SyncModel,
        // а поворот ноды до неё не доходит — корпус модели снимает его обратно.
        // Полосу прочности рисует слой пометок, идущий после модели
        if (Model != null)
            return;

        // Основание стоит под углом постановки и вслед за башней не крутится — снимаем
        // поворот ноды и ставим вместо него угол корпуса. Рисуем до башни: непрозрачный
        // квадрат иначе перекрыл бы треугольник
        DrawSetTransform(Vector2.Zero, BodyFacing - Rotation, Vector2.One);
        var baseRect = new Rect2(-half, -half, Const.Unit, Const.Unit);
        BuildingSkirt.Draw(this, baseRect);
        ShapeDraw.Rect(this, baseRect,
            ShapeStyle.Filled(Definition.Color, new Color(0f, 0f, 0f, 0.35f), 2f, WidthMode.Screen));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // Башня — треугольник носом вперёд по оси. После сброса трансформа: координаты
        // ноды, как у конуса прицеливания; поверх непрозрачного основания
        float nose = half * 0.95f;
        float back = half * 0.6f;
        var body = new[]
        {
            new Vector2(nose, 0f),
            new Vector2(-back, -back * 0.85f),
            new Vector2(-back, back * 0.85f),
        };

        ShapeDraw.Polygon(this, body,
            ShapeStyle.Filled(Definition.Color, new Color(0f, 0f, 0f, 0.45f), 2f, WidthMode.Screen));

        PaintMarks(this);
    }

    /// <summary>
    /// Полоса прочности башни. Она разворачивается вместе с осью башни, поэтому
    /// <see cref="Building.PaintMarks"/> здесь заменяется целиком: место турели — всегда
    /// одна клетка, а поворот полосы задан явно.
    /// </summary>
    protected override void PaintMarks(CanvasItem canvas)
    {
        float half = Const.Unit * 0.5f;
        HealthBar.Draw(canvas, Health, Const.Unit * 0.9f, -half - 10f, Rotation);
    }
}
