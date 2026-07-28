using Godot;

/// <summary>
/// Турель: постройка со стволом. С места не сходит, но башню крутит — и потому
/// единственная постройка, у которой ось «вперёд» живёт в Rotation ноды, а не в справочнике.
///
/// Ведёт себя в бою ровно как все вооружённые: состоит в группе «armed», цель ей находит
/// WeaponSystem, доворот и конус — там же. Своего кода стрельбы у неё нет.
///
/// Параметры ствола заданы на самой сцене, как у завода: скорость переработки у Factory
/// живёт там же. Оружие вынесено в справочник, чтобы башня и коммандер настраивались одинаково.
/// </summary>
public partial class Turret : Building, IArmed
{
    [Export] public WeaponDef Gun;

    /// <summary>Скорость вращения башни в градусах в секунду.</summary>
    [Export] public float TurnSpeedDegrees = 90f;

    public WeaponState GunState { get; } = new();

    WeaponState IArmed.Gun => GunState;

    public WeaponDef Weapon => Gun;

    /// <summary>Башня крутится — ось берём у самой ноды, а не из справочника.</summary>
    public override float Facing => Rotation;

    public bool CanFire => true;

    /// <summary>Своей цели нет: ближайшего врага в радиусе находит система стрельбы.</summary>
    public IDamageable FireTarget => null;

    public float TurnSpeed => Mathf.DegToRad(TurnSpeedDegrees);

    public override void Init(int id, BuildableDef def, Vector2I cell)
    {
        base.Init(id, def, cell);

        // Смотрит наружу от базы: первый разворот не тратится на полкруга
        Rotation = Position.IsZeroApprox() ? 0f : Position.Angle();

        AddToGroup("armed");
    }

    public override void _Process(double delta) => QueueRedraw();

    public void AimAt(Vector2 point, double dt)
    {
        if (GlobalPosition.IsEqualApprox(point))
            return;

        float desired = Heading.AngleTo(GlobalPosition, point);
        Rotation = Heading.TurnToward(Rotation, desired, TurnSpeed * (float)dt);
    }

    public override void OnDestroyed()
    {
        RemoveFromGroup("armed");
        base.OnDestroyed();
    }

    public override void _Draw()
    {
        if (Def == null)
            return;

        float half = Const.Unit * 0.5f;

        WeaponGizmo.Draw(this, Weapon, Def.Color);

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

        DrawColoredPolygon(body, Def.Color);
        DrawPolyline(new[] { body[0], body[1], body[2], body[0] },
            new Color(0f, 0f, 0f, 0.45f), 2f);

        // Основание стоит по клетке и не крутится — компенсируем поворот башни
        DrawSetTransform(Vector2.Zero, -Rotation, Vector2.One);
        DrawRect(new Rect2(-half, -half, Const.Unit, Const.Unit), new Color(Def.Color, 0.3f));
        DrawRect(new Rect2(-half, -half, Const.Unit, Const.Unit), new Color(0f, 0f, 0f, 0.35f),
            false, 2f);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        HealthBar.Draw(this, Health, Const.Unit * 0.9f, -half - 10f, Rotation);
    }
}
