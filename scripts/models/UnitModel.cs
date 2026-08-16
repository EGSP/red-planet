using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Корень графического определения сущности: одна сцена на вид, в которой лежит всё
/// изображение — корпус, инструменты, точки вылета, слои затенения, порядок наложения.
///
/// ЗАЧЕМ ОТДЕЛЬНАЯ СЦЕНА. Прежние ключи <c>sprite</c> и <c>hull</c> описывали ровно одно
/// изображение с одним поворотом, и составную машину — корпус с отдельно поворачивающимся
/// стволом — ими было не выразить. Продолжать наращивать поля значило бы повторять в своём
/// формате дерево узлов Godot вместе с редактором к нему. Сцена даёт то же самое даром:
/// перетаскивание частей, точку отсчёта, порядок наложения через положение в дереве
/// и <c>z_index</c>.
///
/// ГРАНИЦА С СИМУЛЯЦИЕЙ. Модель не хранит ни одной игровой величины: прочность, дальность,
/// скорость и стоимость принадлежат .toml. Единственное, что игровые системы у неё
/// спрашивают, — точка вылета из <see cref="ModelTool"/>, и это выбор места, откуда
/// показывать уже принятое решение, а не само решение. Модели нет — выстрел идёт из центра
/// сущности, и расчёт от этого не меняется.
///
/// СВЯЗЬ С ОПРЕДЕЛЕНИЕМ. Путь к сцене задаётся ключом <c>model</c> в секции <c>[body]</c>
/// и остаётся единственным описанием изображения: другого способа задать вид нет.
/// </summary>
[Tool, GlobalClass]
public partial class UnitModel : Node2D
{
    /// <summary>
    /// Основной спрайт корпуса. Ссылка нужна коду, которому важна именно неподвижная часть,
    /// а не всё дерево; на отрисовку не влияет — рисует себя сам узел.
    /// </summary>
    [Export] public Sprite2D Body { get; set; }

    /// <summary>Показывать ли подсказки в редакторе. На игру не влияет.</summary>
    [Export] public bool ShowGizmo { get; set; } = true;

    /// <summary>
    /// Показывать ли сетку клеток мира под изображением. Отдельным признаком от
    /// <see cref="ShowGizmo"/>: у крупной постройки сетка занимает всё поле редактора,
    /// и, разобравшись с размером, её убирают, оставив ось и центр.
    /// </summary>
    [Export] public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Размер сетки в клетках по каждой оси. Начало координат лежит в середине клетки,
    /// а сетка симметрична, поэтому клеток поперёк выходит на одну больше заданного чётного
    /// числа: при 6 сетка занимает 7 клеток — центральную и по три с каждой стороны.
    /// </summary>
    [Export(PropertyHint.Range, "2,24,2")] public int GridCells { get; set; } = 6;

    private ModelTool[] _tools;
    private ShaderMaterial[] _tinted;

    /// <summary>Имя параметра шейдера, через который передаётся цвет команды.</summary>
    private const string TeamColorParameter = "team_color";

    /// <summary>
    /// Части-инструменты, найденные во всём поддереве. Ищутся один раз при первом
    /// обращении: состав сцены во время игры не меняется.
    /// </summary>
    public ModelTool[] Tools => _tools ??= Collect();

    /// <summary>
    /// Повернуть ВСЕ наводящиеся части на один угол относительно корпуса.
    /// <paramref name="localRadians"/> — разница между осью наведения и осью корпуса.
    ///
    /// Годится там, где инструменты не разделены: изображение в панели строительства,
    /// поле редактора контента, приведение свежей модели к нулю. Носитель в игре
    /// поворачивает каждую часть отдельно — см. <see cref="Bind"/> и <see cref="AimRig"/>.
    ///
    /// Поворот назначается целиком, а не добавляется к тому, что стоит в сцене: иначе
    /// собранный угол зависел бы от того, как художник оставил часть при сохранении.
    /// </summary>
    public void SetToolFacing(float localRadians)
    {
        foreach (var tool in Tools)
            if (tool.FollowsAim)
                tool.Rotation = localRadians;
    }

    /// <summary>
    /// Сопоставить части изображения списку инструментов справочника. Возвращает массив
    /// той же длины и в том же порядке, что <paramref name="tools"/>; null в ячейке
    /// означает, что изображения у этого инструмента в сцене нет.
    ///
    /// ДВА ПРОХОДА, И ПОРЯДОК МЕЖДУ НИМИ ЗНАЧИМ. Сперва разбираются части с заполненным
    /// <see cref="ModelTool.ToolId"/>: указанный художником идентификатор есть прямое
    /// указание, и перебивать его нечем. Затем оставшиеся инструменты получают безымянные
    /// части подходящей роли в порядке следования в сцене. Отсюда правило для художника:
    /// пока инструмент у вида один, идентификатор можно не заполнять, а как только их
    /// стало два — обязательно, иначе связь определяется порядком узлов в дереве и меняется
    /// от перетаскивания.
    /// </summary>
    public ModelTool[] Bind(ToolDefinition[] tools)
    {
        if (tools == null || tools.Length == 0)
            return System.Array.Empty<ModelTool>();

        var bound = new ModelTool[tools.Length];
        var taken = new HashSet<ModelTool>();

        for (int i = 0; i < tools.Length; i++)
        {
            var role = RoleOf(tools[i]);

            foreach (var part in Tools)
                if (!taken.Contains(part) && part.Role == role && part.ToolId == tools[i].Id)
                {
                    bound[i] = part;
                    taken.Add(part);
                    break;
                }
        }

        for (int i = 0; i < tools.Length; i++)
        {
            if (bound[i] != null)
                continue;

            var role = RoleOf(tools[i]);

            foreach (var part in Tools)
                if (!taken.Contains(part) && part.Role == role
                    && string.IsNullOrEmpty(part.ToolId))
                {
                    bound[i] = part;
                    taken.Add(part);
                    break;
                }
        }

        return bound;
    }

    /// <summary>
    /// Какой ролью части изображается инструмент. Деление то же, что у самих определений:
    /// модель собственных видов инструментов не заводит — см. <see cref="ModelToolRole"/>.
    /// </summary>
    public static ModelToolRole RoleOf(ToolDefinition tool) =>
        tool is WeaponDefinition ? ModelToolRole.Weapon : ModelToolRole.BuildArm;

    /// <summary>
    /// Точка вылета в мировых координатах. Ложь означает, что подходящей части в модели нет
    /// и вызывающий обязан взять запасную точку — обычно центр сущности.
    /// </summary>
    public bool TryMuzzle(ModelToolRole role, string toolId, out Vector2 point)
    {
        foreach (var tool in Tools)
            if (tool.Matches(role, toolId))
            {
                point = tool.MuzzleGlobal;
                return true;
            }

        point = GlobalPosition;
        return false;
    }

    /// <summary>
    /// Окрасить части в цвет команды. Затрагивает только те узлы, на которых стоит
    /// материал с шейдером, объявляющим параметр <c>team_color</c>: остальное изображение
    /// остаётся таким, каким нарисовано.
    ///
    /// МАТЕРИАЛ РАЗМНОЖАЕТСЯ. Ресурс материала в сцене один на все её экземпляры, и запись
    /// цвета в него окрасила бы разом всех юнитов этого вида, включая чужих. Поэтому при
    /// первом обращении каждый материал заменяется собственной копией.
    /// </summary>
    public void ApplyTeamColor(Color color)
    {
        _tinted ??= PrepareTinted();

        foreach (var material in _tinted)
            material.SetShaderParameter(TeamColorParameter, color);
    }

    /// <summary>
    /// Переключить все части модели на вещество родителя. Нужно каркасу: шейдер
    /// строительства стоит на слое <see cref="BlueprintLayer"/>, а вещество в Godot есть
    /// свойство узла и на потомков само не распространяется. Признак <c>UseParentMaterial</c>
    /// — единственный способ подчинить всё дерево модели одному шейдеру, не подменяя
    /// материалы частей по одному и не теряя их при достройке.
    /// </summary>
    public void ShareParentMaterial(bool enabled) => ShareParentMaterial(this, enabled);

    private static void ShareParentMaterial(Node node, bool enabled)
    {
        if (node is CanvasItem item)
            item.UseParentMaterial = enabled;

        foreach (var child in node.GetChildren())
            ShareParentMaterial(child, enabled);
    }

    /// <summary>
    /// Габарит изображения в координатах самой модели: объединение прямоугольников всех
    /// спрайтов поддерева.
    ///
    /// ЗАЧЕМ ОН НУЖЕН. Иконка строительной панели и поле редактора контента вписывают
    /// изображение в отведённую площадь, а размер сущности в справочнике для этого не годится:
    /// радиус корпуса меньше видимого размера, поскольку ствол и надстройки намеренно выходят
    /// за него. Спрашивать размер у одного лишь <see cref="Body"/> тоже нельзя — вынесенный
    /// ствол остался бы за границей.
    ///
    /// СЛОИ ЗАТЕНЕНИЯ НЕ УЧИТЫВАЮТСЯ. Изображение тени шире корпуса на запас размытия и
    /// отнесено по направлению света; включи его в габарит — и корпус в ячейке панели стал бы
    /// мельче ровно на величину этого запаса, причём тем заметнее, чем сильнее размытие.
    /// </summary>
    public Rect2 Bounds()
    {
        var result = new Rect2();
        bool any = false;

        Measure(this, Transform2D.Identity, ref result, ref any);

        // Пустая модель всё же занимает место: вписывающему нужен ненулевой размер,
        // иначе множитель обратится в бесконечность
        return any ? result : new Rect2(-Const.Unit * 0.5f, -Const.Unit * 0.5f,
            Const.Unit, Const.Unit);
    }

    /// <summary>
    /// Наибольшее удаление угла габарита от начала координат модели. Именно эту величину
    /// вписывают в половину стороны ячейки: модель поворачивают, и вписывать надо круг,
    /// а не прямоугольник.
    /// </summary>
    public float Extent()
    {
        var bounds = Bounds();

        return Mathf.Max(
            Mathf.Max(bounds.Position.Length(), bounds.End.Length()),
            Mathf.Max(new Vector2(bounds.Position.X, bounds.End.Y).Length(),
                new Vector2(bounds.End.X, bounds.Position.Y).Length()));
    }

    private static void Measure(Node node, Transform2D basis, ref Rect2 result, ref bool any)
    {
        foreach (var child in node.GetChildren())
        {
            var local = child is Node2D placed ? basis * placed.Transform : basis;

            if (child is Sprite2D { Texture: not null, Visible: true } sprite
                and not ModelShade)
            {
                var size = sprite.Texture.GetSize();
                var rect = new Rect2(
                    sprite.Centered ? sprite.Offset - size * 0.5f : sprite.Offset, size);

                Include(local, rect, ref result, ref any);
            }

            Measure(child, local, ref result, ref any);
        }
    }

    /// <summary>
    /// Расширить габарит прямоугольником спрайта. Учитываются все четыре угла после поворота:
    /// повёрнутый прямоугольник занимает больше места, чем его стороны по осям.
    /// </summary>
    private static void Include(Transform2D basis, Rect2 rect, ref Rect2 result, ref bool any)
    {
        Span<Vector2> corners =
        [
            basis * rect.Position,
            basis * new Vector2(rect.End.X, rect.Position.Y),
            basis * rect.End,
            basis * new Vector2(rect.Position.X, rect.End.Y),
        ];

        foreach (var corner in corners)
        {
            if (!any)
            {
                result = new Rect2(corner, Vector2.Zero);
                any = true;
                continue;
            }

            result = result.Expand(corner);
        }
    }

    private ModelTool[] Collect()
    {
        var found = new List<ModelTool>();
        Walk(this, found);
        return found.ToArray();
    }

    private static void Walk(Node node, List<ModelTool> found)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is ModelTool tool)
                found.Add(tool);

            Walk(child, found);
        }
    }

    private ShaderMaterial[] PrepareTinted()
    {
        var materials = new List<ShaderMaterial>();
        PrepareTinted(this, materials);
        return materials.ToArray();
    }

    private static void PrepareTinted(Node node, List<ShaderMaterial> materials)
    {
        if (node is CanvasItem { Material: ShaderMaterial shared } item && Tintable(shared))
        {
            var own = (ShaderMaterial)shared.Duplicate();
            item.Material = own;
            materials.Add(own);
        }

        foreach (var child in node.GetChildren())
            PrepareTinted(child, materials);
    }

    /// <summary>Объявляет ли шейдер параметр цвета команды.</summary>
    private static bool Tintable(ShaderMaterial material)
    {
        if (material.Shader == null)
            return false;

        foreach (var uniform in material.Shader.GetShaderUniformList())
            if (uniform.AsGodotDictionary().TryGetValue("name", out var name)
                && name.AsString() == TeamColorParameter)
                return true;

        return false;
    }

    private ModelLayer _gizmo;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            _gizmo = ModelLayer.Attach(this, PaintGizmo, "Gizmo", ModelLayer.TopZ, internalNode: true);
    }

    public override void _Process(double delta)
    {
        // Подсказки следуют за правкой сцены; в игре узлу считать нечего
        if (Engine.IsEditorHint())
            _gizmo?.QueueRedraw();
        else
            SetProcess(false);
    }

    /// <summary>Подсказка корпуса. Рисует её слой поверх изображения — см. <see cref="ModelLayer"/>.</summary>
    private void PaintGizmo(Node2D canvas)
    {
        if (!ShowGizmo)
            return;

        var color = new Color(0.55f, 1f, 0.6f);

        if (ShowGrid)
            PaintGrid(canvas, color);

        // Центр и ось корпуса: сущность стоит в этой точке и едет в эту сторону,
        // поэтому спрайт корпуса выравнивается по ним, а не по краю картинки
        ModelGizmo.Cross(canvas, Vector2.Zero, ModelGizmo.PivotSize * 1.5f, color);
        ModelGizmo.Arrow(canvas, Vector2.Zero, 0f, ModelGizmo.AxisLength, color);

        // Своя клетка выделена из прочих: по ней сверяется размер рисунка с размером
        // сущности в .toml, тогда как остальная сетка служит счётом клеток
        canvas.DrawRect(
            new Rect2(-CellHalf, -CellHalf, Const.Unit, Const.Unit),
            color with { A = 0.55f }, false, 1f);
    }

    /// <summary>Половина клетки мира. Сетка сдвинута на неё, чтобы центр лежал в клетке.</summary>
    private const float CellHalf = Const.Unit * 0.5f;

    /// <summary>
    /// Сетка клеток мира под изображением.
    ///
    /// ЗАЧЕМ ОНА. Размер рисунка в сцене ничем не связан с размером сущности: масштаб узла
    /// задаётся вручную, а прочность, радиус и форма живут в .toml. Сетка и есть единственная
    /// в сцене мера, по которой видно, во сколько клеток укладывается корпус, — без неё
    /// соответствие проверялось запуском игры.
    ///
    /// ШАГ РАВЕН <see cref="Const.Unit"/>, то есть клетке мира. Начало координат лежит
    /// в СЕРЕДИНЕ клетки, а не на её углу: сущность стоит в точке отсчёта, и клетка,
    /// разрезанная осями на четыре части, размера корпуса не показывала бы.
    ///
    /// РАЗМЕР ПОСТОЯННЫЙ и задан полем <see cref="GridCells"/>. Выводить его из габарита
    /// изображения незачем: сетка служит меркой, а мерка должна быть одинаковой у всех
    /// моделей, иначе размеры двух видов на глаз не сравнить.
    /// </summary>
    private void PaintGrid(Node2D canvas, Color color)
    {
        // Число клеток по сторону от центральной. Начало координат лежит в середине клетки,
        // поэтому клеток поперёк выходит нечётное число: 2 * reach + 1
        int reach = Mathf.Max(GridCells / 2, 1);

        float span = reach * Const.Unit + CellHalf;
        var line = color with { A = 0.18f };

        // Границы клеток проходят по (i + 0.5) клетки; крайняя слева и сверху получается
        // при i = -(reach + 1), крайняя справа и снизу — при i = reach
        for (int i = -(reach + 1); i <= reach; i++)
        {
            float offset = i * Const.Unit + CellHalf;

            canvas.DrawLine(new Vector2(offset, -span), new Vector2(offset, span), line);
            canvas.DrawLine(new Vector2(-span, offset), new Vector2(span, offset), line);
        }
    }
}
