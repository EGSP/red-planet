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
    public WeaponState GunState { get; } = new();

    WeaponState IArmed.Gun => GunState;

    /// <summary>
    /// Ствол турели — такой же инструмент, как строительная рука фабрикатора, и лежит
    /// в том же списке. Раньше он был отдельным полем сцены, из-за чего турель настраивалась
    /// не там, где все остальные, а её числа не попадали в справочник вовсе.
    /// </summary>
    public WeaponDefinition Weapon => Definition?.Weapon;

    /// <summary>Скорость вращения башни в градусах в секунду.</summary>
    public float TurnSpeedDegrees => Definition?.TurnSpeedDegrees ?? 90f;

    /// <summary>Башня крутится — ось берём у самой ноды, а не из справочника.</summary>
    public override float Facing => Rotation;

    public bool CanFire => true;

    /// <summary>
    /// Приказали цель — бьём её, не приказали — ближайшую в радиусе найдёт система стрельбы.
    /// Своего кода наведения у турели по-прежнему нет.
    /// </summary>
    public IDamageable FireTarget =>
        Orders.Current?.Kind == OrderKind.Attack ? Orders.Current.Entity as IDamageable : null;

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public override void Init(int id, UnitDefinition def, Vector2 center, float facing)
    {
        base.Init(id, def, center, facing);

        // Башня начинает с угла, под которым турель поставили: игрок, разворачивая её
        // при постановке, показывает, откуда ждёт противника.
        //
        // Угол из справочника означает, что игрок его не задавал — постановка была щелчком
        // без протаскивания. Тогда остаётся прежнее правило: смотреть наружу от базы,
        // чтобы первый разворот не тратился на полкруга
        bool own = Mathf.IsEqualApprox(facing, Mathf.DegToRad(def.FacingDegrees));

        Rotation = own && !Position.IsZeroApprox() ? Position.Angle() : facing;
    }

    public override void _Process(double delta) => QueueRedraw();

    public void AimAt(Vector2 point, double dt)
    {
        if (GlobalPosition.IsEqualApprox(point))
            return;

        float desired = Heading.AngleTo(GlobalPosition, point);
        Rotation = Heading.TurnToward(Rotation, desired, TurnSpeed * (float)dt);
    }

    public override void _Draw()
    {
        if (Definition == null)
            return;

        float half = Const.Unit * 0.5f;

        UnitGizmos.Draw(this, GizmoTools.From(Definition), Faction,
            selected: GizmoGate.IsSelected(this),
            armedStructure: true);

        // Башня — треугольник носом вперёд по оси. Рисуется в координатах самой ноды,
        // до всякой правки трансформа: нос обязан совпадать с конусом прицеливания
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

        // Основание стоит под углом постановки и вслед за башней не крутится — снимаем
        // поворот ноды и ставим вместо него угол корпуса
        DrawSetTransform(Vector2.Zero, BodyFacing - Rotation, Vector2.One);
        ShapeDraw.Rect(this, new Rect2(-half, -half, Const.Unit, Const.Unit),
            ShapeStyle.Filled(new Color(Definition.Color, 0.3f), new Color(0f, 0f, 0f, 0.35f), 2f,
                WidthMode.Screen));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        HealthBar.Draw(this, Health, Const.Unit * 0.9f, -half - 10f, Rotation);
    }
}
