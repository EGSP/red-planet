using Godot;

/// <summary>
/// План постройки: намерение игрока, а не строение. Появляется в мире по щелчку и живёт
/// до тех пор, пока исполнитель не дойдёт до места и не поставит на нём каркас.
///
/// ЧЕМ ПЛАН ОТЛИЧАЕТСЯ ОТ КАРКАСА. Каркас — материальный объект: он занимает место,
/// его можно разбить, он тянет метал из хранилища. План неосязаем: ходьбе он не мешает,
/// прочности у него нет, атаковать его нечем и незачем. Единственное, на что он влияет, —
/// на постановку СЛЕДУЮЩИХ планов и строений: размеченное место больше не предлагается
/// под другую постройку.
///
/// ЗАЧЕМ ЭТО НУЖНО. Раньше каркас возникал в любой точке карты мгновенно, поэтому стену
/// перед наступающим противником можно было поставить, не имея там ни одного юнита.
/// С планом застройка становится следствием присутствия: сначала исполнитель доходит,
/// и только потом на месте появляется то, что можно обстрелять.
///
/// ПЛАН — НЕ ПРИКАЗ, хотя и рождается из него. Приказ живёт в очереди одного исполнителя
/// и исчезает вместе с ним; план переживает гибель того, кому был назначен, и за него
/// берётся любой, кому его поручат. Поэтому план — сущность мира, а приказ на неё —
/// обычный <see cref="OrderKind.Build"/>.
/// </summary>
public partial class BuildPlan : Node2D, IWorkSite, IFacing
{
    public int Id { get; set; }

    public UnitDefinition Definition { get; private set; }

    /// <summary>Угол, под которым план размечен. Каркас и постройка его унаследуют.</summary>
    public float BodyFacing { get; private set; }

    /// <summary>
    /// Сцена каркаса. План носит её с собой, потому что каркас ставит он сам, а взять
    /// сцену в этот миг больше неоткуда: <see cref="CommandSystem"/> к тому времени
    /// занят уже другим, и спрашивать её у системы означало бы связать сущность
    /// с системой ввода.
    /// </summary>
    public PackedScene FrameScene;

    /// <summary>План израсходован: каркас поставлен либо место оказалось занято.</summary>
    private bool _spent;

    /// <summary>
    /// Место работы сменилось каркасом. Подписаны на объявление сами приказы, нацеленные
    /// на этот план: список подписчиков и есть обратная связь «место работы — приказы»,
    /// без которой их пришлось бы искать перебором всех веток приказов.
    ///
    /// Отменённый план объявления не делает: преемника у него нет, и приказы на него
    /// становятся невыполнимыми сами — цели больше нет.
    /// </summary>
    public event System.Action<IWorkSite> Superseded;

    public float Facing => BodyFacing;

    /// <summary>
    /// Место, которое план держит за собой. В карту препятствий не попадает: занятость
    /// там означает «здесь стоит нечто твёрдое», а план не твёрд. Читает эту форму только
    /// <see cref="Placement"/>.
    /// </summary>
    public Obb Footprint => Definition == null
        ? new Obb(GlobalPosition, Vector2.Zero)
        : Placement.Footprint(Definition, GlobalPosition, BodyFacing);

    public bool NeedsWork => !_spent && Definition != null;

    public void Init(int id, UnitDefinition def, Vector2 center, float facing, PackedScene frame)
    {
        Id = id;
        Definition = def;
        Position = center;
        BodyFacing = facing;
        FrameScene = frame;
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>
    /// Исполнитель дошёл: поставить каркас и уйти из мира.
    ///
    /// ПОЧЕМУ ПРОВЕРКА МЕСТА ЗДЕСЬ ПОВТОРЯЕТСЯ. Между разметкой и приходом исполнителя
    /// проходит время, и за это время место могло быть застроено: план его не занимал.
    /// Случай редкий, поскольку правило постановки планы учитывает, но не невозможный:
    /// план тихо отменяется, а очередь исполнителя идёт дальше.
    ///
    /// ПРИКАЗ НЕ ЗАМЕНЯЕТСЯ И НЕ ДОПИСЫВАЕТСЯ — у него переезжает цель. План объявляет,
    /// что сменился каркасом, а нацеленный сюда приказ переводит цель сам (см.
    /// <see cref="IWorkSite.Superseded"/>). Приказ при этом остаётся ОДНИМ объектом на всех,
    /// на прежнем месте во всех ветках, где он значится: состояние исполнения у него общее,
    /// поэтому подошедшие следом узнают, что работа уже начата, и включаются в неё.
    /// </summary>
    public void Realize()
    {
        if (_spent)
            return;

        _spent = true;

        var gm = GameManager.I;

        if (!Placement.CanPlace(gm, Definition, GlobalPosition, BodyFacing, ignorePlan: this))
        {
            gm.Events.Append(new BuildPlanCancelled
            {
                EntityId = Id,
                DefinitionId = Definition.Id,
                Pos = GlobalPosition,
            });

            Retire();
            return;
        }

        var frame = gm.Spawn.SpawnBlueprint(FrameScene, Definition, GlobalPosition, BodyFacing);

        gm.Events.Append(new BlueprintPlaced
        {
            EntityId = frame.Id,
            DefinitionId = Definition.Id,
            Pos = GlobalPosition,
            Facing = BodyFacing,
        });

        Superseded?.Invoke(frame);

        Retire();
    }

    /// <summary>
    /// План уходит из игры. Приказы на него станут невыполнимы сами: цели больше нет,
    /// и <see cref="OrderQueue.DropInvalid"/> снимет их с головы очередей.
    /// </summary>
    private void Retire()
    {
        _spent = true;
        SetProcess(false);
        Visible = false;
        QueueFree();
    }

    /// <summary>
    /// Бело-серый полупрозрачный контур. Цвет выбран именно бесцветным: план не должен
    /// читаться как каркас, у которого своя зелень стройки, и не должен выглядеть
    /// материальным.
    /// </summary>
    public override void _Draw()
    {
        if (Definition == null)
            return;

        var size = new Vector2(Definition.Size.X, Definition.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        DrawSetTransform(Vector2.Zero, BodyFacing, Vector2.One);

        ShapeDraw.Rect(this, rect, ShapeStyle.Solid(new Color(0.85f, 0.88f, 0.92f, 0.08f)));
        ShapeDraw.Rect(this, rect,
            ShapeStyle.Outline(new Color(0.85f, 0.88f, 0.92f, 0.45f), 1.5f, WidthMode.Screen));

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        DrawString(ThemeDB.FallbackFont, new Vector2(rect.Position.X, rect.Position.Y - 6f),
            Definition.DisplayName, HorizontalAlignment.Left, -1, 12,
            new Color(0.85f, 0.88f, 0.92f, 0.6f));
    }
}
