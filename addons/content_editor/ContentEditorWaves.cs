using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Режим «Волны»: карта применимости по шкале террора слева и правка выбранной волны справа.
///
/// ЗАЧЕМ КАРТА. Волна применима не всегда, а внутри своего terror_range, и главный вопрос
/// при настройке давления — какие волны доступны при данном показателе террора и нет ли
/// на шкале участка, где не подходит ни одна. Форма свойств отдельной волны на этот вопрос
/// не отвечает, поскольку показывает по одному файлу.
///
/// ОТКУДА ЗНАЧЕНИЯ. Карта строится по <see cref="ContentEditorStore.WaveOverviews"/> —
/// снисходительному разбору черновиков, а не по собранному каталогу: компилятор отбрасывает
/// волну целиком при любой ошибке проверки, и строка исчезала бы посреди правки.
/// Инспектор правит тот же черновик через сессию вкладки, поэтому «Применить» и «Сбросить»
/// работают так же, как для юнитов.
///
/// ШКАЛА. Верхняя граница берётся по наибольшей конечной границе среди всех волн.
/// Логарифмический режим нужен затем, что одна волна с границей в десятки тысяч сжимает
/// все остальные в неразличимую полосу у нуля.
/// </summary>
[Tool]
public partial class ContentEditorWaves : HSplitContainer
{
    private const string UnitListArray = "unit_list";

    private ContentEditorStore _store;
    private ContentEditorIconCache _icons;
    private LineEdit _search;
    private CheckBox _logScale;
    private HSlider _probe;
    private Label _probeLabel;
    private EditorPanel _chartPanel;
    private EditorPanel _inspectorPanel;
    private WaveGanttChart _chart;
    private VBoxContainer _details;
    private Button _applyButton;
    private Button _revertButton;
    private Button _reloadButton;
    private Button _openFileButton;
    private string _selectedId;
    private bool _refreshing;
    private float _maxTerror = 1f;
    private readonly HashSet<string> _expandedSections = new(StringComparer.Ordinal) { "", "spawn" };

    /// <summary>Открыть определение юнита во вкладке сущностей.</summary>
    public event Action<string> OpenRequested;

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons = null)
    {
        _store = store;
        _icons = icons;
        EnsureUi();
        Refresh();
    }

    public void Refresh()
    {
        // Присвоение MaxValue ползунку может подрезать его значение и снова прислать
        // ValueChanged; повторный вход в перестроение при этом не нужен.
        if (_store == null || _refreshing)
            return;

        _refreshing = true;
        try
        {
            RefreshInternal();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshInternal()
    {
        EnsureUi();

        var waves = _store.WaveOverviews
            .OrderBy(wave => wave.TerrorMin < 0f ? 0f : wave.TerrorMin)
            .ThenBy(wave => wave.Id, StringComparer.Ordinal)
            .ToList();

        _maxTerror = 1f;
        foreach (var wave in waves)
        {
            _maxTerror = Mathf.Max(_maxTerror, wave.TerrorMin);
            _maxTerror = Mathf.Max(_maxTerror, wave.TerrorMax);
        }

        _probe.MaxValue = _maxTerror;
        _probe.Step = Mathf.Max(_maxTerror / 500f, 0.01f);
        _probeLabel.Text = $"terror {_probe.Value.ToString("0.##", CultureInfo.InvariantCulture)}" +
                           $" of {_maxTerror.ToString("0.##", CultureInfo.InvariantCulture)}";

        string query = (_search.Text ?? "").Trim();
        var rows = new List<WaveGanttRow>();
        foreach (var wave in waves)
        {
            if (query.Length > 0
                && !wave.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !(wave.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
                && !wave.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(new WaveGanttRow
            {
                Id = wave.Id,
                Title = string.IsNullOrEmpty(wave.DisplayName) ? wave.Id : wave.DisplayName,
                Subtitle = wave.Tags.Length > 0 ? string.Join(", ", wave.Tags) : wave.FileName,
                From = wave.TerrorMin < 0f ? 0f : wave.TerrorMin,
                To = wave.TerrorMax < 0f ? _maxTerror : wave.TerrorMax,
                OpenEnded = wave.TerrorMax < 0f,
                OpenStart = wave.TerrorMin < 0f,
                BudgetText = BudgetText(wave),
                Applicable = wave.Fits((float)_probe.Value),
                Faulted = !wave.Compiled,
                Dirty = SessionOf(wave.Id)?.Dirty == true,
                Accent = AccentFor(wave),
            });
        }

        if (_selectedId != null && rows.All(row => row.Id != _selectedId))
            _selectedId = null;

        _chart.SetRows(rows, _maxTerror, _logScale.ButtonPressed, (float)_probe.Value, _selectedId);
        RefreshInspector();
    }

    private static string BudgetText(WaveOverview wave)
    {
        string budget = wave.Budget.ToString("0.##", CultureInfo.InvariantCulture);
        return wave.BudgetPerTerror != 0f
            ? $"{budget} + {wave.BudgetPerTerror.ToString("0.###", CultureInfo.InvariantCulture)}/terror"
            : budget;
    }

    private static Color AccentFor(WaveOverview wave)
    {
        // Оттенок выводится из тега, а не назначается в файле: так волны одного рода
        // читаются на карте как одна группа, и новый файл не требует правки палитры.
        string key = wave.Tags.Length > 0 ? wave.Tags[0] : wave.Id;
        int hash = 0;
        foreach (char c in key)
            hash = hash * 31 + c;

        return Color.FromHsv(Mathf.Abs(hash % 360) / 360f, 0.5f, 0.85f);
    }

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    private void EnsureUi()
    {
        if (_chart != null)
            return;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        _chartPanel = new EditorPanel("Terror map");
        _chartPanel.TitleTooltip =
            "Applicability of every wave along the terror axis, read from the current drafts";
        AddChild(_chartPanel);
        BuildChartActions(_chartPanel.Actions);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _chartPanel.AddContent(scroll);

        _chart = new WaveGanttChart
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _chart.RowSelected += SelectWave;
        _chart.RowActivated += id =>
        {
            SelectWave(id);
            OpenSelectedFile();
        };
        scroll.AddChild(_chart);

        _inspectorPanel = new EditorPanel("No wave selected");
        _inspectorPanel.CustomMinimumSize = new Vector2(360, 0);
        AddChild(_inspectorPanel);
        BuildInspectorActions(_inspectorPanel.Actions);

        var detailsScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _inspectorPanel.AddContent(detailsScroll);

        _details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _details.AddThemeConstantOverride("separation", 5);
        detailsScroll.AddChild(_details);
    }

    private void BuildChartActions(HBoxContainer bar)
    {
        _search = new LineEdit
        {
            PlaceholderText = "Search waves by name or tag…",
            CustomMinimumSize = new Vector2(210, 0),
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => Refresh();
        bar.AddChild(_search);

        _logScale = new CheckBox
        {
            Text = "Log scale",
            TooltipText = "Compress the terror axis logarithmically: one wave with a very "
                          + "high bound otherwise squeezes the rest into a single strip",
        };
        _logScale.Toggled += _ => Refresh();
        bar.AddChild(_logScale);

        _probeLabel = new Label
        {
            Text = "terror 0",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _probeLabel.AddThemeFontSizeOverride("font_size", 12);
        bar.AddChild(_probeLabel);

        _probe = new HSlider
        {
            CustomMinimumSize = new Vector2(150, 0),
            MinValue = 0,
            MaxValue = 100,
            Step = 0.1,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = "Terror value to test: waves that do not fit it are dimmed",
        };
        _probe.ValueChanged += _ => Refresh();
        bar.AddChild(_probe);
    }

    private void BuildInspectorActions(HBoxContainer bar)
    {
        _applyButton = ActionButton(bar, "Apply", "Save", () =>
        {
            var session = SelectedSession();
            if (session == null)
                return;

            string error = _store.Apply(session);
            if (error != null)
                GD.PushWarning($"[Content editor] {error}");
            Refresh();
        });

        _revertButton = ActionButton(bar, "Revert", "Undo", () =>
        {
            var session = SelectedSession();
            if (session != null)
                _store.Revert(session);
        });

        _reloadButton = ActionButton(bar, "Reload", "Reload", () =>
        {
            var session = SelectedSession();
            if (session != null)
                _store.AcceptExternalReload(session.Id);
        });

        _openFileButton = ActionButton(bar, "", "ExternalLink", OpenSelectedFile);
        if (_openFileButton.Icon == null)
            _openFileButton.Text = "Open file";
    }

    private static Button ActionButton(HBoxContainer bar, string text, string icon, Action action)
    {
        var button = new Button
        {
            Text = text,
            Icon = ContentEditorTheme.Icon(icon),
        };
        button.Pressed += action;
        bar.AddChild(button);
        return button;
    }

    // ── Выбор и действия ──────────────────────────────────────────────────────────

    private void SelectWave(string id)
    {
        _selectedId = id;
        _chart.SetSelected(id);

        // Правка требует черновика, а черновик живёт в сессии вкладки. Открытие сессии
        // здесь же избавляет от отдельного действия «начать правку».
        if (id != null && SessionOf(id) == null)
            _store.Open(id);
        else
            RefreshInspector();
    }

    private OpenEntitySession SessionOf(string id) =>
        id == null ? null : _store?.Sessions.FirstOrDefault(session => session.Id == id);

    private OpenEntitySession SelectedSession() => SessionOf(_selectedId);

    private WaveOverview SelectedOverview() =>
        _selectedId == null
            ? null
            : _store?.WaveOverviews.FirstOrDefault(wave => wave.Id == _selectedId);

    private void OpenSelectedFile()
    {
        var overview = SelectedOverview();
        if (overview == null)
            return;

        OS.ShellOpen(ProjectSettings.GlobalizePath(overview.Path));
    }

    private void UpdateActionStates()
    {
        var session = SelectedSession();
        var overview = SelectedOverview();
        bool open = session != null;
        bool dirty = open && session.Dirty;
        string name = overview?.FileName ?? "";

        ContentEditorTheme.SetAction(_applyButton, dirty,
            $"Write the draft of {name} to disk",
            open ? "No unsaved changes in this wave" : "No wave is selected");
        ContentEditorTheme.SetAction(_revertButton, dirty,
            $"Discard the draft of {name}",
            open ? "No unsaved changes in this wave" : "No wave is selected");
        ContentEditorTheme.SetAction(_reloadButton, open,
            $"Re-read {name} from disk and discard the draft",
            "No wave is selected");
        ContentEditorTheme.SetAction(_openFileButton, overview != null,
            $"Open {overview?.Path} in the system editor",
            "No wave is selected");
    }

    // ── Инспектор ─────────────────────────────────────────────────────────────────

    private void RefreshInspector()
    {
        foreach (Node child in _details.GetChildren())
        {
            _details.RemoveChild(child);
            child.QueueFree();
        }

        UpdateActionStates();

        var overview = SelectedOverview();
        var session = SelectedSession();
        if (overview == null || session == null)
        {
            _inspectorPanel.Title = "No wave selected";
            _inspectorPanel.TitleTooltip = "";
            _details.AddChild(new Label
            {
                Text = "Select a wave on the chart to edit it",
                Modulate = new Color(1f, 1f, 1f, 0.6f),
            });
            return;
        }

        _inspectorPanel.Title = session.Dirty
            ? $"{overview.FileName} ({overview.DisplayName})  •  unsaved"
            : $"{overview.FileName} ({overview.DisplayName})";
        _inspectorPanel.TitleTooltip = overview.Path;

        if (!overview.Compiled)
        {
            var warning = new Label
            {
                Text = "This draft does not pass content checks; it cannot be applied yet. "
                       + "See the Godot output for the reason.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(1f, 0.75f, 0.5f),
            };
            _details.AddChild(warning);
        }

        var sections = ContentSchema.WaveFields
            .Where(field => field.Key != "id")
            .GroupBy(field => field.RootOnly ? "" : field.Section ?? "");

        foreach (var section in sections)
        {
            bool expanded = _expandedSections.Contains(section.Key);
            _details.AddChild(SectionHeading(
                section.Key,
                string.IsNullOrEmpty(section.Key) ? "Wave" : "Spawn shape",
                expanded));
            if (!expanded)
                continue;

            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 4);
            foreach (var field in section)
                body.AddChild(BuildFieldRow(session, field));
            _details.AddChild(body);
        }

        BuildUnitLists(session);
    }

    private Button SectionHeading(string key, string title, bool expanded)
    {
        Texture2D arrow = expanded
            ? ContentEditorTheme.Icon("GuiTreeArrowDown")
            : ContentEditorTheme.Icon("GuiTreeArrowRight");
        var heading = new Button
        {
            Text = (arrow == null ? (expanded ? "▼  " : "▶  ") : "") + title,
            Icon = arrow,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            TooltipText = expanded ? "Collapse section" : "Expand section",
        };
        heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
        heading.Pressed += () =>
        {
            if (!_expandedSections.Add(key))
                _expandedSections.Remove(key);
            RefreshInspector();
        };
        return heading;
    }

    private Control BuildFieldRow(OpenEntitySession session, ContentFieldSpec field)
    {
        bool local = _store.HasLocalValue(session, field);
        object value = _store.EffectiveValue(session, field);

        Control editor = EditorFieldEditor.Build(field, value, editable: true,
            committed => _store.SetField(session, field, committed, clear: false));

        var actions = new HBoxContainer { CustomMinimumSize = new Vector2(96, 0) };
        var source = new Label
        {
            Text = local ? "in this file" : "default",
            Modulate = local
                ? new Color(0.55f, 0.9f, 0.65f)
                : new Color(1f, 1f, 1f, 0.5f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        source.AddThemeFontSizeOverride("font_size", 11);
        actions.AddChild(source);
        actions.AddChild(ResetButton(
            local,
            $"Remove {field.Key} from the file and fall back to the default",
            () => _store.SetField(session, field, null, clear: true)));

        string tooltip = $"{field.Section ?? "root"}.{field.Key}";
        if (!string.IsNullOrEmpty(field.Hint))
            tooltip += "\n" + field.Hint;

        var row = new EditorPropertyRow();
        row.Configure(field.Label, tooltip, editor, actions, EditorFieldEditor.BelowTitle(field));
        return row;
    }

    private static Button ResetButton(bool enabled, string tooltip, Action action)
    {
        Texture2D icon = ContentEditorTheme.IconAny("Reload", "Undo");
        var reset = new Button
        {
            Text = icon == null ? "↶" : "",
            Icon = icon,
            Flat = true,
            CustomMinimumSize = new Vector2(24, 24),
        };
        ContentEditorTheme.SetAction(reset, enabled, tooltip, "The key is not written in this file");
        reset.Pressed += action;
        return reset;
    }

    private void BuildUnitLists(OpenEntitySession session)
    {
        var lists = _store.WaveLists(session);
        var heading = new Label { Text = "Unit lists" };
        heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
        _details.AddChild(heading);

        var explanation = new Label
        {
            Text = "allow narrows what is admissible, deny subtracts from it, "
                   + "limit only assigns a share of the budget.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        explanation.AddThemeFontSizeOverride("font_size", 11);
        _details.AddChild(explanation);

        var available = _store.EnemyUnitIds();
        foreach (var list in lists)
            _details.AddChild(BuildUnitList(session, list, available));

        var add = new Button
        {
            Text = "Add unit list",
            Icon = ContentEditorTheme.IconAny("Add", "Plus"),
            Alignment = HorizontalAlignment.Left,
            TooltipText = "Append a new [[unit_list]] block to the file",
        };
        add.Pressed += () => _store.AddArrayItem(session, UnitListArray, new[]
        {
            ("mode", TomlPatchWriter.FormatEnum(nameof(UnitListMode.Allow))),
            ("units", "[]"),
        });
        _details.AddChild(add);
    }

    private Control BuildUnitList(
        OpenEntitySession session, WaveUnitListView list, IReadOnlyList<string> available)
    {
        var panel = new EditorPanel($"[[unit_list]] {list.Index + 1} · {list.Mode}")
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };

        var remove = new Button
        {
            Icon = ContentEditorTheme.IconAny("Remove", "Close"),
            Flat = true,
            TooltipText = "Delete this list from the file",
            CustomMinimumSize = new Vector2(24, 24),
        };
        if (remove.Icon == null)
            remove.Text = "×";
        int index = list.Index;
        remove.Pressed += () => _store.RemoveArrayItem(session, UnitListArray, index);
        panel.Actions.AddChild(remove);

        foreach (var field in ContentSchema.WaveUnitListFields)
        {
            if (field.Key == "units")
            {
                panel.AddContent(BuildUnitPicker(session, list, available));
                continue;
            }

            panel.AddContent(BuildArrayFieldRow(session, index, field));
        }

        return panel;
    }

    private Control BuildUnitPicker(
        OpenEntitySession session, WaveUnitListView list, IReadOnlyList<string> available)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);

        var caption = new HBoxContainer();
        caption.AddChild(new Label { Text = "Units", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var spec = ContentSchema.WaveUnitListFields.First(field => field.Key == "units");
        caption.AddChild(ResetButton(
            _store.HasArrayItemValue(session, UnitListArray, list.Index, spec),
            "Remove the units key from this list",
            () => _store.SetArrayItemField(
                session, UnitListArray, list.Index, spec, null, clear: true)));
        box.AddChild(caption);

        var picker = new EditorIdListField();
        picker.Configure(
            list.UnitIds,
            available,
            id => _store.UnitDisplayName(id),
            id => _icons?.GetUnit(_store.PreviewUnit(id)),
            "Add unit…",
            "no units: the list does nothing");
        int index = list.Index;
        picker.Changed += values => _store.SetArrayItemField(
            session, UnitListArray, index, spec, values, clear: false);
        box.AddChild(picker);

        var open = new Button
        {
            Text = "Open selected units in Entities",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            Icon = ContentEditorTheme.IconAny("ExternalLink", "Forward"),
        };
        ContentEditorTheme.SetAction(open, list.UnitIds.Length > 0,
            "Open every unit of this list in the entities tab",
            "The list is empty");
        open.Pressed += () =>
        {
            foreach (string id in list.UnitIds)
                OpenRequested?.Invoke(id);
        };
        box.AddChild(open);
        return box;
    }

    private Control BuildArrayFieldRow(
        OpenEntitySession session, int index, ContentFieldSpec field)
    {
        bool local = _store.HasArrayItemValue(session, UnitListArray, index, field);
        object value = _store.ArrayItemValue(session, UnitListArray, index, field);

        Control editor = EditorFieldEditor.Build(field, value, editable: true,
            committed => _store.SetArrayItemField(
                session, UnitListArray, index, field, committed, clear: false));

        var actions = new HBoxContainer();
        actions.AddChild(ResetButton(
            local,
            $"Remove {field.Key} from this list",
            () => _store.SetArrayItemField(
                session, UnitListArray, index, field, null, clear: true)));

        string tooltip = field.Key;
        if (!string.IsNullOrEmpty(field.Hint))
            tooltip += "\n" + field.Hint;

        var row = new EditorPropertyRow();
        row.Configure(field.Label, tooltip, editor, actions, EditorFieldEditor.BelowTitle(field));
        return row;
    }
}

/// <summary>Строка карты волн.</summary>
public sealed class WaveGanttRow
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string Subtitle { get; init; }
    public float From { get; init; }
    public float To { get; init; }
    public bool OpenStart { get; init; }
    public bool OpenEnded { get; init; }
    public string BudgetText { get; init; }
    public bool Applicable { get; init; }
    public bool Faulted { get; init; }
    public bool Dirty { get; init; }
    public Color Accent { get; init; }
}

/// <summary>
/// Полосы применимости волн на общей шкале террора. Отдельный Control, поскольку
/// раскладка контейнерами не даёт совместить подписи строк с общей осью сверху.
/// </summary>
[Tool]
public partial class WaveGanttChart : Control
{
    private const float LabelWidth = 200f;
    private const float HeaderHeight = 26f;
    private const float RowHeight = 34f;
    private const float BarInset = 6f;

    private readonly List<WaveGanttRow> _rows = new();
    private float _max = 1f;
    private bool _log;
    private float _probe;
    private string _selectedId;
    private int _hoverRow = -1;

    public event Action<string> RowSelected;
    public event Action<string> RowActivated;

    public void SetRows(
        List<WaveGanttRow> rows, float max, bool log, float probe, string selectedId)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _max = Mathf.Max(max, 0.001f);
        _log = log;
        _probe = probe;
        _selectedId = selectedId;
        CustomMinimumSize = new Vector2(560, HeaderHeight + _rows.Count * RowHeight + 12f);
        QueueRedraw();
    }

    public void SetSelected(string id)
    {
        _selectedId = id;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int row = RowAt(motion.Position);
            if (row != _hoverRow)
            {
                _hoverRow = row;
                QueueRedraw();
            }

            return;
        }

        if (@event is not InputEventMouseButton mouse
            || mouse.ButtonIndex != MouseButton.Left
            || !mouse.Pressed)
        {
            return;
        }

        int index = RowAt(mouse.Position);
        if (index < 0)
            return;

        AcceptEvent();
        if (mouse.DoubleClick)
            RowActivated?.Invoke(_rows[index].Id);
        else
            RowSelected?.Invoke(_rows[index].Id);
    }

    private int RowAt(Vector2 position)
    {
        if (position.Y < HeaderHeight)
            return -1;

        int index = (int)((position.Y - HeaderHeight) / RowHeight);
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        if (_rows.Count == 0)
        {
            DrawString(font, new Vector2(16f, HeaderHeight + 16f),
                "No wave matches the search", HorizontalAlignment.Left, -1, 13,
                new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        float right = Size.X - 12f;
        float width = Mathf.Max(right - LabelWidth, 40f);

        DrawAxis(font, width, right);

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            float top = HeaderHeight + i * RowHeight;
            bool selected = row.Id == _selectedId;

            if (selected)
                DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(0.3f, 0.45f, 0.7f, 0.28f));
            else if (i == _hoverRow)
                DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(1f, 1f, 1f, 0.05f));
            else if (i % 2 == 1)
                DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(1f, 1f, 1f, 0.03f));

            float alpha = row.Applicable ? 1f : 0.32f;
            string title = row.Dirty ? row.Title + " *" : row.Title;
            DrawString(font, new Vector2(10f, top + 15f), title,
                HorizontalAlignment.Left, LabelWidth - 18f, 13,
                new Color(0.95f, 0.97f, 1f, alpha));
            if (!string.IsNullOrEmpty(row.Subtitle))
                DrawString(font, new Vector2(10f, top + 28f), row.Subtitle,
                    HorizontalAlignment.Left, LabelWidth - 18f, 11,
                    new Color(0.75f, 0.82f, 0.95f, alpha * 0.8f));

            float x0 = PositionOf(row.From, width);
            float x1 = PositionOf(row.To, width);
            var bar = new Rect2(
                x0, top + BarInset,
                Mathf.Max(x1 - x0, 3f), RowHeight - BarInset * 2f);
            var fill = row.Accent;
            fill.A = alpha * 0.85f;
            DrawRect(bar, fill, true);
            DrawRect(bar,
                row.Faulted
                    ? new Color(1f, 0.45f, 0.35f, alpha)
                    : new Color(1f, 1f, 1f, alpha * 0.35f),
                false, row.Faulted ? 2f : 1f);

            // Открытый конец диапазона обозначен клином: полоса доходит до края шкалы,
            // и без этого знака её нельзя отличить от границы, совпавшей с максимумом.
            if (row.OpenEnded)
                DrawOpenEnd(bar, fill);
            if (row.OpenStart)
                DrawString(font, new Vector2(LabelWidth + 4f, top + 12f), "◂",
                    HorizontalAlignment.Left, -1, 12, new Color(1f, 1f, 1f, alpha * 0.6f));

            if (bar.Size.X > 70f)
                DrawString(font, new Vector2(bar.Position.X + 8f, top + RowHeight * 0.5f + 5f),
                    row.BudgetText, HorizontalAlignment.Left, bar.Size.X - 16f, 12,
                    new Color(0.08f, 0.09f, 0.12f, alpha));
            else
                DrawString(font, new Vector2(bar.Position.X + bar.Size.X + 6f,
                        top + RowHeight * 0.5f + 5f),
                    row.BudgetText, HorizontalAlignment.Left, -1, 12,
                    new Color(0.9f, 0.95f, 1f, alpha));
        }

        float probeX = PositionOf(_probe, width);
        DrawLine(new Vector2(probeX, HeaderHeight - 6f),
            new Vector2(probeX, HeaderHeight + _rows.Count * RowHeight),
            new Color(1f, 0.75f, 0.35f, 0.9f), 2f);
    }

    private void DrawOpenEnd(Rect2 bar, Color fill)
    {
        float tip = bar.Position.X + bar.Size.X;
        var points = new[]
        {
            new Vector2(tip, bar.Position.Y),
            new Vector2(tip + 8f, bar.Position.Y + bar.Size.Y * 0.5f),
            new Vector2(tip, bar.Position.Y + bar.Size.Y),
        };
        DrawColoredPolygon(points, fill);
    }

    private void DrawAxis(Font font, float width, float right)
    {
        DrawLine(new Vector2(LabelWidth, HeaderHeight - 4f),
            new Vector2(right, HeaderHeight - 4f), new Color(1f, 1f, 1f, 0.25f));

        foreach (float value in Ticks())
        {
            float x = PositionOf(value, width);
            DrawLine(new Vector2(x, HeaderHeight - 9f), new Vector2(x, HeaderHeight - 4f),
                new Color(1f, 1f, 1f, 0.3f));
            DrawString(font, new Vector2(x + 2f, HeaderHeight - 12f),
                value.ToString("0.##", CultureInfo.InvariantCulture),
                HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.86f, 1f, 0.75f));
        }

        DrawString(font, new Vector2(10f, HeaderHeight - 12f), "wave / terror",
            HorizontalAlignment.Left, LabelWidth - 18f, 11, new Color(0.8f, 0.86f, 1f, 0.75f));
    }

    private IEnumerable<float> Ticks()
    {
        if (_log)
        {
            for (float value = 1f; value <= _max; value *= 10f)
                yield return value;
            yield break;
        }

        const int count = 6;
        for (int i = 0; i <= count; i++)
            yield return _max * i / count;
    }

    private float PositionOf(float value, float width)
    {
        float clamped = Mathf.Clamp(value, 0f, _max);
        float t = _log
            ? Mathf.Log(1f + clamped) / Mathf.Log(1f + _max)
            : clamped / _max;
        return LabelWidth + t * width;
    }
}
