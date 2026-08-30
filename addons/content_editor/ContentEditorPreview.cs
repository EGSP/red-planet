using System;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Общее измерительное поле всех открытых вкладок.
///
/// БЕЗ SPAWNER И СЕССИИ. Силуэты рисуются теми же UnitSilhouette / BuildingVisual,
/// что и в игре, но узлы Unit/Building не создаются: нет Health, AI и навигации.
/// Координаты — мировые пиксели (Const.Unit на клетку); экран = world * zoom + pan.
///
/// СРАВНЕНИЕ. Несколько сущностей на одном поле нужны затем, чтобы подобрать радиус
/// и размер footprint рядом с соседом, а не по памяти. Активная вкладка обведена;
/// ShowOnField скрывает объект, не закрывая вкладку.
/// </summary>
[Tool]
public partial class ContentEditorPreview : Control
{
    private ContentEditorStore _store;
    private Vector2 _pan;
    private float _zoom = 0.5f;
    private bool _panning;
    private Vector2 _panFrom;
    private Vector2 _panMouse;
    private OpenEntitySession _dragSession;
    private Vector2 _dragOffset;
    private bool _measureMode;
    private Vector2? _measureA;
    private Vector2? _measureB;
    private bool _viewInitialized;
    private int _knownSessionCount;

    private bool _showGrid = true;
    private bool _showSizes = true;
    private bool _showFootprint = true;
    private bool _showMargin = true;
    private bool _showVision;
    private bool _showAttack;
    private bool _showWork;
    private bool _showAxis = true;

    /// <summary>
    /// Секторы наведения инструментов. Отдельным переключателем от «Attack» и «Work»:
    /// те показывают дальность, а этот — куда инструмент способен повернуться, и смотрят
    /// на них в разное время.
    /// </summary>
    private bool _showAim;

    /// <summary>Слой осей поверх экземпляров моделей — см. <see cref="PaintAxes"/>.</summary>
    private ModelLayer _axes;

    private PanelContainer _toolbarPanel;
    private HFlowContainer _toolbar;
    private Button _fitButton;
    private Button _oneToOneButton;
    private Button _layoutButton;

    public void Bind(ContentEditorStore store)
    {
        _store = store;
        _knownSessionCount = store?.SessionsIn(ContentEditorScope.Entities).Count() ?? 0;
        EnsureToolbar();
        UpdateToolbarState();
        QueueRedraw();
    }

    /// <summary>
    /// Обновить изображение после изменения Store. Первый открытый объект и изменение
    /// числа вкладок автоматически вписываются, но правка поля не меняет камеру.
    /// </summary>
    /// <summary>
    /// Экземпляры сцен изображения по идентификатору вкладки. Поле рисуется в <c>_Draw</c>,
    /// а модель — самостоятельный узел, поэтому её нельзя нарисовать в общем ряду команд:
    /// она добавляется потомком поля и получает положение и масштаб перед отрисовкой.
    ///
    /// СОСТАВ ПЕРЕСМАТРИВАЕТСЯ НЕ В <c>_Draw</c>. Добавление и освобождение узлов посреди
    /// отрисовки родителя Godot не допускает, поэтому состав правится в
    /// <see cref="SyncModels"/> по изменению Store, а отрисовка только расставляет готовое.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<string, UnitModel> _models = new();

    /// <summary>
    /// Экземпляр взрыва, показывающий область поражения активного ствола. Один на всё
    /// поле — см. <see cref="SyncSplash"/>; null означает, что показывать его некому.
    /// </summary>
    private BurstParticles _splash;

    public void RefreshFromStore()
    {
        int count = _store?.SessionsIn(ContentEditorScope.Entities).Count() ?? 0;
        if (count != _knownSessionCount)
        {
            _knownSessionCount = count;
            // Main уже объединяет Store.Changed в deferred-обновление, поэтому здесь
            // можно вписать объекты синхронно. Второй deferred-вызов переживал ResetUi
            // и мог обратиться к уже освобождённому Preview после C#-сборки.
            FitAll();
        }

        SyncModels();
        UpdateToolbarState();
        QueueRedraw();
    }

    /// <summary>
    /// Освободить все экземпляры моделей, чтобы <see cref="SyncModels"/> поднял их заново.
    ///
    /// ЗАЧЕМ ЭТО ОТДЕЛЬНОЕ ДЕЙСТВИЕ. Состав экземпляров сверяется с открытыми вкладками
    /// по пути к сцене, а правка самой сцены путь не меняет: без этого вызова поле держало бы
    /// изображение, разобранное до правки, до самого закрытия вкладки. Вызывается по сигналу
    /// файловой системы редактора — см. <c>ContentEditorMain.CheckExternalChanges</c>.
    /// </summary>
    public void ReloadModels()
    {
        foreach (var model in _models.Values)
            if (Alive.Is(model))
                model.QueueFree();

        _models.Clear();

        // Взрыв поднимается из той же файловой системы и правится так же, как модель
        if (Alive.Is(_splash))
            _splash.QueueFree();

        _splash = null;

        SyncModels();
        QueueRedraw();
    }

    /// <summary>
    /// Привести набор экземпляров моделей в соответствие с открытыми вкладками. Экземпляр
    /// пересоздаётся, когда в определении сменился путь к сцене; вкладка без модели своего
    /// экземпляра не держит вовсе.
    /// </summary>
    private void SyncModels()
    {
        var wanted = new System.Collections.Generic.Dictionary<string, string>();

        if (_store != null)
            foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
            {
                var def = _store.PreviewUnit(session.Id);

                if (def is { HasModel: true })
                    wanted[session.Id] = def.Model;
            }

        foreach (string id in _models.Keys.ToArray())
        {
            var model = _models[id];

            if (Alive.Is(model) && wanted.TryGetValue(id, out string path)
                                && model.Source == path)
                continue;

            if (Alive.Is(model))
                model.QueueFree();

            _models.Remove(id);
        }

        foreach (var (id, path) in wanted)
        {
            if (_models.ContainsKey(id))
                continue;

            var model = UnitModel.Realize(ModelBake.Of(path));

            // Вне мира уровни раскладки не значат ничего: холст общий с интерфейсом,
            // и картинки машины легли бы поверх окружающих панелей
            model?.Flatten();

            if (model == null)
                continue;

            // Собственные подсказки модели на поле выключены: направление показывает
            // общая ось поля, одинаковая у сущностей с моделью и без неё, а точка вылета
            // рассматривается в самой сцене модели
            model.ShowGizmo = false;

            foreach (var tool in model.Tools)
                tool.ShowGizmo = false;

            AddChild(model);
            _models[id] = model;
        }

        SyncSplash();
    }

    /// <summary>
    /// Поднять экземпляр взрыва, если он нужен хоть одной открытой вкладке.
    ///
    /// ЭКЗЕМПЛЯР ОДИН НА ВСЁ ПОЛЕ, а не по одному на вкладку: взрыв показывается только
    /// у активной сущности. Несколько повторяющихся вспышек разом означали бы, что поле
    /// мигает, и подобрать по нему радиус стало бы труднее, а не легче.
    ///
    /// ПОДНИМАЕТСЯ НЕ В <c>_Draw</c> по той же причине, по которой не поднимаются модели:
    /// добавление узлов посреди отрисовки родителя Godot не допускает. Отрисовка только
    /// ставит готовое на место — см. <see cref="PlaceSplash"/>.
    /// </summary>
    private void SyncSplash()
    {
        bool wanted = _store != null
            && _store.SessionsIn(ContentEditorScope.Entities).Any(s => SplashWeapon(s) != null);

        if (!wanted)
        {
            if (Alive.Is(_splash))
                _splash.QueueFree();

            _splash = null;
            return;
        }

        if (Alive.Is(_splash))
            return;

        if (CombatSettings.Active.SplashEffect?.Instantiate() is not BurstParticles burst)
            return;

        // Повтор — тот же признак, которым художник пользуется, открыв сцену эффекта:
        // одноразовая вспышка, проигранная раз, для подбора радиуса бесполезна
        burst.PreviewLoop = true;
        burst.PreviewInterval = 1.4f;

        // Предпросмотр отметины копоти на поле не нужен: она ложится на грунт, которого
        // здесь нет вовсе, а её круги разброса читались бы как ещё одна граница взрыва
        foreach (var stamp in burst.Stamps)
            stamp.ShowGizmo = false;

        AddChild(burst);
        _splash = burst;
    }

    /// <summary>
    /// Ствол вкладки, у которого есть взрыв. Пусто означает, что показывать нечего:
    /// вкладка не про оружие либо оружие бьёт только прямым попаданием.
    /// </summary>
    private WeaponDefinition SplashWeapon(OpenEntitySession session)
    {
        if (session == null || !session.ShowOnField)
            return null;

        var weapon = session.Kind is ContentEntityKind.Weapon
            ? _store.PreviewTool(session.Id) as WeaponDefinition
            : _store.PreviewUnit(session.Id)?.Weapon;

        return weapon is { HasSplash: true } ? weapon : null;
    }

    /// <summary>
    /// Поставить экземпляр модели туда, где рисуется вкладка. Постройка получает угол
    /// из справочника, подвижная сущность — нулевой: поле показывает вид в покое,
    /// а не в движении.
    /// </summary>
    private void PlaceModel(OpenEntitySession session, UnitDefinition def, Vector2 origin,
        float alpha)
    {
        if (!_models.TryGetValue(session.Id, out var model) || !Alive.Is(model))
            return;

        model.Position = origin;
        model.Scale = Vector2.One * _zoom;
        model.Rotation = def.IsStructure ? Mathf.DegToRad(def.FacingDegrees) : 0f;
        model.Modulate = new Color(1f, 1f, 1f, alpha);
        model.SetToolFacing(0f);
        model.ApplyTeamColor(TeamPalette.Player);
        model.Visible = true;
    }

    /// <summary>
    /// Секторы наведения инструментов сущности — каждый от СВОЕЙ точки крепления.
    ///
    /// ПОЧЕМУ ТОЧКА БЕРЁТСЯ У МОДЕЛИ. Сектор отсчитывается от оси вращения инструмента,
    /// а она у вынесенного ствола не совпадает с центром сущности. Рисовать все секторы
    /// из центра значило бы показывать не то, что будет в игре, и настраивать по такой
    /// картинке было бы нельзя. Части связываются со списком <c>tools</c> тем же
    /// <see cref="UnitModel.Bind"/>, каким это делает носитель, поэтому расхождение
    /// между редактором и игрой исключено.
    ///
    /// Модели нет либо части для инструмента в ней не нашлось — сектор рисуется из центра:
    /// именно так поведёт себя и носитель, у которого изображения инструмента нет.
    /// </summary>
    private void DrawAimArcs(OpenEntitySession session, UnitDefinition def, Vector2 origin)
    {
        var tools = def.Tools;

        if (!_showAim || tools == null || tools.Length == 0)
            return;

        _models.TryGetValue(session.Id, out var model);
        bool hasModel = Alive.Is(model);
        var parts = hasModel ? model.Bind(tools) : null;

        float body = def.IsStructure ? Mathf.DegToRad(def.FacingDegrees) : 0f;

        for (int i = 0; i < tools.Length; i++)
        {
            var at = Vector2.Zero;

            if (parts?[i] is { } part && Alive.Is(part))
                at = model.ToLocal(part.GlobalPosition);

            // Масштаб в трансформ НЕ передаётся, а вносится в радиус: иначе он умножал бы
            // и толщину линий, и при отдалении поля границы сектора истончались бы
            // до невидимости — ровно тогда, когда сектор и нужен целиком
            DrawSetTransform(origin + at.Rotated(body) * _zoom, body, Vector2.One);
            AimArcGizmo.Draw(this, tools[i], tools[i].RangePx * _zoom, i);
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>Спрятать все модели перед обходом вкладок: показаны будут только видимые.</summary>
    private void HideModels()
    {
        foreach (var model in _models.Values)
            if (Alive.Is(model))
                model.Visible = false;

        if (Alive.Is(_splash))
        {
            _splash.Visible = false;

            // Спрятанный взрыв и проигрываться не должен: перезапуск частиц каждые
            // полторы секунды при закрытом переключателе есть работа впустую
            _splash.PreviewLoop = false;
        }
    }

    /// <summary>
    /// Поставить взрыв туда же, где гизмо ствола рисует круг области поражения, — на край
    /// дальности по оси ствола. Точка одна на оба изображения: круг показывает границу
    /// области, эффект — то, как она будет выглядеть в бою, и разъехаться они не должны.
    ///
    /// РАЗМЕР СЧИТАЕТСЯ ТЕМ ЖЕ ПЕРЕВОДОМ, что и в игре
    /// (<see cref="CombatSettings.SplashSize"/>), и домножается на приближение поля.
    /// </summary>
    private void PlaceSplash(WeaponDefinition weapon, Vector2 origin, bool active)
    {
        if (!active || !_showAttack || weapon is not { HasSplash: true } || !Alive.Is(_splash))
            return;

        _splash.Position = origin + Vector2.Right * weapon.RangePx * _zoom;
        _splash.Scale = Vector2.One * _zoom * CombatSettings.SplashSize(weapon.SplashRadiusPx);
        _splash.Visible = true;
        _splash.PreviewLoop = true;
    }

    /// <summary>
    /// Ось «вперёд» каждой показанной сущности: откуда у неё нос и куда она поедет.
    ///
    /// РИСУЕТСЯ ОТДЕЛЬНЫМ СЛОЕМ. Экземпляры моделей добавлены потомками поля, а собственные
    /// команды узла Godot выполняет ДО потомков, поэтому ось, нарисованная в <c>_Draw</c>,
    /// оказалась бы под спрайтом корпуса. Слой стоит выше по <c>ZIndex</c> и потому виден
    /// поверх любого изображения.
    ///
    /// Длина оси считается от размера сущности, а не задана числом: у завода в четыре
    /// клетки и у бота в треть клетки одна и та же стрелка означала бы для первого
    /// незаметную чёрточку, а для второго — линию во весь силуэт.
    /// </summary>
    private void PaintAxes(Node2D canvas)
    {
        if (!_showAxis || _store == null)
            return;

        foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
        {
            if (!session.ShowOnField
                || session.Kind is ContentEntityKind.Weapon or ContentEntityKind.WorkTool)
            {
                continue;
            }

            var def = _store.PreviewUnit(session.Id);
            if (def == null)
                continue;

            var origin = WorldToScreen(session.FieldPosition);
            float facing = def.IsStructure ? Mathf.DegToRad(def.FacingDegrees) : 0f;
            float reach = def.IsStructure
                ? Mathf.Max(def.Width, def.Height) * Const.Unit * 0.5f
                : def.RadiusPx;
            float length = Mathf.Max(reach * _zoom * 1.15f, 16f);

            var color = new Color(0.55f, 1f, 0.6f,
                _store.ActiveSession == session ? 0.95f : 0.5f);

            ModelGizmo.Cross(canvas, origin, 5f, color);
            ModelGizmo.Arrow(canvas, origin, facing, length, color);
        }
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        EnsureToolbar();
        _axes = ModelLayer.Attach(this, PaintAxes, "Axes", ModelLayer.TopZ);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            if (!_viewInitialized && Size.X > 0f && Size.Y > 0f)
            {
                _viewInitialized = true;
                _pan = Size * 0.5f;
            }

            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
            {
                ZoomAt(mouse.Position, 1.1f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
            {
                ZoomAt(mouse.Position, 1f / 1.1f);
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.Middle)
            {
                _panning = mouse.Pressed;
                _panFrom = _pan;
                _panMouse = mouse.Position;
                AcceptEvent();
            }
            else if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            {
                if (_measureMode)
                {
                    var world = ScreenToWorld(mouse.Position);
                    if (_measureA == null || _measureB != null)
                    {
                        _measureA = Snap(world);
                        _measureB = null;
                    }
                    else
                        _measureB = Snap(world);

                    QueueRedraw();
                    AcceptEvent();
                    return;
                }

                var hit = HitTest(mouse.Position);
                if (hit != null)
                {
                    _store.Activate(hit.Id);
                    _dragSession = hit;
                    _dragOffset = hit.FieldPosition - ScreenToWorld(mouse.Position);
                    AcceptEvent();
                }
            }
            else if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
            {
                _dragSession = null;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_panning)
            {
                _pan = _panFrom + (motion.Position - _panMouse);
                QueueRedraw();
                AcceptEvent();
            }
            else if (_dragSession != null)
            {
                _dragSession.FieldPosition = Snap(ScreenToWorld(motion.Position) + _dragOffset);
                QueueRedraw();
                AcceptEvent();
            }
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.13f, 0.16f));

        if (_showGrid)
            DrawGrid();

        DrawRulers();

        HideModels();

        if (_store == null)
            return;

        foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
        {
            if (!session.ShowOnField)
                continue;

            bool active = _store.ActiveSession == session;
            DrawEntity(session, active);
        }

        DrawScaleBar();
        DrawMeasure();
        DrawActiveSizes();

        // Слой осей рисуется отдельно и сам о правке поля не узнаёт
        _axes?.QueueRedraw();
    }

    private void EnsureToolbar()
    {
        if (_toolbar != null)
            return;

        _toolbarPanel = new PanelContainer
        {
            AnchorRight = 1f,
            OffsetLeft = 8f,
            OffsetTop = 8f,
            OffsetRight = -8f,
        };
        AddChild(_toolbarPanel);

        // HFlowContainer переносит переключатели при узком поле. Прежний HBox обрезал
        // «Работа», «Показать всё» и 1:1 справа.
        _toolbar = new HFlowContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
        };
        _toolbarPanel.AddChild(_toolbar);

        AddToggle("Grid", _showGrid, v => _showGrid = v);
        AddToggle("Sizes", _showSizes, v => _showSizes = v);
        AddToggle("Cells", _showFootprint, v => _showFootprint = v);
        AddToggle("Margin", _showMargin, v => _showMargin = v);
        AddToggle("Vision", _showVision, v => _showVision = v);
        AddToggle("Attack", _showAttack, v => _showAttack = v);
        AddToggle("Work", _showWork, v => _showWork = v);
        AddToggle("Aim", _showAim, v => _showAim = v);
        AddToggle("Axis", _showAxis, v => _showAxis = v);

        var measure = new Button
        {
            Text = "Ruler",
            ToggleMode = true,
            Icon = ContentEditorTheme.IconAny("Ruler", "ToolRuler"),
            TooltipText = "Measure a distance in cells between two points of the field",
        };
        measure.Toggled += on =>
        {
            _measureMode = on;
            _measureA = _measureB = null;
            QueueRedraw();
        };
        _toolbar.AddChild(measure);

        _fitButton = new Button
        {
            Text = "Fit all",
            Icon = ContentEditorTheme.IconAny("Zoom", "ZoomMore"),
        };
        _fitButton.Pressed += FitAll;
        _toolbar.AddChild(_fitButton);

        _oneToOneButton = new Button
        {
            Text = "1:1",
            Icon = ContentEditorTheme.IconAny("ZoomReset", "Zoom"),
            TooltipText = "Set the field scale to one screen pixel per world pixel",
        };
        _oneToOneButton.Pressed += () =>
        {
            _zoom = 1f;
            QueueRedraw();
        };
        _toolbar.AddChild(_oneToOneButton);

        // «Разложить» переставляет объекты именно этого поля, поэтому кнопка стоит здесь,
        // а не в верхней полосе, где она читалась как действие над всем проектом.
        _layoutButton = new Button
        {
            Text = "Layout",
            Icon = ContentEditorTheme.IconAny("GridContainer", "Grid", "HBoxContainer"),
        };
        _layoutButton.Pressed += () => _store?.LayoutOpenSessions();
        _toolbar.AddChild(_layoutButton);

        UpdateToolbarState();
    }

    /// <summary>
    /// Действия над содержимым поля выключаются, пока поле пусто: нажатие на них
    /// в этом состоянии ничего не изменило бы.
    /// </summary>
    private void UpdateToolbarState()
    {
        if (!IsInstanceValid(_fitButton))
            return;

        int total = _store?.SessionsIn(ContentEditorScope.Entities).Count() ?? 0;
        bool anyVisible = false;
        if (_store != null)
        {
            foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
            {
                if (session.ShowOnField)
                {
                    anyVisible = true;
                    break;
                }
            }
        }

        ContentEditorTheme.SetAction(_fitButton, anyVisible,
            "Fit every shown entity into the field",
            total == 0
                ? "No entity is open"
                : "Every open entity is hidden by “Show on field”");
        ContentEditorTheme.SetAction(_layoutButton, total > 1,
            "Arrange the open entities in rows",
            total == 0 ? "No entity is open" : "Only one entity is open");
    }

    private void AddToggle(string text, bool initial, Action<bool> assign)
    {
        var box = new CheckBox { Text = text, ButtonPressed = initial };
        box.Toggled += on =>
        {
            assign(on);
            QueueRedraw();
        };
        _toolbar.AddChild(box);
    }

    private void DrawEntity(OpenEntitySession session, bool active)
    {
        float alpha = active ? 1f : 0.68f;
        Vector2 origin = WorldToScreen(session.FieldPosition);

        if (session.Kind is ContentEntityKind.Weapon or ContentEntityKind.WorkTool)
        {
            var tool = _store.PreviewTool(session.Id);
            float extent = 10f * _zoom;

            // Изображения у инструмента нет: ствол и манипулятор рисуются частью модели
            // носителя, а сам по себе инструмент есть набор чисел. Кружок показывает его
            // на поле затем, чтобы рядом с ним читались круги дальности
            Color mark = tool is WeaponDefinition
                ? new Color(1f, 0.38f, 0.32f, alpha)
                : new Color(0.38f, 0.85f, 0.55f, alpha);
            DrawCircle(origin, extent, mark);

            if (active)
                DrawArc(origin, extent + 5f, 0f, Mathf.Tau, 40,
                    new Color(1f, 0.9f, 0.3f, 0.8f), 2f, true);

            DrawEntityLabel(origin + new Vector2(extent + 8f, 5f), session, active);

            if (tool is WeaponDefinition weapon && _showAttack)
            {
                DrawSetTransform(origin, 0f, Vector2.One * _zoom);
                WeaponGizmo.Draw(this, weapon, splash: true);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                PlaceSplash(weapon, origin, active);
            }

            // Носителя у вкладки инструмента нет, поэтому ось корпуса берётся нулевой:
            // сектор показан таким, каким он будет у юнита, смотрящего вправо
            if (tool != null && _showAim)
            {
                DrawSetTransform(origin, 0f, Vector2.One);
                AimArcGizmo.Draw(this, tool, tool.RangePx * _zoom);
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            return;
        }

        var def = _store.PreviewUnit(session.Id);
        if (def == null)
            return;

        // Gizmo используют мировые размеры, поэтому для них удобна масштабированная
        // трансформация. Силуэты ниже получают экранный origin и масштаб явно:
        // производственная отрисовка может назначать DrawSetTransform внутри себя.
        DrawSetTransform(origin, 0f, Vector2.One * _zoom);
        if (_showVision)
            VisionGizmo.Draw(this, def.VisionRadiusPx);
        if (_showAttack && def.Weapon != null)
            WeaponGizmo.Draw(this, def.Weapon, splash: true);
        if (_showWork && def.BuildTool != null)
            WorkGizmo.Draw(this, def.WorkRangePx);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        PlaceSplash(def.Weapon, origin, active);

        PlaceModel(session, def, origin, alpha);
        DrawAimArcs(session, def, origin);

        if (def.IsStructure)
        {
            BuildingVisual.Draw(this, def, origin,
                bodyFacing: Mathf.DegToRad(def.FacingDegrees),
                showFootprint: _showFootprint,
                showMargin: _showMargin,
                alpha: alpha,
                presentationScale: _zoom);
        }
        else
        {
            // Запасной круг рисуется только без модели: изображение со сценой собирает
            // сам её экземпляр, добавленный отдельным узлом
            if (!def.HasModel)
                UnitVisual.Draw(this, def, def.RadiusPx * _zoom, origin);

            if (active)
                DrawArc(origin, def.RadiusPx * _zoom + 6f, 0f, Mathf.Tau, 48,
                    new Color(1f, 0.9f, 0.3f, 0.8f), 2f, true);
        }

        float labelOffset = def.IsStructure
            ? Mathf.Max(def.Height, 1) * Const.Unit * _zoom * 0.5f + 18f
            : def.RadiusPx * _zoom + 18f;
        DrawEntityLabel(origin + new Vector2(0, labelOffset), session, active);
    }

    private void DrawEntityLabel(Vector2 position, OpenEntitySession session, bool active)
    {
        string text = active
            ? $"{session.FileName}  ({session.DisplayName})"
            : session.FileName;
        int fontSize = active ? 13 : 12;
        Vector2 textSize = ThemeDB.FallbackFont.GetStringSize(
            text, HorizontalAlignment.Left, -1, fontSize);
        var rect = new Rect2(
            position - new Vector2(4f, textSize.Y),
            textSize + new Vector2(8f, 5f));
        DrawRect(rect, new Color(0.05f, 0.06f, 0.08f, active ? 0.86f : 0.62f), true);
        DrawString(ThemeDB.FallbackFont, position, text,
            HorizontalAlignment.Left, -1, fontSize,
            new Color(0.95f, 0.97f, 1f, active ? 1f : 0.75f));
    }

    private void DrawGrid()
    {
        float step = Const.Unit * _zoom;
        if (step < 4f)
            return;

        var origin = WorldToScreen(Vector2.Zero);
        var color = new Color(1f, 1f, 1f, 0.07f);
        var major = new Color(1f, 1f, 1f, 0.14f);

        float startX = origin.X % step;
        float startY = origin.Y % step;

        for (float x = startX; x < Size.X; x += step)
        {
            int cell = Mathf.RoundToInt((x - origin.X) / step);
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y),
                cell % 5 == 0 ? major : color);
        }

        for (float y = startY; y < Size.Y; y += step)
        {
            int cell = Mathf.RoundToInt((y - origin.Y) / step);
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y),
                cell % 5 == 0 ? major : color);
        }
    }

    private void DrawRulers()
    {
        float step = Const.Unit * _zoom;
        if (step < 8f)
            return;

        var origin = WorldToScreen(Vector2.Zero);
        var bar = new Color(0.08f, 0.09f, 0.11f, 0.92f);
        DrawRect(new Rect2(0, 0, Size.X, 18), bar);
        DrawRect(new Rect2(0, 0, 36, Size.Y), bar);

        for (float x = origin.X % step; x < Size.X; x += step)
        {
            float world = (x - origin.X) / _zoom / Const.Unit;
            DrawLine(new Vector2(x, 0), new Vector2(x, 18), new Color(1, 1, 1, 0.25f));
            if (step >= 24f)
                DrawString(ThemeDB.FallbackFont, new Vector2(x + 2, 14),
                    world.ToString("0", CultureInfo.InvariantCulture),
                    HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.85f, 1f, 0.7f));
        }

        for (float y = origin.Y % step; y < Size.Y; y += step)
        {
            float world = (y - origin.Y) / _zoom / Const.Unit;
            DrawLine(new Vector2(0, y), new Vector2(36, y), new Color(1, 1, 1, 0.25f));
            if (step >= 24f)
                DrawString(ThemeDB.FallbackFont, new Vector2(2, y - 2),
                    world.ToString("0", CultureInfo.InvariantCulture),
                    HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.85f, 1f, 0.7f));
        }
    }

    private void DrawScaleBar()
    {
        float length = Const.Unit * _zoom;
        var pos = new Vector2(48, Size.Y - 28);
        DrawLine(pos, pos + new Vector2(length, 0), Colors.White, 2f);
        DrawLine(pos, pos + new Vector2(0, -6), Colors.White, 2f);
        DrawLine(pos + new Vector2(length, 0), pos + new Vector2(length, -6), Colors.White, 2f);
        DrawString(ThemeDB.FallbackFont, pos + new Vector2(0, -10),
            "1 cell", HorizontalAlignment.Left, -1, 12, Colors.White);
    }

    private void DrawMeasure()
    {
        if (_measureA == null)
            return;

        var a = WorldToScreen(_measureA.Value);
        DrawCircle(a, 4f, new Color(1f, 0.6f, 0.2f));

        if (_measureB == null)
            return;

        var b = WorldToScreen(_measureB.Value);
        DrawLine(a, b, new Color(1f, 0.6f, 0.2f), 2f);
        float dist = _measureA.Value.DistanceTo(_measureB.Value) / Const.Unit;
        DrawString(ThemeDB.FallbackFont, (a + b) * 0.5f,
            $"{dist:0.###} cells", HorizontalAlignment.Left, -1, 13, new Color(1f, 0.75f, 0.4f));
    }

    private void DrawActiveSizes()
    {
        if (!_showSizes || _store?.ActiveSession == null)
            return;

        var session = _store.ActiveSession;
        var lines = new System.Collections.Generic.List<string>();

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            var def = _store.PreviewUnit(session.Id);
            if (def == null)
                return;

            if (def.IsStructure)
            {
                lines.Add($"footprint {def.Width}×{def.Height} cells");
                lines.Add($"extent {def.Width * Const.Unit}×{def.Height * Const.Unit} px");
            }
            else
            {
                lines.Add($"radius {def.Radius:0.###} cells ({def.RadiusPx:0} px)");
                lines.Add($"diameter {def.Radius * 2f:0.###} cells");
            }

            lines.Add($"vision {def.VisionRange:0.###} cells");
            if (def.Weapon != null)
                lines.Add($"attack {def.Weapon.Range:0.###} cells");
            if (def.BuildTool != null)
                lines.Add($"work {def.BuildTool.Range:0.###} cells");
            if (!def.HasModel)
                lines.Add("no model scene");

            DescribeTools(session, def, lines);
        }
        else
        {
            var tool = _store.PreviewTool(session.Id);
            if (tool != null)
            {
                lines.Add($"range {tool.Range:0.###} cells");
                lines.Add($"aim arc ±{tool.AimArcDegrees:0.#}° at {tool.AimRateDegrees:0.#}°/s");
                lines.Add($"body assist {tool.BodyAssist}");
            }
        }

        const float panelWidth = 238f;
        float panelHeight = lines.Count * 17f + 34f;
        var panel = new Rect2(
            new Vector2(Mathf.Max(Size.X - panelWidth - 12f, 42f),
                Mathf.Max(Size.Y - panelHeight - 12f, 48f)),
            new Vector2(panelWidth, panelHeight));
        DrawRect(panel, new Color(0.05f, 0.06f, 0.08f, 0.88f), true);
        DrawRect(panel, new Color(0.55f, 0.7f, 0.95f, 0.35f), false, 1f);

        float y = panel.Position.Y + 18f;
        DrawString(ThemeDB.FallbackFont, new Vector2(panel.Position.X + 10f, y),
            session.FileName, HorizontalAlignment.Left, panelWidth - 20f, 13,
            new Color(0.75f, 0.86f, 1f));
        y += 18f;
        foreach (string line in lines)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(panel.Position.X + 10f, y), line,
                HorizontalAlignment.Left, -1, 13, new Color(0.9f, 0.95f, 1f, 0.9f));
            y += 16f;
        }
    }

    /// <summary>
    /// Строки о снаряжении: у каждого инструмента сектор наведения и то, нашлась ли для него
    /// часть в сцене модели.
    ///
    /// ЗАЧЕМ СВЕРКА СО СЦЕНОЙ. Инструмент объявлен в .toml, а изображён узлом
    /// <see cref="ModelTool"/> в сцене, и эти два перечня расходятся молча: у юнита с двумя
    /// стволами художник заводит вторую часть и забывает проставить ей
    /// <see cref="ModelTool.ToolId"/>, после чего связь определяется порядком узлов в дереве.
    /// Обратный случай тот же: часть с идентификатором, которому в справочнике ничего
    /// не отвечает, не поворачивается вовсе. Оба несоответствия видны здесь.
    /// </summary>
    private void DescribeTools(OpenEntitySession session, UnitDefinition def,
        System.Collections.Generic.List<string> lines)
    {
        var tools = def.Tools;

        if (tools == null || tools.Length == 0)
            return;

        _models.TryGetValue(session.Id, out var model);
        bool hasModel = Alive.Is(model);
        var parts = hasModel ? model.Bind(tools) : null;

        for (int i = 0; i < tools.Length; i++)
        {
            var tool = tools[i];
            string arc = tool.Fixed ? "fixed"
                : tool.FullCircle ? "full circle"
                : $"±{tool.AimArcDegrees:0.#}°";

            lines.Add($"{tool.Id}: {arc}");

            if (hasModel && parts[i] == null)
                lines.Add("  no model part");
        }

        if (!hasModel)
            return;

        foreach (var part in model.Tools)
            if (!System.Array.Exists(parts, bound => bound == part))
                lines.Add($"  part {PartName(part)} unbound");
    }

    /// <summary>Как назвать часть модели в сводке: по идентификатору, иначе по имени узла.</summary>
    private static string PartName(ModelTool part) =>
        string.IsNullOrEmpty(part.ToolId) ? part.Name : part.ToolId;

    private void FitAll()
    {
        if (_store == null || !IsInstanceValid(this) || !IsInsideTree())
            return;

        bool any = false;
        var min = Vector2.Zero;
        var max = Vector2.Zero;

        foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
        {
            if (!session.ShowOnField)
                continue;

            var def = _store.PreviewUnit(session.Id);
            float extent = def != null
                ? (def.IsStructure
                    ? Mathf.Max(def.Width, def.Height) * Const.Unit * 0.5f
                    : def.RadiusPx)
                : Const.Unit;

            var a = session.FieldPosition - Vector2.One * extent;
            var b = session.FieldPosition + Vector2.One * extent;
            if (!any)
            {
                min = a;
                max = b;
                any = true;
            }
            else
            {
                min = min.Min(a);
                max = max.Max(b);
            }
        }

        if (!any)
            return;

        var size = max - min;
        if (size.X < 1f)
            size.X = Const.Unit;
        if (size.Y < 1f)
            size.Y = Const.Unit;

        float zoomX = (Size.X - 120f) / size.X;
        float zoomY = (Size.Y - 140f) / size.Y;
        _zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), 0.05f, 4f);

        var center = (min + max) * 0.5f;
        // Верхние 48 пикселей заняты панелью управления.
        _pan = new Vector2(Size.X * 0.5f, Size.Y * 0.5f + 24f) - center * _zoom;
        QueueRedraw();
    }

    private OpenEntitySession HitTest(Vector2 screen)
    {
        var world = ScreenToWorld(screen);
        OpenEntitySession best = null;
        float bestDist = float.MaxValue;

        foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
        {
            if (!session.ShowOnField)
                continue;

            float radius = Const.Unit;
            var def = _store.PreviewUnit(session.Id);
            if (def != null)
                radius = def.IsStructure
                    ? Mathf.Max(def.Width, def.Height) * Const.Unit * 0.5f
                    : def.RadiusPx;

            float dist = session.FieldPosition.DistanceTo(world);
            if (dist <= radius + 8f && dist < bestDist)
            {
                best = session;
                bestDist = dist;
            }
        }

        return best;
    }

    private void ZoomAt(Vector2 screen, float factor)
    {
        var before = ScreenToWorld(screen);
        _zoom = Mathf.Clamp(_zoom * factor, 0.05f, 8f);
        var after = ScreenToWorld(screen);
        _pan += (after - before) * _zoom;
        QueueRedraw();
    }

    private Vector2 WorldToScreen(Vector2 world) => world * _zoom + _pan;

    private Vector2 ScreenToWorld(Vector2 screen) => (screen - _pan) / _zoom;

    private static Vector2 Snap(Vector2 world)
    {
        float cell = Const.Unit * 0.25f;
        return new Vector2(
            Mathf.Round(world.X / cell) * cell,
            Mathf.Round(world.Y / cell) * cell);
    }
}
