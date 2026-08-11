using Godot;

/// <summary>
/// Слой отрисовки каркаса. Спрайт каркаса вынесен на собственный узел потому, что вещество
/// с шейдером есть свойство узла и действует на все его команды кадра: оставь спрайт в общей
/// отрисовке — и шейдер строительства исказил бы заодно контур габарита, подпись и полосу
/// прочности.
///
/// ПОЧЕМУ СЛОЯ ДВА. Дочерний узел рисуется после родителя целиком, поэтому пометки — контур,
/// подпись, полоса — обязаны переехать на второй узел, идущий следом за спрайтом. Иначе они
/// оказались бы под ним: спрайт при <c>sprite_scale</c> больше единицы выходит за габарит и
/// закрыл бы контур.
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
        };

        owner.AddChild(layer);
        return layer;
    }

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

    public override void _Draw()
    {
        var def = _owner?.Definition;
        if (def == null)
            return;

        var size = new Vector2(def.Size.X, def.Size.Y) * Const.Unit;
        var rect = new Rect2(-size * 0.5f, size);

        if (_slot == Slot.Hull)
            DrawHull(def, rect);
        else
            _owner.DrawMarks(this, rect, size);
    }

    private void DrawHull(UnitDefinition def, Rect2 rect)
    {
        if (string.IsNullOrEmpty(def.Sprite))
            return;

        // При включённом шейдере спрайт подаётся без правки цвета: разделение по готовности
        // и плотность частей целиком на шейдере. При выключенном остаётся прежний приём —
        // непрозрачность растёт вместе с готовностью
        var modulate = GraphicsSettings.Building.Enabled
            ? Colors.White
            : new Color(1f, 1f, 1f, 0.2f + 0.65f * _owner.Ratio);

        SpriteArt.DrawHull(this, def, rect, modulate);
    }
}
