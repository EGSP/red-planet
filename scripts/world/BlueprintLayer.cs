using Godot;

/// <summary>
/// Слой отрисовки каркаса. Модель каркаса вынесена на собственный узел потому, что вещество
/// с шейдером есть свойство узла и действует на всё его поддерево: оставь модель в общей
/// отрисовке — и шейдер строительства исказил бы заодно контур габарита, подпись и полосу
/// прочности.
///
/// ПОЧЕМУ СЛОЯ ДВА. Дочерний узел рисуется после родителя целиком, поэтому пометки — контур,
/// подпись, полоса — обязаны переехать на второй узел, идущий следом за моделью. Иначе они
/// оказались бы под ней: корпус нередко выходит за габарит и закрыл бы контур.
///
/// Порядок задан положением в дереве, а не ZIndex: индексы вывели бы узел из порядка
/// <see cref="WorldLayer"/>, где все слои мира стоят на нуле, и спрятали бы его под саму
/// местность.
/// </summary>
public partial class BlueprintLayer : Node2D
{
    /// <summary>Что рисует слой.</summary>
    public enum Slot
    {
        /// <summary>Спрайт каркаса под шейдером строительства.</summary>
        Hull,

        /// <summary>Контур габарита, подписи и полоса прочности.</summary>
        Marks,
    }

    private const string ShaderPath = "res://resources/shaders/construction.gdshader";

    private Blueprint _owner;
    private Slot _slot;
    private ShaderMaterial _material;

    /// <summary>
    /// Сглаженный признак работы: строитель подключается мгновенно, и мгновенная остановка
    /// волнения читалась бы как рывок.
    /// </summary>
    private float _activity;

    /// <summary>Создать слой и подвесить его к каркасу.</summary>
    public static BlueprintLayer Attach(Blueprint owner, Slot slot)
    {
        var layer = new BlueprintLayer
        {
            Name = slot == Slot.Hull ? "Hull" : "Marks",
            _owner = owner,
            _slot = slot,

            // Пометки обязаны лежать выше корпуса, а корпус разложен по уровням ради
            // объединения вызовов отрисовки: одного положения в дереве уже недостаточно
            ZIndex = slot == Slot.Hull ? 0 : Playground.MarksZ,
        };

        owner.AddChild(layer);
        return layer;
    }

    /// <summary>
    /// Поднять сцену изображения под шейдер строительства. Модель становится потомком слоя
    /// и переключается на его вещество: шейдер строительства обязан действовать на весь
    /// корпус целиком, а вещество в Godot есть свойство узла и на потомков само
    /// не распространяется — см. <see cref="UnitModel.ShareParentMaterial"/>.
    ///
    /// Угол корпуса слой уже держит, поэтому модель поворота не получает: каркас стоит так же,
    /// как встанет постройка.
    /// </summary>
    public void AttachModel(UnitDefinition def)
    {
        if (_slot != Slot.Hull || def is not { HasModel: true })
            return;

        var model = UnitModel.Realize(ModelBake.For(def));

        if (model == null)
            return;

        // Слои затенения каркасу не полагаются: тень принадлежит стоящему корпусу, а под
        // шейдером строительства она вдобавок получала бы собственную сетку, отчего силуэт
        // каркаса двоился. Снимаются они, а не прячутся: ModelShade собирает себя заново
        // при всякой правке настроек и невидимость бы себе вернул
        model.DropShades();

        model.ShareParentMaterial(true);
        AddChild(model);

        // Габарит нужен шейдеру: уровень готовности идёт по всей модели, а не по каждой
        // её части отдельно. Меряется до подчинения веществу, но после снятия теней —
        // тень отнесена от корпуса и растянула бы габарит на величину отхода
        _bounds = model.Bounds();
    }

    /// <summary>
    /// Габарит поднятой модели в осях слоя. Служит шейдеру мерой, в которой считается
    /// уровень готовности. Пусто, если модели нет: рисовать тогда нечего.
    /// </summary>
    private Rect2? _bounds;

    /// <summary>
    /// Согласовать слой с каркасом: угол корпуса, признак работы и параметры шейдера.
    /// Вызывается каждый кадр из <see cref="Blueprint"/>, поскольку готовность и активность
    /// меняются непрерывно.
    /// </summary>
    public void Sync(double delta)
    {
        if (_owner == null)
            return;

        float wanted = _owner.BodyFacing - _owner.Rotation;
        if (!Mathf.IsEqualApprox(Rotation, wanted))
            Rotation = wanted;

        QueueRedraw();

        if (_slot != Slot.Hull)
            return;

        var settings = GraphicsSettings.Building;
        float target = _owner.WorkerCount > 0 ? 1f : 0f;
        float step = (float)delta / Mathf.Max(settings.ActivityFade, 0.01f);
        _activity = Mathf.MoveToward(_activity, target, step);

        Apply(settings);
    }

    /// <summary>
    /// Подставить шейдеру настройки и состояние каркаса. Вещество создаётся при первом
    /// вызове и принадлежит этому каркасу: доля готовности у каждого своя, поэтому общее
    /// вещество на всех не годится.
    /// </summary>
    private void Apply(ConstructionSettings settings)
    {
        if (!settings.Enabled)
        {
            Material = null;
            return;
        }

        if (_material == null)
        {
            var shader = GD.Load<Shader>(ShaderPath);

            if (shader == null)
            {
                GD.PushWarning($"[BlueprintLayer] нет шейдера {ShaderPath}");
                return;
            }

            _material = new ShaderMaterial { Shader = shader };
        }

        if (Material != _material)
            Material = _material;

        // Оси слоя и габарит модели: по ним шейдер переводит мировое положение точки
        // в общую для всех частей систему отсчёта — см. construction.gdshader
        var bounds = _bounds ?? Footprint();

        _material.SetShaderParameter("hull_origin", GlobalPosition);
        _material.SetShaderParameter("hull_axis", Vector2.Right.Rotated(GlobalRotation));
        _material.SetShaderParameter("hull_min", bounds.Position);
        _material.SetShaderParameter("hull_size", bounds.Size);

        _material.SetShaderParameter("progress", _owner.Ratio);
        _material.SetShaderParameter("activity", _activity);
        _material.SetShaderParameter("wire_color", settings.Wire);
        _material.SetShaderParameter("edge_color", settings.Edge);
        _material.SetShaderParameter("built_opacity", settings.BuiltOpacity);
        _material.SetShaderParameter("edge_width", settings.EdgeWidth);
        _material.SetShaderParameter("scan_count", settings.ScanCount);
        _material.SetShaderParameter("scan_strength", settings.ScanStrength);
        _material.SetShaderParameter("scan_speed", settings.ScanSpeed);
        _material.SetShaderParameter("wave_amplitude", settings.WaveAmplitude);
        _material.SetShaderParameter("wave_frequency", settings.WaveFrequency);
        _material.SetShaderParameter("wave_speed", settings.WaveSpeed);
    }

    /// <summary>
    /// Запасной габарит — клетки, занятые постройкой. Годится там, где модели нет вовсе:
    /// шейдеру нужна мера в любом случае, а без модели рисовать всё равно нечего.
    /// </summary>
    private Rect2 Footprint()
    {
        var def = _owner?.Definition;

        if (def == null)
            return new Rect2(-Const.Unit * 0.5f, -Const.Unit * 0.5f, Const.Unit, Const.Unit);

        var size = new Vector2(def.Size.X, def.Size.Y) * Const.Unit;
        return new Rect2(-size * 0.5f, size);
    }

    public override void _Draw()
    {
        var def = _owner?.Definition;

        // Слой корпуса собственных команд отрисовки не имеет вовсе: изображение целиком
        // принадлежит экземпляру сцены модели, подвешенному к этому слою и подчинённому
        // его веществу — см. AttachModel. Узел нужен ради вещества и порядка, а не рисунка
        if (def == null || _slot == Slot.Hull)
            return;

        var size = new Vector2(def.Size.X, def.Size.Y) * Const.Unit;
        _owner.DrawMarks(this, new Rect2(-size * 0.5f, size), size);
    }
}
