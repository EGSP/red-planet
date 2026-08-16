using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Режим «Waves»: карта применимости волн по шкале террора, панель формы появления
/// и инспектор выбранной волны.
///
/// ЗАЧЕМ КАРТА. Волна применима не всегда, а внутри своего <c>terror_range</c>, и главный
/// вопрос при настройке давления — какие волны доступны при данном показателе террора
/// и нет ли на шкале участка, где не подходит ни одна. Форма свойств отдельной волны
/// на этот вопрос не отвечает, поскольку показывает по одному файлу.
///
/// ЗАЧЕМ ПАНЕЛЬ ФОРМЫ. Числа секции <c>[spawn]</c> задают геометрию, которую по отдельным
/// полям представить нельзя. Панель показывает построение теми же <see cref="WaveFormation"/>
/// и <see cref="WaveComposer"/>, что работают в партии.
///
/// ОТКУДА ЗНАЧЕНИЯ. Карта строится по <see cref="ContentEditorStore.WaveOverviews"/> —
/// снисходительному разбору черновиков, а не по собранному каталогу: компилятор отбрасывает
/// волну целиком при любой ошибке проверки, и строка исчезала бы посреди правки.
///
/// ВКЛАДОК СУЩНОСТЕЙ ЭТО НЕ КАСАЕТСЯ. Выбранная волна открывается сессией области
/// <see cref="ContentEditorScope.Waves"/>, поэтому режим «Entities» её не показывает
/// и активная там сущность не подменяется.
/// </summary>
[Tool]
public partial class ContentEditorWaves : HSplitContainer, IContentEditorMode
{
    private ContentEditorStore _store;
    private ContentEditorIconCache _icons;

    private LineEdit _search;
    private CheckBox _logScale;
    private HSlider _probe;
    private Label _probeLabel;
    private VSplitContainer _leftSplit;
    private EditorPanel _chartPanel;
    private EditorPanel _shapePanel;
    private EditorPanel _inspectorPanel;
    private WaveGanttChart _chart;
    private WaveShapeView _shapeView;
    private WaveInspector _inspector;

    private CheckBox _showUnits;
    private CheckBox _showWorld;
    private HSlider _direction;
    private Label _directionLabel;
    private Button _seedButton;

    private Button _applyButton;
    private Button _revertButton;
    private Button _reloadButton;
    private Button _openFileButton;

    private string _selectedId;

    /// <summary>Зерно набора состава: с ним состав воспроизводится, а не скачет при правке.</summary>
    private int _seed = 1;

    /// <summary>Наибольшая конечная граница среди всех волн: она задаёт длину шкалы.</summary>
    private float _maxTerror = 1f;

    /// <summary>Присвоение MaxValue ползунку может прислать ValueChanged и вызвать рекурсию.</summary>
    private bool _refreshing;

    private bool _restoring;
    private bool _built;

    public string ModeTitle => "Waves";

    public event Action UiStateChanged;
    public event Action<string> StatusReported;
    public event Action<string> EntityOpenRequested;

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons)
    {
        _store = store;
        _icons = icons;
        EnsureUi();
        _inspector.Bind(store, icons);
        Refresh();
    }

    public void Refresh()
    {
        if (_store == null || _refreshing)
            return;

        _refreshing = true;
        try
        {
            EnsureUi();
            RefreshChart();
            RefreshInspector();
            RefreshShape();
        }
        finally
        {
            _refreshing = false;
        }
    }

    // ── Снимок состояния ──────────────────────────────────────────────────────────

    public void CaptureInto(ContentEditorWorkspace workspace)
    {
        if (!_built || workspace == null)
            return;

        var state = workspace.Waves;
        state.SelectedId = _selectedId;
        state.Search = _search.Text ?? "";
        state.LogScale = _logScale.ButtonPressed;
        state.Probe = (float)_probe.Value;
        state.Split = EditorControls.SplitOffset(this);
        state.ShapeSplit = EditorControls.SplitOffset(_leftSplit);
        state.ShapeShowUnits = _showUnits.ButtonPressed;
        state.ShapeShowWorld = _showWorld.ButtonPressed;
        state.ShapeDirectionDegrees = (float)_direction.Value;
        state.ShapeSeed = _seed;
        state.ExpandedSections = _inspector.ExpandedSections;
        state.HasExpandedSections = true;
    }

    public void RestoreFrom(ContentEditorWorkspace workspace)
    {
        if (workspace?.Waves == null)
            return;

        EnsureUi();
        _restoring = true;
        try
        {
            var state = workspace.Waves;
            _selectedId = string.IsNullOrEmpty(state.SelectedId) ? null : state.SelectedId;
            _search.Text = state.Search ?? "";
            _logScale.ButtonPressed = state.LogScale;
            _probe.Value = state.Probe;
            _showUnits.ButtonPressed = state.ShapeShowUnits;
            _showWorld.ButtonPressed = state.ShapeShowWorld;
            _direction.Value = state.ShapeDirectionDegrees;
            _seed = Math.Max(state.ShapeSeed, 1);
            if (state.HasExpandedSections)
                _inspector.ExpandedSections = state.ExpandedSections;

            if (state.Split != 0)
                EditorControls.SetSplitOffset(this, state.Split);
            if (state.ShapeSplit != 0)
                EditorControls.SetSplitOffset(_leftSplit, state.ShapeSplit);

            // Волна выбирается через Store: инспектору нужен черновик, а он живёт в сессии.
            if (_selectedId != null && _store?.Session(_selectedId) == null)
                _store?.Open(_selectedId);
        }
        finally
        {
            _restoring = false;
        }

        Refresh();
    }

    // ── Карта ─────────────────────────────────────────────────────────────────────

    private void RefreshChart()
    {
        var waves = _store.WaveOverviews
            .OrderBy(wave => wave.TerrorMin < 0f ? 0f : wave.TerrorMin)
            .ThenBy(wave => wave.Id, StringComparer.Ordinal)
            .ToList();

        _maxTerror = 1f;
        foreach (var wave in waves)
            _maxTerror = Mathf.Max(_maxTerror, Mathf.Max(wave.TerrorMin, wave.TerrorMax));

        _probe.MaxValue = _maxTerror;
        _probe.Step = Mathf.Max(_maxTerror / 500f, 0.01f);
        _probeLabel.Text =
            $"terror {_probe.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
            + $" of {_maxTerror.ToString("0.##", CultureInfo.InvariantCulture)}";

        string query = (_search.Text ?? "").Trim();
        var rows = waves
            .Where(wave => Matches(wave, query))
            .Select(ToRow)
            .ToList();

        if (_selectedId != null && rows.All(row => row.Id != _selectedId))
            _selectedId = null;

        _chart.SetRows(rows, _maxTerror, _logScale.ButtonPressed, (float)_probe.Value, _selectedId);
    }

    private static bool Matches(WaveOverview wave, string query) =>
        query.Length == 0
        || wave.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (wave.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
        || wave.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));

    private WaveGanttRow ToRow(WaveOverview wave) => new()
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
        Dirty = _store.Session(wave.Id)?.Dirty == true,
        Accent = AccentFor(wave),
    };

    private static string BudgetText(WaveOverview wave)
    {
        string budget = wave.Budget.ToString("0.##", CultureInfo.InvariantCulture);
        return wave.BudgetPerTerror != 0f
            ? $"{budget} + {wave.BudgetPerTerror.ToString("0.###", CultureInfo.InvariantCulture)}/terror"
            : budget;
    }

    /// <summary>
    /// Оттенок выводится из тега, а не назначается в файле: волны одного рода читаются
    /// на карте как одна группа, и новый файл не требует правки палитры.
    /// </summary>
    private static Color AccentFor(WaveOverview wave)
    {
        string key = wave.Tags.Length > 0 ? wave.Tags[0] : wave.Id;
        int hash = 0;
        foreach (char c in key)
            hash = hash * 31 + c;

        return Color.FromHsv(Mathf.Abs(hash % 360) / 360f, 0.5f, 0.85f);
    }

    // ── Панель формы ──────────────────────────────────────────────────────────────

    private void RefreshShape()
    {
        _shapeView.ShowUnits = _showUnits.ButtonPressed;
        _shapeView.ShowWorld = _showWorld.ButtonPressed;
        _directionLabel.Text =
            $"from {_direction.Value.ToString("0", CultureInfo.InvariantCulture)}°";
        ContentEditorTheme.SetAction(_seedButton, true,
            $"Re-roll the composition. Current seed: {_seed}", "");

        var session = SelectedSession();
        var overview = SelectedOverview();
        if (session == null || overview == null)
        {
            _shapePanel.Title = "Spawn shape";
            _shapeView.SetPreview(null, "", "Select a wave on the chart");
            return;
        }

        var preview = WaveShapePreview.Build(
            _store, session, (float)_probe.Value, (float)_direction.Value, _seed);

        _shapePanel.Title = $"Spawn shape · {overview.FileName}";
        _shapeView.SetPreview(preview,
            $"{overview.DisplayName} at terror "
            + $"{_probe.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
            preview == null ? "The wave shape could not be computed" : "");
    }

    // ── Инспектор ─────────────────────────────────────────────────────────────────

    private void RefreshInspector()
    {
        var session = SelectedSession();
        var overview = SelectedOverview();
        _inspector.Show(session, overview);
        UpdateActionStates();

        if (overview == null || session == null)
        {
            _inspectorPanel.Title = "No wave selected";
            _inspectorPanel.TitleTooltip = "";
            return;
        }

        _inspectorPanel.Title = session.Dirty
            ? $"{overview.FileName} ({overview.DisplayName})  •  unsaved"
            : $"{overview.FileName} ({overview.DisplayName})";
        _inspectorPanel.TitleTooltip = overview.Path;
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

    // ── Выбор ─────────────────────────────────────────────────────────────────────

    private void SelectWave(string id)
    {
        _selectedId = id;
        _chart.SetSelected(id);

        // Правка требует черновика, а черновик живёт в сессии: открытие сессии здесь же
        // избавляет от отдельного действия «начать правку».
        if (id != null && _store.Session(id) == null)
            _store.Open(id);
        else if (id != null)
            _store.Activate(id);

        Refresh();
        if (!_restoring)
            UiStateChanged?.Invoke();
    }

    private OpenEntitySession SelectedSession() =>
        _selectedId == null ? null : _store?.Session(_selectedId);

    private WaveOverview SelectedOverview() => _store?.WaveOverview(_selectedId);

    private void OpenSelectedFile()
    {
        if (SelectedOverview() is { } overview)
            OS.ShellOpen(ProjectSettings.GlobalizePath(overview.Path));
    }

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    private void EnsureUi()
    {
        if (_built)
            return;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Dragged += _ => NotifyUiStateChanged();

        _leftSplit = new VSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _leftSplit.Dragged += _ => NotifyUiStateChanged();
        AddChild(_leftSplit);

        _leftSplit.AddChild(BuildChartPanel());
        _leftSplit.AddChild(BuildShapePanel());
        AddChild(BuildInspectorPanel());
        _built = true;
    }

    private Control BuildChartPanel()
    {
        _chartPanel = new EditorPanel("Terror map")
        {
            TitleTooltip =
                "Applicability of every wave along the terror axis, read from the current drafts",
        };
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
        return _chartPanel;
    }

    private void BuildChartActions(HBoxContainer bar)
    {
        _search = new LineEdit
        {
            PlaceholderText = "Search waves by name or tag…",
            CustomMinimumSize = new Vector2(210, 0),
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => OnControlChanged();
        bar.AddChild(_search);

        _logScale = new CheckBox
        {
            Text = "Log scale",
            TooltipText = "Compress the terror axis logarithmically: one wave with a very "
                          + "high bound otherwise squeezes the rest into a single strip",
        };
        _logScale.Toggled += _ => OnControlChanged();
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
            TooltipText = "Terror value to test: waves that do not fit it are dimmed, "
                          + "and the spawn shape below is computed for it",
        };
        _probe.ValueChanged += _ => OnControlChanged();
        bar.AddChild(_probe);
    }

    private Control BuildShapePanel()
    {
        _shapePanel = new EditorPanel("Spawn shape")
        {
            TitleTooltip = "Where the wave appears and how it is arranged, "
                           + "computed by the same code the game uses",
            CustomMinimumSize = new Vector2(0, 220),
        };
        BuildShapeActions(_shapePanel.Actions);

        _shapeView = new WaveShapeView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _shapePanel.AddContent(_shapeView);
        return _shapePanel;
    }

    private void BuildShapeActions(HBoxContainer bar)
    {
        _showUnits = new CheckBox
        {
            Text = "Units",
            ButtonPressed = true,
            TooltipText = "Draw the composition, not only the sector outline",
        };
        _showUnits.Toggled += _ => OnShapeControlChanged();
        bar.AddChild(_showUnits);

        _showWorld = new CheckBox
        {
            Text = "World",
            ButtonPressed = true,
            TooltipText = "Draw the spawn circle and the build field border",
        };
        _showWorld.Toggled += _ => OnShapeControlChanged();
        bar.AddChild(_showWorld);

        _directionLabel = new Label
        {
            Text = "from 270°",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _directionLabel.AddThemeFontSizeOverride("font_size", 12);
        bar.AddChild(_directionLabel);

        _direction = new HSlider
        {
            CustomMinimumSize = new Vector2(110, 0),
            MinValue = -180,
            MaxValue = 180,
            Step = 1,
            Value = -90,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = "Direction of the first group. In the game it is random; "
                          + "here it only chooses which case to look at",
        };
        _direction.ValueChanged += _ => OnShapeControlChanged();
        bar.AddChild(_direction);

        _seedButton = EditorControls.Add(bar, "", "RandomNumberGenerator", () =>
        {
            // Зерно задаёт состав: другое зерно — другой набор при том же бюджете.
            _seed = (int)(GD.Randi() % 100000) + 1;
            RefreshShape();
            NotifyUiStateChanged();
        });
        if (_seedButton.Icon == null)
            _seedButton.Text = "Re-roll";

        EditorControls.Add(bar, "Fit", "Zoom", () => _shapeView.Fit(),
            "Fit the whole formation into the panel");
    }

    private Control BuildInspectorPanel()
    {
        _inspectorPanel = new EditorPanel("No wave selected")
        {
            CustomMinimumSize = new Vector2(360, 0),
        };
        BuildInspectorActions(_inspectorPanel.Actions);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _inspectorPanel.AddContent(scroll);

        _inspector = new WaveInspector();
        _inspector.EntityOpenRequested += id => EntityOpenRequested?.Invoke(id);
        _inspector.UiStateChanged += NotifyUiStateChanged;
        scroll.AddChild(_inspector);
        return _inspectorPanel;
    }

    private void BuildInspectorActions(HBoxContainer bar)
    {
        _applyButton = EditorControls.Add(bar, "Apply", "Save", () =>
        {
            if (SelectedSession() is not { } session)
                return;

            string error = _store.Apply(session);
            StatusReported?.Invoke(error ?? $"saved: {session.FileName}");
        });

        _revertButton = EditorControls.Add(bar, "Revert", "Undo", () =>
        {
            if (SelectedSession() is { } session)
            {
                _store.Revert(session);
                StatusReported?.Invoke($"draft reverted: {session.FileName}");
            }
        });

        _reloadButton = EditorControls.Add(bar, "Reload", "Reload", () =>
        {
            if (SelectedSession() is { } session)
            {
                string fileName = session.FileName;
                _store.AcceptExternalReload(session.Id);
                StatusReported?.Invoke($"reloaded from disk: {fileName}");
            }
        });

        _openFileButton = EditorControls.Add(bar, "", "ExternalLink", OpenSelectedFile);
        if (_openFileButton.Icon == null)
            _openFileButton.Text = "Open file";
    }

    /// <summary>Отбор и шкала изменились: перестроить карту и пересчитать форму.</summary>
    private void OnControlChanged()
    {
        if (_restoring || _refreshing)
            return;

        EditorControls.Run("update wave map", () =>
        {
            Refresh();
            NotifyUiStateChanged();
        });
    }

    /// <summary>Изменились только настройки показа формы: карту трогать незачем.</summary>
    private void OnShapeControlChanged()
    {
        if (_restoring || _refreshing)
            return;

        EditorControls.Run("update wave shape", () =>
        {
            RefreshShape();
            NotifyUiStateChanged();
        });
    }

    private void NotifyUiStateChanged()
    {
        if (!_restoring)
            UiStateChanged?.Invoke();
    }
}
