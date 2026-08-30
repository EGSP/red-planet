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
/// ДВА СОСТОЯНИЯ ОДНОГО УЗЛА. В редакторе сцена открыта целиком: спрайты, точки вылета
/// и объявления эффектов стоят узлами, и художник правит их мышью. В игре узел получается
/// иначе — копированием образца из <see cref="ModelBake"/>, где от сцены остались только
/// работающие узлы, а картинки стали объектами отрисовки сервера. Отсюда правило: всё, что
/// относится к запечённой модели, спрашивает <see cref="Bake"/> и молчит, когда его нет.
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
    /// Основной спрайт корпуса. Ссылка нужна слоям затенения, выводящим себя из его альфы;
    /// в запечённой модели узла уже нет, и поле остаётся значимым только в редакторе.
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

    /// <summary>Запечённая модель, из которой поднят экземпляр. Null в открытой сцене редактора.</summary>
    public ModelBake Bake { get; private set; }

    /// <summary>Путь к сцене модели. Служит признаком того, что вид сменился.</summary>
    public string Source => Bake?.Source ?? SceneFilePath;

    private ModelTool[] _tools;
    private float _health = float.NaN;

    /// <summary>
    /// Объекты отрисовки, принимающие величины сущности: те, чьё вещество объявляет параметр
    /// экземпляра <see cref="InstanceParam.TeamColor"/>. Цвет команды и доля прочности
    /// объявлены одним шейдером, поэтому перечень у них общий.
    /// </summary>
    private readonly List<Rid> _painted = new();

    /// <summary>
    /// Модели, введённые в дерево. Нужны пересборке теней: настройки затенения правит
    /// отладочная панель, и обойти перечень тех, кого правка касается, дешевле, чем держать
    /// обработку кадра у каждой машины ради сверки одного числа.
    ///
    /// Перечень статический потому, что правка приходит извне дерева сцены и адресована
    /// сразу всем; в редакторе он не ведётся вовсе — там модель печётся заново при всяком
    /// обращении.
    /// </summary>
    private static readonly List<UnitModel> Shown = new();

    /// <summary>Объекты отрисовки, созданные под картинки. Освобождаются при выходе из дерева.</summary>
    private readonly List<Rid> _items = new();

    /// <summary>Картинки, зависящие от угла корпуса: слои затенения с отходом.</summary>
    private readonly List<(Rid Item, ModelSprite Piece)> _shifted = new();

    /// <summary>
    /// Ключи картинок, на тех же местах, что и <see cref="_items"/>. Хранятся ради
    /// переназначения уровней: раскладка пересобирается, когда разобран новый вид,
    /// и номера уровней у прежних картинок сдвигаются — см. <see cref="Restack"/>.
    /// </summary>
    private readonly List<MaterialOrderBalancer.Key> _keys = new();

    /// <summary>
    /// Узлы, пережившие запекание и получившие собственный уровень. Ключ свой они помнят
    /// пометкой <see cref="ModelBaker.LevelMeta"/>.
    /// </summary>
    private readonly List<CanvasItem> _staged = new();

    /// <summary>Номер раскладки, по которой уровни назначались в последний раз.</summary>
    private int _revision = -1;

    /// <summary>
    /// Свести изображение на один уровень. Нужно вне мира — иконке панели и полю редактора
    /// контента: там холст общий с интерфейсом, а уровни раскладки заданы в мерах слоя мира
    /// и вывели бы картинки машины поверх окружающих их панелей. Внутри одного уровня порядок
    /// задан деревом, поэтому изображение остаётся правильным; объединение вызовов там
    /// не нужно, поскольку иконок единицы.
    /// </summary>
    private bool _flat;

    /// <summary>
    /// Основание порядка отрисовки картинок. Всё, что осталось узлами, движок нумерует
    /// от нуля вверх, поэтому картинки нумеруются ниже нуля.
    /// </summary>
    private const int DrawBase = -4096;

    /// <summary>Угол корпуса, при котором отход теней считался в последний раз.</summary>
    private float _aligned = float.NaN;

    /// <summary>
    /// Цвет команды, назначенный владельцем. Помнится потому, что вещества создаются заново
    /// при всяком вводе в дерево, а окраску владелец назначает один раз при рождении.
    /// </summary>
    private Color _team = Colors.White;

    /// <summary>Показывать ли слои затенения. Каркас строительства их не показывает.</summary>
    private bool _shades = true;

    /// <summary>Подчинены ли картинки веществу родителя. Нужно каркасу строительства.</summary>
    private bool _shared;

    /// <summary>
    /// Поднять экземпляр по запечённой модели. Узел возвращается вне дерева: объекты
    /// отрисовки создаются при вводе в дерево, когда у владельца уже есть холст.
    /// </summary>
    public static UnitModel Realize(ModelBake bake)
    {
        if (bake?.Template == null || !Alive.Is(bake.Template))
            return null;

        // Копирование БЕЗ повторного подъёма сцены: признак UseInstantiation вернул бы
        // узлы из файла .tscn, то есть отменил бы запекание и вернул в дерево все спрайты
        const int flags = (int)(DuplicateFlags.Signals | DuplicateFlags.Groups
                                | DuplicateFlags.Scripts);

        if (bake.Template.Duplicate(flags) is not UnitModel model)
        {
            GD.PushWarning($"[UnitModel] образец не скопирован: {bake.Source}");
            return null;
        }

        model.Adopt(bake);

        // Разбор нового вида пересобирает раскладку и сдвигает уровни у прежних картинок.
        // Здесь и есть то место, где это становится известно: машина рождается сразу после
        // того, как её вид испечён
        Restack();

        return model;
    }

    /// <summary>Принять запечённую модель: раздать частям их данные и запомнить её.</summary>
    private void Adopt(ModelBake bake)
    {
        Bake = bake;

        var parts = Tools;

        if (parts.Length != bake.Tools.Length)
        {
            GD.PushWarning($"[UnitModel] частей в образце {parts.Length}, " +
                           $"а в запечённой модели {bake.Tools.Length}: {bake.Source}");
            return;
        }

        for (int i = 0; i < parts.Length; i++)
            parts[i].Adopt(bake.Tools[i]);
    }

    public override void _EnterTree() => Build();

    public override void _ExitTree() => Release();

    /// <summary>
    /// Создать объекты отрисовки под картинки запечённой модели.
    ///
    /// ПОЧЕМУ ЭТО НЕ УЗЛЫ. Спрайт после рождения ничего не решает: текстура, место и порядок
    /// наложения заданы сценой и до гибели машины не меняются. Объект отрисовки сервера
    /// заполняется один раз и наследует преобразование от владельца, поэтому движение машины
    /// не стоит ни одной записи на картинку — сервер применяет преобразование сам.
    ///
    /// ВЛАДЕЛЕЦ КАРТИНКИ — корень модели либо часть-инструмент: поворот ствола обязан вести
    /// за собой его изображение, а корпус к развороту ствола безразличен.
    /// </summary>
    private void Build()
    {
        if (Bake == null || _items.Count > 0)
            return;

        int index = 0;

        foreach (var piece in Bake.Body)
            Add(piece, GetCanvasItem(), ref index);

        var parts = Tools;

        for (int i = 0; i < Bake.Tools.Length && i < parts.Length; i++)
        {
            int inner = 0;

            foreach (var piece in Bake.Tools[i].Pieces)
                Add(piece, parts[i].GetCanvasItem(), ref inner);
        }

        // Узлы, пережившие запекание, — луч работы и частицы — принимают величины сущности
        // тем же порядком: вещество у них общее со всеми носителями вида
        CollectPainted(this);
        CollectStaged(this);

        _revision = ModelBaker.Revision;

        _health = float.NaN;

        ApplyTeamColor(_team);
        _aligned = float.NaN;

        Align();

        // Обработка кадра остаётся только редактору, где узел ведёт подсказки. В игре модель
        // за кадр не делает ничего: пересборку теней приносит UnitModel.Rebake
        SetProcess(Engine.IsEditorHint());
        SetNotifyTransform(_shifted.Count > 0);

        if (!Engine.IsEditorHint())
            Shown.Add(this);
    }

    /// <summary>Создать один объект отрисовки под картинку.</summary>
    private void Add(in ModelSprite piece, Rid parent, ref int index)
    {
        if (piece.Shade && !_shades)
            return;

        var item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(item, parent);
        RenderingServer.CanvasItemSetTransform(item, piece.Local);

        // Уровень, а не авторский z_index: одинаковые картинки всех машин обязаны лечь
        // на общий уровень, иначе объединение вызовов отрисовки не работает —
        // см. MaterialOrderBalancer
        RenderingServer.CanvasItemSetZIndex(item, Depth(piece.Key));

        // Порядок среди равных ZIndex. Отсчёт ведётся от отрицательного основания, потому
        // что узлам, пережившим запекание, движок назначает порядковый номер по их месту
        // в дереве, то есть неотрицательный: вспышка выстрела и луч работы обязаны лежать
        // поверх картинок своей части, а не под ними
        RenderingServer.CanvasItemSetDrawIndex(item, DrawBase + index++);

        RenderingServer.CanvasItemSetDefaultTextureFilter(item,
            (RenderingServer.CanvasItemTextureFilter)(int)piece.Filter);

        if (_shared)
        {
            RenderingServer.CanvasItemSetUseParentMaterial(item, true);
        }
        else if (piece.Material != null)
        {
            // Вещество назначается ОБЩЕЕ: ресурс в сцене один на все экземпляры вида,
            // и копия на машину отменила бы объединение вызовов отрисовки
            RenderingServer.CanvasItemSetMaterial(item, piece.Material.GetRid());

            if (InstanceParam.Declares(piece.Material, InstanceParam.TeamColor))
                _painted.Add(item);
        }

        RenderingServer.CanvasItemAddTextureRect(item, piece.Rect, piece.Texture.GetRid(),
            false, piece.Modulate);

        _items.Add(item);
        _keys.Add(piece.Key);

        if (piece.Shift != Vector2.Zero)
            _shifted.Add((item, piece));
    }

    /// <summary>Освободить объекты отрисовки. Сервер владеет ими сам, дерево сцены их не убирает.</summary>
    private void Release()
    {
        foreach (var item in _items)
            RenderingServer.FreeRid(item);

        _items.Clear();
        _keys.Clear();
        _shifted.Clear();
        _painted.Clear();
        _staged.Clear();

        Shown.Remove(this);
    }

    /// <summary>
    /// Обработка кадра остаётся редактору, где подсказки следуют за правкой сцены.
    /// В игре она выключена — см. <see cref="Build"/>.
    /// </summary>
    public override void _Process(double delta) => _gizmo?.QueueRedraw();

    /// <summary>
    /// Пересобрать слои затенения у всех показанных моделей. Зовётся отладочной панелью
    /// после правки настроек затенения — см. <see cref="ShadingSettings"/>.
    ///
    /// ПОЧЕМУ ИЗВЕЩЕНИЕМ, А НЕ СВЕРКОЙ КАЖДЫЙ КАДР. Прежде всякая модель держала обработку
    /// кадра и сверяла слепок настроек; при полутора сотнях машин это давало около шести
    /// процентов времени главного потока — плату за переход границы ради сравнения одного
    /// числа. Настройки же меняются лишь рукой в отладочной панели, то есть считанные разы
    /// за партию.
    ///
    /// СЛОИ ПЕЧУТСЯ ИЗ АЛЬФЫ КОРПУСА и потому пересобираются целиком, а не правятся по месту;
    /// сама выпечка идёт один раз на вид, поскольку запечённая модель хранится в определении.
    /// </summary>
    public static void Rebake()
    {
        if (Shown.Count == 0)
            return;

        // Слепок настроек снимается один раз за кадр, а правка пришла в этом же кадре:
        // без сброса сверка вернула бы величину, снятую до неё
        ModelBake.Restamp();

        foreach (var model in Shown.ToArray())
        {
            if (model.Bake is not { Stale: true })
                continue;

            var fresh = ModelBake.Refresh(model.Bake);

            if (ReferenceEquals(fresh, model.Bake))
                continue;

            model.Release();
            model.Adopt(fresh);
            model.Build();
        }

        // Виды пекутся по одному, и раскладка пересобирается на каждом: у моделей, собранных
        // в начале обхода, уровни остались от промежуточной раскладки
        Restack();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged)
            Align();
    }

    /// <summary>
    /// Поставить слои затенения по углу корпуса. Отход тени задан в мировых осях, поскольку
    /// источник света на карте один; отсюда следует, что у повёрнутой машины тень уходит
    /// в ту же сторону, что и у неповёрнутой, и место её зависит от угла.
    ///
    /// Сравнение с прежним углом снимает работу с неподвижных: у постройки угол не меняется
    /// никогда, и сверка двух чисел заменяет запись во все слои.
    /// </summary>
    private void Align()
    {
        if (_shifted.Count == 0)
            return;

        float world = GlobalRotation;

        if (Mathf.IsEqualApprox(world, _aligned))
            return;

        _aligned = world;

        foreach (var (item, piece) in _shifted)
            RenderingServer.CanvasItemSetTransform(item, piece.Placed(world));
    }

    /// <summary>
    /// Части-инструменты, найденные во всём поддереве. Ищутся один раз при первом
    /// обращении: состав узлов после запекания не меняется.
    /// </summary>
    public ModelTool[] Tools => _tools ??= Collect();

    /// <summary>
    /// Повернуть ВСЕ наводящиеся части на один угол относительно корпуса.
    /// <paramref name="localRadians"/> — разница между осью наведения и осью корпуса.
    ///
    /// Годится там, где инструменты не разделены: изображение в панели строительства,
    /// поле редактора контента, приведение свежей модели к нулю. Носитель в игре
    /// поворачивает каждую часть отдельно — см. <see cref="AimRig"/>.
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
            return Array.Empty<ModelTool>();

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
    /// Окрасить части в цвет команды. Затрагивает только те картинки и узлы, на которых
    /// стоит вещество с шейдером, объявляющим параметр экземпляра <c>team_color</c>:
    /// остальное изображение остаётся таким, каким нарисовано.
    ///
    /// Цвет пишется объекту отрисовки, а не веществу: вещество одно на вид, и запись в него
    /// окрасила бы разом всех носителей — см. <see cref="InstanceParam"/>.
    /// </summary>
    public void ApplyTeamColor(Color color)
    {
        _team = color;

        foreach (var item in _painted)
            InstanceParam.WriteColor(item, InstanceParam.TeamColor, color);
    }

    /// <summary>
    /// Передать долю прочности слою повреждения: 1 — целая сущность, 0 — вскрытая целиком.
    ///
    /// ЗАЧЕМ ЭТО ИДЁТ КОДОМ, А ВСЁ ОСТАЛЬНОЕ В СЛОЕ ПОВРЕЖДЕНИЯ — ВЕЩЕСТВОМ. Текстура
    /// внутренностей, карта порядка вскрытия и цвет каймы принадлежат виду и выбираются
    /// веществом в сцене модели. Прочность же принадлежит сущности и у двух машин одного
    /// вида разная, поэтому веществом её задать нельзя вовсе: она идёт параметром
    /// экземпляра — см. <see cref="InstanceParam"/>.
    ///
    /// ЗНАЧЕНИЕ СВЕРЯЕТСЯ С ПРЕЖНИМ. Метод зовут каждый кадр, а прочность меняется редко;
    /// запись идёт через движок, и платить за неё на каждом кадре за каждую сущность незачем.
    /// </summary>
    public void ApplyDamage(float health)
    {
        health = Mathf.Clamp(health, 0f, 1f);

        if (Mathf.IsEqualApprox(health, _health) || _painted.Count == 0)
            return;

        _health = health;

        foreach (var item in _painted)
            InstanceParam.Write(item, InstanceParam.Health, health);
    }

    /// <summary>
    /// Переключить всю модель на вещество родителя. Нужно каркасу: шейдер строительства
    /// стоит на слое <see cref="BlueprintLayer"/>, а вещество в Godot есть свойство узла
    /// либо объекта отрисовки и на потомков само не распространяется.
    ///
    /// Зовётся ДО ввода модели в дерево: картинки создаются с уже назначенным признаком.
    /// </summary>
    public void ShareParentMaterial(bool enabled)
    {
        _shared = enabled;

        ShareParentMaterial(this, enabled);

        foreach (var item in _items)
            RenderingServer.CanvasItemSetUseParentMaterial(item, enabled);
    }

    /// <summary>
    /// Не показывать слои затенения. Нужно каркасу: тень принадлежит стоящему корпусу,
    /// а под шейдером строительства она вдобавок получает собственную сетку, отчего силуэт
    /// каркаса двоится.
    ///
    /// Зовётся ДО ввода модели в дерево: слои не создаются вовсе, а не прячутся.
    /// </summary>
    public void DropShades() => _shades = false;

    /// <summary>
    /// Пустая модель. Отвечает за узел, у которого запечённой модели нет вовсе: сцену
    /// открыли в редакторе. Величины запаса заданы в одном месте — самой <see cref="ModelBake"/>.
    /// </summary>
    private static readonly ModelBake Empty = new();

    /// <summary>Габарит изображения в осях модели. Считается при запекании — см. ModelBaker.</summary>
    public Rect2 Bounds() => (Bake ?? Empty).Bounds;

    /// <summary>
    /// Наибольшее удаление угла габарита от начала координат модели. Именно эту величину
    /// вписывают в половину стороны ячейки: модель поворачивают, и вписывать надо круг,
    /// а не прямоугольник.
    /// </summary>
    public float Extent() => (Bake ?? Empty).Extent;

    private static void ShareParentMaterial(Node node, bool enabled)
    {
        if (node is CanvasItem item)
            item.UseParentMaterial = enabled;

        foreach (var child in node.GetChildren())
            ShareParentMaterial(child, enabled);
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

    /// <summary>Уровень картинки. У сведённого изображения все картинки лежат на нуле.</summary>
    private int Depth(in MaterialOrderBalancer.Key key) =>
        _flat ? 0 : ModelBaker.Order.Level(key);

    /// <summary>
    /// Переназначить уровни показанным моделям, если раскладка пересобрана. Уровень зависит
    /// от всех разобранных видов сразу, поэтому появление нового вида сдвигает номера
    /// у картинок уже показанных машин.
    ///
    /// Сверка идёт по номеру раскладки, поэтому в спокойном состоянии обход стоит одного
    /// сравнения на машину, а работа возникает лишь при первом рождении машины нового вида.
    /// </summary>
    public static void Restack()
    {
        int revision = ModelBaker.Revision;

        if (_stacked == revision)
            return;

        _stacked = revision;

        foreach (var model in Shown)
            model.Restack(revision);
    }

    /// <summary>Номер раскладки, до которого перечень показанных моделей уже доведён.</summary>
    private static int _stacked = -1;

    private void Restack(int revision)
    {
        if (_revision == revision)
            return;

        _revision = revision;

        for (int i = 0; i < _items.Count; i++)
            RenderingServer.CanvasItemSetZIndex(_items[i], Depth(_keys[i]));

        foreach (var node in _staged)
            if (Alive.Is(node))
                node.ZIndex = Depth(Staged(node));
    }

    /// <summary>
    /// Собрать узлы, получившие при запекании собственный уровень, и поставить им его.
    /// Ключ узла хранится пометкой: копия узла у каждой машины своя, а уровень у них общий.
    /// </summary>
    private void CollectStaged(Node node)
    {
        if (node is CanvasItem item && item.HasMeta(ModelBaker.LevelMeta))
        {
            item.ZIndex = Depth(Staged(item));
            _staged.Add(item);
        }

        foreach (var child in node.GetChildren())
            CollectStaged(child);
    }

    /// <summary>Ключ уцелевшего узла, записанный на нём при запекании.</summary>
    private static MaterialOrderBalancer.Key Staged(CanvasItem item) =>
        MaterialOrderBalancer.Key.Single(item.GetMeta(ModelBaker.LevelMeta).AsUInt64());

    /// <summary>
    /// Свести изображение на один уровень — см. <see cref="_flat"/>. Зовётся ДО ввода
    /// модели в дерево: картинки создаются с уже назначенным уровнем.
    /// </summary>
    public void Flatten() => _flat = true;

    /// <summary>
    /// Собрать узлы, принимающие величины сущности. Вещество им НЕ размножается: величины
    /// пишутся параметром экземпляра прямо в объект отрисовки узла.
    /// </summary>
    private void CollectPainted(Node node)
    {
        if (node is CanvasItem item
            && InstanceParam.Declares(item.Material, InstanceParam.TeamColor))
            _painted.Add(item.GetCanvasItem());

        foreach (var child in node.GetChildren())
            CollectPainted(child);
    }

    private ModelLayer _gizmo;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            _gizmo = ModelLayer.Attach(this, PaintGizmo, "Gizmo", ModelLayer.TopZ, internalNode: true);
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
