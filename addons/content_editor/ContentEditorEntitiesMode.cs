using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Режим «Entities»: каталог слева, вкладки открытых файлов сверху, общее измерительное
/// поле по центру и форма свойств активной сущности справа.
///
/// ЧТО СЮДА НЕ ВХОДИТ. Волны сущностями каталога не являются и правятся в своём режиме;
/// перечень вкладок берётся из области <see cref="ContentEditorScope.Entities"/>, поэтому
/// выбор волны на карте больше не открывает вкладку здесь и не подменяет активную
/// сущность.
///
/// ОБЛАСТИ ДЕЙСТВИЯ КНОПОК. Кнопка стоит в заголовке той панели, на содержимое которой
/// действует: правка файла — в заголовке панели свойств, расстановка объектов — на панели
/// поля. Общий для проекта «Reload all files» остаётся в верхней полосе главного экрана.
/// </summary>
[Tool]
public partial class ContentEditorEntitiesMode : HSplitContainer, IContentEditorMode
{
    private ContentEditorStore _store;
    private ContentEditorIconCache _icons;

    private LineEdit _search;
    private TabBar _catalogTabs;
    private ItemList _catalog;
    private TabBar _entityTabs;
    private ContentEditorForm _form;
    private ContentEditorPreview _preview;
    private EditorContextTabs _contextTabs;
    private EditorPanel _entityPanel;
    private VSplitContainer _center;
    private HSplitContainer _workspace;
    private Button _applyButton;
    private Button _revertButton;
    private Button _reloadFileButton;
    private Button _closeEntityButton;
    private ConfirmationDialog _closeDialog;

    /// <summary>Вкладка, закрытие которой ожидает ответа о судьбе черновика.</summary>
    private string _pendingCloseId;

    /// <summary>Перестроение перечня вкладок не должно считаться выбором вкладки.</summary>
    private bool _refreshingTabs;

    private bool _built;

    public string ModeTitle => "Entities";

    public event Action UiStateChanged;
    public event Action<string> StatusReported;

    // Режим сущностей сам является целью такого запроса, поэтому событие договора здесь
    // не используется; предупреждение о неиспользуемом событии подавлено намеренно.
#pragma warning disable CS0067
    public event Action<string> EntityOpenRequested;
#pragma warning restore CS0067

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons)
    {
        _store = store;
        _icons = icons;
        EnsureUi();
        _form.Bind(store);
        _preview.Bind(store);
        Refresh();
    }

    public void Refresh()
    {
        if (_store == null || !_built)
            return;

        RefreshCatalog();
        RefreshContextPanels();
        _form.Refresh();
        _preview.RefreshFromStore();
        RefreshTabs();
    }

    /// <summary>Перечень записей каталога: только он зависит от отбора и от иконок.</summary>
    private void RefreshCatalog()
    {
        if (!IsInstanceValid(_catalog) || _store == null)
            return;

        _catalog.Clear();
        string query = (_search?.Text ?? "").Trim();
        int category = _catalogTabs?.CurrentTab ?? 0;

        foreach (var entry in _store.Entries.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase))
        {
            // Шаблоны с abstract = true в игре не создаются — в каталоге редактора скрыты.
            if (entry.Abstract || !MatchesCategory(entry.Kind, category))
                continue;

            // Имя файла — устойчивый ориентир и совпадает с деревом FileSystem.
            // Отображаемое имя остаётся в скобках для художника и геймдизайнера.
            string label = $"{entry.FileName} ({entry.DisplayName})";
            if (query.Length > 0
                && !label.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int index = _catalog.AddItem(label, CatalogIcon(entry));
            _catalog.SetItemMetadata(index, entry.Id);
        }
    }

    public void CaptureInto(ContentEditorWorkspace workspace)
    {
        if (!_built || workspace == null)
            return;

        var state = workspace.Entities;
        state.CatalogTab = _catalogTabs.CurrentTab;
        state.CatalogSearch = _search.Text ?? "";
        state.FormFieldFilter = _form.FieldFilter;
        state.ExpandedSections = _form.ExpandedSections;
        state.HasExpandedSections = true;
        state.CatalogSplit = EditorControls.SplitOffset(this);
        state.WorkspaceSplit = EditorControls.SplitOffset(_workspace);
        state.CenterSplit = EditorControls.SplitOffset(_center);
    }

    public void RestoreFrom(ContentEditorWorkspace workspace)
    {
        if (workspace == null)
            return;

        EnsureUi();
        var state = workspace.Entities;
        _catalogTabs.CurrentTab = Mathf.Clamp(state.CatalogTab, 0, _catalogTabs.TabCount - 1);
        _search.Text = state.CatalogSearch ?? "";
        _form.FieldFilter = state.FormFieldFilter ?? "";
        if (state.HasExpandedSections)
            _form.ExpandedSections = state.ExpandedSections;

        // Ноль означает «положение не сохранялось»: SplitContainer в этом случае сам
        // раскладывает области по минимальным размерам, и навязывать ему ноль не нужно.
        if (state.CatalogSplit != 0)
            EditorControls.SetSplitOffset(this, state.CatalogSplit);
        if (state.WorkspaceSplit != 0)
            EditorControls.SetSplitOffset(_workspace, state.WorkspaceSplit);
        if (state.CenterSplit != 0)
            EditorControls.SetSplitOffset(_center, state.CenterSplit);
    }

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    private void EnsureUi()
    {
        if (_built)
            return;

        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Dragged += _ => UiStateChanged?.Invoke();

        AddChild(BuildCatalogPanel());

        var right = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(right);

        // Нативный TabBar связывает крестик с самой вкладкой и поддерживает горизонтальную
        // прокрутку. Отдельные Button и «×» не показывали, к какому названию относится
        // закрытие.
        _entityTabs = new TabBar
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowAlways,
            ScrollingEnabled = true,
        };
        _entityTabs.TabChanged += OnTabChanged;
        _entityTabs.TabClosePressed += OnTabClosePressed;
        StyleTabs(_entityTabs);
        right.AddChild(_entityTabs);

        _workspace = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _workspace.Dragged += _ => UiStateChanged?.Invoke();
        right.AddChild(_workspace);

        // Центральная область делится по вертикали только при наличии контекстных вкладок:
        // скрытый EditorContextTabs не отнимает места у предпросмотра.
        _center = new VSplitContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _center.Dragged += _ => UiStateChanged?.Invoke();
        _workspace.AddChild(_center);

        var previewPanel = new EditorPanel("Measure field · all open entities")
        {
            TitleTooltip = "Every open entity with “Show on field” drawn at the same scale",
        };
        _center.AddChild(previewPanel);

        _preview = new ContentEditorPreview
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(360, 240),
        };
        previewPanel.AddContent(_preview);

        _contextTabs = new EditorContextTabs();
        _center.AddChild(_contextTabs);

        // Панель активной сущности охватывает форму её свойств, а не всю рабочую область:
        // измерительное поле общее для всех вкладок и одной сущности не принадлежит.
        _entityPanel = new EditorPanel("no entity open")
        {
            CustomMinimumSize = new Vector2(450, 0),
        };
        _workspace.AddChild(_entityPanel);
        BuildEntityActions(_entityPanel.Actions);

        _form = new ContentEditorForm
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _entityPanel.AddContent(_form);
        _form.VarsPanelRequested += OpenVarsPanel;
        _form.GraphPanelRequested += OpenGraphPanel;
        _form.UiStateChanged += () => UiStateChanged?.Invoke();

        BuildCloseDialog();
        _built = true;
    }

    private Control BuildCatalogPanel()
    {
        var left = new EditorPanel("Catalog")
        {
            CustomMinimumSize = new Vector2(240, 0),
            TitleTooltip = "Definitions of the project grouped by kind",
        };

        _search = new LineEdit
        {
            PlaceholderText = "Search catalog…",
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => EditorControls.Run("filter catalog", () =>
        {
            RefreshCatalog();
            UiStateChanged?.Invoke();
        });
        left.AddContent(_search);

        // Виды содержимого разделены вкладками: смешанный список заставлял различать
        // юнита и его ствол по двухбуквенному префиксу.
        _catalogTabs = new TabBar { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _catalogTabs.AddTab("Units");
        _catalogTabs.AddTab("Buildings");
        _catalogTabs.AddTab("Weapons");
        _catalogTabs.AddTab("Tools");
        _catalogTabs.TabChanged += _ => EditorControls.Run("switch catalog tab", () =>
        {
            RefreshCatalog();
            UiStateChanged?.Invoke();
        });
        left.AddContent(_catalogTabs);

        // Одинарный щелчок открывает вкладку: отдельный жест «активировать» здесь не нужен.
        _catalog = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(24, 24),
        };
        _catalog.ItemActivated += index => OpenCatalogIndex((int)index);
        _catalog.ItemSelected += index => OpenCatalogIndex((int)index);
        left.AddContent(_catalog);
        return left;
    }

    /// <summary>
    /// Полоса действий над той сущностью, вкладка которой выбрана. Имя файла стоит
    /// в заголовке панели, поэтому область действия кнопок видна по расположению.
    /// </summary>
    private void BuildEntityActions(HBoxContainer bar)
    {
        _applyButton = EditorControls.Add(bar, "Apply", "Save", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            string error = _store.Apply(session);
            StatusReported?.Invoke(error ?? $"saved: {session.FileName}");
            RefreshTabs();
        });

        _revertButton = EditorControls.Add(bar, "Revert", "Undo", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            _store.Revert(session);
            StatusReported?.Invoke($"draft reverted: {session.FileName}");
        });

        _reloadFileButton = EditorControls.Add(bar, "Reload file", "Reload", () =>
        {
            var session = _store.ActiveSession;
            if (session == null)
                return;

            string fileName = session.FileName;
            _store.AcceptExternalReload(session.Id);
            StatusReported?.Invoke($"reloaded from disk: {fileName}");
        });

        _closeEntityButton = EditorControls.Add(bar, "Close", "Close", () =>
        {
            if (_store.ActiveSession is { } session)
                RequestClose(session.Id);
        });
    }

    private void BuildCloseDialog()
    {
        _closeDialog = new ConfirmationDialog
        {
            Title = "Unsaved changes",
            DialogText = "Save changes before closing?",
            OkButtonText = "Save",
        };
        _closeDialog.Confirmed += () => EditorControls.Run("save before closing", () =>
        {
            string id = _pendingCloseId;
            string error = _store.Apply(_store.Session(id));
            if (error != null)
            {
                StatusReported?.Invoke(error);
                return;
            }

            _pendingCloseId = null;
            _store.Close(id, discard: true);
        });
        AddChild(_closeDialog);

        // Кнопка с пользовательским действием не обязана скрывать диалог сама.
        _closeDialog.AddButton("Discard changes", true, "discard");
        _closeDialog.CustomAction += action =>
        {
            if (action != "discard")
                return;

            EditorControls.Run("discard draft", () =>
            {
                _closeDialog.Hide();
                string id = _pendingCloseId;
                _pendingCloseId = null;
                _store.Close(id, discard: true);
            });
        };
    }

    // ── Вкладки открытых сущностей ────────────────────────────────────────────────

    private void RefreshTabs()
    {
        if (!IsInstanceValid(_entityTabs) || _store == null)
            return;

        _refreshingTabs = true;
        _entityTabs.ClearTabs();

        int active = -1;
        int index = 0;
        foreach (var session in _store.SessionsIn(ContentEditorScope.Entities))
        {
            _entityTabs.AddTab(session.TabTitle);
            _entityTabs.SetTabMetadata(index, session.Id);
            _entityTabs.SetTabTooltip(index,
                $"{session.FileName} ({session.DisplayName})\n{session.Path}");

            if (_store.ActiveSession == session)
                active = index;

            index++;
        }

        if (active >= 0)
            _entityTabs.CurrentTab = active;

        _refreshingTabs = false;
        UpdateActionStates();
    }

    private void OnTabChanged(long index)
    {
        if (_refreshingTabs || _store == null || index < 0 || index >= _entityTabs.TabCount)
            return;

        EditorControls.Run("switch entity tab", () =>
            _store.Activate(_entityTabs.GetTabMetadata((int)index).AsString()));
    }

    private void OnTabClosePressed(long index)
    {
        if (_store == null || index < 0 || index >= _entityTabs.TabCount)
            return;

        RequestClose(_entityTabs.GetTabMetadata((int)index).AsString());
    }

    /// <summary>Закрыть вкладку; несохранённая правка требует явного решения.</summary>
    private void RequestClose(string id)
    {
        var session = _store.Session(id);
        if (session == null)
            return;

        if (!session.Dirty)
        {
            _store.Close(id, discard: true);
            return;
        }

        _pendingCloseId = id;
        _closeDialog.PopupCentered();
    }

    private void OpenCatalogIndex(int index)
    {
        if (_store == null || index < 0 || index >= _catalog.ItemCount)
            return;

        EditorControls.Run("open entity", () =>
        {
            string id = _catalog.GetItemMetadata(index).AsString();
            _store.Open(id);
            StatusReported?.Invoke($"opened: {id}");
        });
    }

    /// <summary>
    /// Согласовать доступность кнопок с состоянием: кнопка, нажатие на которую ничего
    /// не изменило бы, выключается, а подсказка называет причину.
    /// </summary>
    private void UpdateActionStates()
    {
        if (!IsInstanceValid(_entityPanel) || _store == null)
            return;

        var session = _store.ActiveSession;
        bool open = session != null;
        bool dirty = open && session.Dirty;

        _entityPanel.Title = open
            ? $"{session.FileName} ({session.DisplayName}){(dirty ? "  •  unsaved" : "")}"
            : "no entity open";
        _entityPanel.TitleTooltip = open ? session.Path : "";

        ContentEditorTheme.SetAction(_applyButton, dirty,
            open ? $"Write the draft of {session.FileName} to disk" : "",
            open ? "No unsaved changes in this entity" : "No entity is open");
        ContentEditorTheme.SetAction(_revertButton, dirty,
            open ? $"Discard the draft of {session.FileName}" : "",
            open ? "No unsaved changes in this entity" : "No entity is open");
        ContentEditorTheme.SetAction(_reloadFileButton, open,
            open ? $"Re-read {session.FileName} from disk and discard the draft" : "",
            "No entity is open");
        ContentEditorTheme.SetAction(_closeEntityButton, open,
            open ? $"Close the tab of {session.FileName}" : "",
            "No entity is open");
    }

    // ── Контекстные страницы ──────────────────────────────────────────────────────

    /// <summary>Открыть страницу значений файла переменных для активной сущности.</summary>
    private void OpenVarsPanel(string path)
    {
        var session = _store.ActiveSession;
        if (session?.Kind is not (ContentEntityKind.Unit or ContentEntityKind.Building))
            return;

        _contextTabs.SetContext(session.Id, preservePages: true);
        VarsPanelData data = _store.VarsPanel(path);
        if (data == null || data.ContextId != session.Id)
            return;

        string key = $"vars:{path}";
        if (_contextTabs.Page(key) is EditorKeyValueTable existing)
        {
            existing.SetRows(data.Rows);
            _contextTabs.Select(key);
            return;
        }

        var table = new EditorKeyValueTable();
        table.SetRows(data.Rows);
        _contextTabs.Open(key, data.FileName, table);
    }

    /// <summary>Открыть страницу графа зависимостей активной сущности.</summary>
    private void OpenGraphPanel()
    {
        var session = _store.ActiveSession;
        if (session?.Kind is not (ContentEntityKind.Unit or ContentEntityKind.Building))
            return;

        _contextTabs.SetContext(session.Id, preservePages: true);
        ContentGraphData data = _store.ActiveContextGraph();
        if (data == null || data.ContextId != session.Id)
            return;

        if (_contextTabs.Page("graph") is EditorNodeGraph existing)
        {
            existing.SetGraph(data);
            _contextTabs.Select("graph");
            return;
        }

        var graph = new EditorNodeGraph();
        graph.SetGraph(data);
        graph.NodeActivated += OpenGraphNode;
        _contextTabs.Open("graph", "Relations", graph);
    }

    private void OpenGraphNode(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (TomlResolver.IsVars(path))
            OpenVarsPanel(path);
        else
            _store.OpenPath(path);
    }

    /// <summary>
    /// Согласовать открытые контекстные страницы с активной сущностью: граф получает новые
    /// данные, а страница переменных сохраняется, только если новая сущность ссылается
    /// на тот же файл.
    /// </summary>
    private void RefreshContextPanels()
    {
        var session = _store.ActiveSession;
        string contextId = session?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? session.Id
            : null;
        if (contextId == null)
        {
            _contextTabs.SetContext(null);
            return;
        }

        ContentGraphData graphData = _store.ActiveContextGraph();
        _contextTabs.SetContext(contextId, preservePages: true);
        var referencedVars = graphData?.Nodes
                                 .Where(node => node.Kind == ContentGraphNodeKind.Vars)
                                 .Select(node => node.Path)
                                 .ToHashSet(StringComparer.Ordinal)
                             ?? new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in _contextTabs.Pages.ToList())
        {
            if (pair.Key == "graph" && pair.Value is EditorNodeGraph graph)
            {
                graph.SetGraph(graphData);
                continue;
            }

            if (!pair.Key.StartsWith("vars:", StringComparison.Ordinal)
                || pair.Value is not EditorKeyValueTable table)
            {
                continue;
            }

            string path = pair.Key["vars:".Length..];
            if (!referencedVars.Contains(path))
            {
                _contextTabs.Close(pair.Key);
                continue;
            }

            VarsPanelData data = _store.VarsPanel(path);
            if (data != null && data.ContextId == contextId)
                table.SetRows(data.Rows);
        }

        _contextTabs.RevealPages();
    }

    /// <summary>Спрятать контекстные страницы до обновления: иначе виден чужой контекст.</summary>
    public void HideStaleContext()
    {
        if (!_built || !IsInstanceValid(_contextTabs))
            return;

        string activeContext = _store?.ActiveSession?.Kind
            is ContentEntityKind.Unit or ContentEntityKind.Building
            ? _store.ActiveSession.Id
            : null;

        if (!string.Equals(_contextTabs.ContextId, activeContext, StringComparison.Ordinal))
            _contextTabs.Visible = false;
    }

    // ── Мелочи ────────────────────────────────────────────────────────────────────

    private Texture2D CatalogIcon(ContentEditorEntry entry)
    {
        if (!IsInstanceValid(_icons) || _store == null || entry == null)
            return null;

        if (_store.PreviewUnit(entry.Id) is { } unit)
            return _icons.GetUnit(unit);

        var tool = _store.PreviewTool(entry.Id);
        return tool == null ? null : _icons.GetSprite(tool.Sprite);
    }

    /// <summary>Соответствие вкладки каталога виду записи.</summary>
    private static bool MatchesCategory(ContentEntityKind kind, int category) => category switch
    {
        0 => kind == ContentEntityKind.Unit,
        1 => kind == ContentEntityKind.Building,
        2 => kind == ContentEntityKind.Weapon,
        3 => kind == ContentEntityKind.WorkTool,
        _ => false,
    };

    /// <summary>Выделение активной вкладки: без него активная не отличалась от соседних.</summary>
    private static void StyleTabs(TabBar tabs)
    {
        tabs.AddThemeStyleboxOverride("tab_selected", TabStyle(new Color(0.38f, 0.55f, 0.82f)));
        tabs.AddThemeStyleboxOverride("tab_hovered", TabStyle(new Color(0.28f, 0.36f, 0.5f)));
        tabs.AddThemeStyleboxOverride("tab_unselected", TabStyle(new Color(0.14f, 0.15f, 0.18f)));
        tabs.AddThemeColorOverride("font_selected_color", Colors.White);
        tabs.AddThemeColorOverride("font_hovered_color", new Color(0.92f, 0.95f, 1f));
        tabs.AddThemeColorOverride("font_unselected_color", new Color(1f, 1f, 1f, 0.5f));
    }

    private static StyleBoxFlat TabStyle(Color background) => new()
    {
        BgColor = background,
        ContentMarginLeft = 12,
        ContentMarginRight = 12,
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
    };
}
