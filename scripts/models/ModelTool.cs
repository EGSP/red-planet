using Godot;

/// <summary>
/// Назначение части модели, изображающей инструмент. Совпадает по смыслу с делением
/// <see cref="ToolDefinition"/> на <see cref="WeaponDefinition"/> и
/// <see cref="WorkToolDefinition"/>: модель показывает то, чем сущность снабжена
/// по справочнику, и собственных видов инструментов не заводит.
/// </summary>
public enum ModelToolRole
{
    /// <summary>Ствол. Точка вылета снаряда берётся из <see cref="ModelTool.Muzzle"/>.</summary>
    Weapon,

    /// <summary>Строительная рука. Луч работы выходит из той же точки.</summary>
    BuildArm,
}

/// <summary>
/// Часть модели, изображающая инструмент: ствол или манипулятор.
///
/// ЧТО ЭТОТ УЗЕЛ ЗАДАЁТ, А ЧТО НЕТ. Он задаёт только размещение: где инструмент сидит
/// на корпусе, вокруг какой точки поворачивается и откуда выходит выстрел. Числа
/// инструмента — дальность, урон, интервал, мощность — принадлежат
/// <see cref="ToolDefinition"/> и живут в .toml. Отсюда следует правило: модель не может
/// добавить сущности инструмент, которого нет в справочнике, и не может изменить ни одного
/// его параметра; она лишь показывает уже объявленное.
///
/// СВЯЗЬ СО СПРАВОЧНИКОМ. По умолчанию часть связывается с первым инструментом подходящей
/// роли. Если у сущности два ствола, каждой части задаётся <see cref="ToolId"/> — тот же
/// идентификатор, что стоит в списке <c>tools</c> файла юнита.
///
/// ОСЬ. Узел обязан смотреть вперёд по +X при нулевом повороте: наведение выставляет
/// <see cref="Node2D.Rotation"/> целиком, а не добавляет к тому, что задано в сцене.
/// Разворот самого изображения относительно оси задаётся поворотом дочернего спрайта.
/// </summary>
[Tool, GlobalClass]
public partial class ModelTool : Node2D
{
    /// <summary>Чем эта часть является для игровых систем.</summary>
    [Export] public ModelToolRole Role { get; set; } = ModelToolRole.Weapon;

    /// <summary>
    /// Идентификатор инструмента из <c>tools</c> файла юнита. Пусто — часть связывается
    /// с любым инструментом своей роли; заполняется тогда, когда ролей одного вида
    /// несколько и их нужно различить.
    /// </summary>
    [Export] public string ToolId { get; set; } = "";

    /// <summary>
    /// Точка вылета: откуда рождается снаряд и откуда тянется луч работы. Не задана —
    /// точкой считается начало координат самой части.
    /// </summary>
    [Export] public Node2D Muzzle { get; set; }

    /// <summary>
    /// Поворачивается ли часть вслед за наведением. Ложь оставляет её неподвижной
    /// относительно корпуса — так устроен инструмент, жёстко закреплённый на раме.
    /// </summary>
    [Export] public bool FollowsAim { get; set; } = true;

    /// <summary>Показывать ли подсказки в редакторе. На игру не влияет.</summary>
    [Export] public bool ShowGizmo { get; set; } = true;

    /// <summary>Точка вылета в мировых координатах — с учётом поворота корпуса и наведения.</summary>
    public Vector2 MuzzleGlobal => Muzzle != null ? Muzzle.GlobalPosition : GlobalPosition;

    private BeamVisual _beam;
    private bool _beamSought;

    private ProjectileDeclaration _projectile;
    private bool _projectileSought;

    /// <summary>
    /// Луч, объявленный внутри части. Null означает, что луча в модели нет и носитель
    /// покажет работу запасным отрезком.
    ///
    /// Ищется один раз при первом обращении: состав сцены во время игры не меняется.
    /// Подписывать луч не нужно — он принадлежит той части, внутри которой лежит, ровно
    /// как вспышка выстрела принадлежит своему стволу.
    /// </summary>
    public BeamVisual Beam
    {
        get
        {
            if (!_beamSought)
            {
                _beamSought = true;
                _beam = Seek<BeamVisual>(this);
            }

            return Alive.Is(_beam) ? _beam : null;
        }
    }

    /// <summary>
    /// Объявление снаряда, лежащее внутри части. Null означает, что своего снаряда у ствола
    /// нет и <see cref="ProjectileSystem"/> возьмёт общий.
    ///
    /// Ищется тем же однократным обходом, что и луч, и по той же причине: состав сцены
    /// во время игры не меняется.
    /// </summary>
    public ProjectileDeclaration Projectile
    {
        get
        {
            if (!_projectileSought)
            {
                _projectileSought = true;
                _projectile = Seek<ProjectileDeclaration>(this);
            }

            return Alive.Is(_projectile) ? _projectile : null;
        }
    }

    private static T Seek<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                return match;

            if (Seek<T>(child) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>Подходит ли часть под запрос по роли и идентификатору инструмента.</summary>
    public bool Matches(ModelToolRole role, string toolId) =>
        Role == role && (string.IsNullOrEmpty(toolId) || string.IsNullOrEmpty(ToolId)
                         || ToolId == toolId);

    private ModelLayer _gizmo;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            _gizmo = ModelLayer.Attach(this, PaintGizmo, "Gizmo", ModelLayer.TopZ, internalNode: true);
    }

    public override void _Process(double delta)
    {
        // Подсказки обязаны следовать за перетаскиванием части и точки вылета, а сигнала
        // «дочерний узел сдвинулся» у Node2D нет. В игре перерисовка не нужна вовсе:
        // узел ничего не рисует, кроме подсказок редактора.
        if (Engine.IsEditorHint())
            _gizmo?.QueueRedraw();
        else
            SetProcess(false);
    }

    /// <summary>
    /// Подсказка инструмента. Рисует её слой поверх изображения, иначе спрайт самой части
    /// закрыл бы и ось, и точку вылета — см. <see cref="ModelLayer"/>.
    /// </summary>
    private void PaintGizmo(Node2D canvas)
    {
        if (!ShowGizmo)
            return;

        var color = Role == ModelToolRole.Weapon
            ? new Color(1f, 0.55f, 0.3f)
            : new Color(0.4f, 0.85f, 1f);

        // Ось части: куда смотрит инструмент при нулевом наведении
        ModelGizmo.Arrow(canvas, Vector2.Zero, 0f, ModelGizmo.AxisLength, color);
        ModelGizmo.Cross(canvas, Vector2.Zero, ModelGizmo.PivotSize, color);

        // Идентификатор подписан прямо у оси: пока инструмент у вида один, он пуст и связь
        // определяется ролью, а как только их стало два — по этой подписи видно, какая
        // часть какому инструменту справочника отвечает
        canvas.DrawString(ThemeDB.FallbackFont,
            new Vector2(4f, -6f),
            string.IsNullOrEmpty(ToolId) ? $"<{Role}>" : ToolId,
            HorizontalAlignment.Left, -1, 10, color with { A = 0.85f });

        if (Muzzle == null)
            return;

        var muzzle = canvas.ToLocal(Muzzle.GlobalPosition);

        // Вынос точки вылета от оси вращения: по этому отрезку видно, насколько выстрел
        // сместится в сторону при довороте
        canvas.DrawLine(Vector2.Zero, muzzle, color with { A = 0.4f }, 1f);
        canvas.DrawCircle(muzzle, ModelGizmo.MuzzleRadius, color with { A = 0.35f });
        canvas.DrawArc(muzzle, ModelGizmo.MuzzleRadius, 0f, Mathf.Tau, 20, color, 1.5f);

        // Направление выстрела показывается осью самой точки вылета: развернув её,
        // художник видит, куда пойдёт след, не запуская игру
        float angle = Muzzle.GlobalRotation - canvas.GlobalRotation;
        ModelGizmo.Arrow(canvas, muzzle, angle, ModelGizmo.AxisLength * 0.6f, color);
    }
}
