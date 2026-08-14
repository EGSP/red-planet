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

        UpdateToolbarState();
        QueueRedraw();
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        EnsureToolbar();
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
            if (tool != null && !string.IsNullOrEmpty(tool.Sprite))
            {
                SpriteArt.DrawNative(
                    this,
                    tool.Sprite,
                    Vector2.Zero,
                    tool.SpriteRotationDegrees,
                    baseOrigin: origin,
                    presentationScale: _zoom,
                    modulate: new Color(1f, 1f, 1f, alpha));
                extent = Mathf.Max(SpriteArt.NativeExtent(tool.Sprite) * _zoom, extent);
                if (active)
                {
                    DrawArc(origin, extent + 5f, 0f, Mathf.Tau, 40,
                        new Color(1f, 0.9f, 0.3f, 0.8f), 2f, true);
                }
            }
            else
            {
                Color fallback = tool is WeaponDefinition
                    ? new Color(1f, 0.38f, 0.32f, alpha)
                    : new Color(0.38f, 0.85f, 0.55f, alpha);
                DrawCircle(origin, extent, fallback);
            }

            DrawEntityLabel(origin + new Vector2(extent + 8f, 5f), session, active);
            if (tool is WeaponDefinition weapon && _showAttack)
            {
                DrawSetTransform(origin, 0f, Vector2.One * _zoom);
                WeaponGizmo.Draw(this, weapon);
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
            WeaponGizmo.Draw(this, def.Weapon);
        if (_showWork && def.BuildTool != null)
            WorkGizmo.Draw(this, def.WorkRangePx);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

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
            UnitSilhouette.Draw(
                this,
                def,
                def.RadiusPx * _zoom,
                toolLocal: 0f,
                origin: origin,
                presentationScale: _zoom);
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
            if (!string.IsNullOrEmpty(def.Sprite))
                lines.Add($"sprite ×{def.SpriteScale:0.##}");
        }
        else
        {
            var tool = _store.PreviewTool(session.Id);
            if (tool != null)
                lines.Add($"range {tool.Range:0.###} cells");
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
