using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Режим «Balance»: две таблицы по итоговому каталогу редактора, с учётом несохранённых
/// черновиков. Сверху сущности игрока, снизу противник; отбор, набор колонок и сортировка
/// общие для обеих.
///
/// ЧТО ТАКОЕ ПРЕДСТАВЛЕНИЕ. Представление хранит и колонки, и отбор, и сортировку:
/// вопрос «сравнить дальнобойность подвижных машин противника» задаётся и тем, и другим.
/// Пока представление хранило одни колонки, отбор приходилось задавать заново при каждом
/// возврате к той же задаче. Ширина колонок хранится там же — подобранная ширина
/// принадлежит набору колонок, а не таблице вообще.
///
/// ОТКУДА ВЕЛИЧИНЫ. Колонки объявлены в <see cref="BalanceColumns"/> вместе со способом
/// показать значение и сравнить его; здесь остаётся раскладка, отбор и работа
/// с представлениями.
/// </summary>
[Tool]
public partial class ContentEditorBalance : VBoxContainer, IContentEditorMode
{
    private ContentEditorStore _store;
    private ContentEditorIconCache _icons;

    private LineEdit _search;
    private OptionButton _typeFilter;
    private CheckBox _attackers;
    private CheckBox _builders;
    private OptionButton _viewOption;
    private Button _deleteViewButton;
    private PopupPanel _fieldsPopup;
    private VBoxContainer _fieldsList;
    private ConfirmationDialog _saveDialog;
    private ConfirmationDialog _choiceDialog;
    private LineEdit _saveName;
    private VSplitContainer _split;
    private ResizableBalanceTree _playerTree;
    private ResizableBalanceTree _enemyTree;

    /// <summary>Показанные колонки в порядке общего перечня.</summary>
    private readonly List<string> _visibleColumnIds = new(BalanceColumns.ColumnsForView(BalanceColumns.DefaultViewId));

    private readonly List<ContentEditorSavedView> _savedViews = new();

    /// <summary>Подобранная ширина каждой колонки, в том числе скрытой сейчас.</summary>
    private readonly Dictionary<string, int> _columnWidths = new(StringComparer.Ordinal);

    private string _viewId = BalanceColumns.DefaultViewId;
    private string _sortColumnId = BalanceColumns.DefaultSortColumnId;
    private bool _sortAsc;

    /// <summary>Набор колонок, уже применённый к таблицам: по нему видно, нужна ли перестройка.</summary>
    private string[] _appliedColumnIds;

    /// <summary>Перестроение списка представлений не должно считаться выбором представления.</summary>
    private bool _refreshingViews;

    /// <summary>Согласование ширины между таблицами не должно вызывать само себя.</summary>
    private bool _syncingColumnWidths;

    /// <summary>Восстановление состояния не должно порождать запрос на запись снимка.</summary>
    private bool _restoring;

    public string ModeTitle => "Balance";

    public event Action UiStateChanged;
    public event Action<string> EntityOpenRequested;

    // Таблица баланса ничего не пишет на диск и потому сообщать в строку состояния ей
    // не о чем; событие договора остаётся неиспользованным намеренно.
#pragma warning disable CS0067
    public event Action<string> StatusReported;
#pragma warning restore CS0067

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons)
    {
        _store = store;
        _icons = icons;
        EnsureUi();
        Refresh();
    }

    public void Refresh()
    {
        if (_store?.Catalog == null)
            return;

        EnsureUi();
        ApplyColumns();

        string query = (_search?.Text ?? "").Trim();
        int type = _typeFilter?.Selected ?? 0;
        bool attackers = _attackers?.ButtonPressed ?? false;
        bool builders = _builders?.ButtonPressed ?? false;

        var rows = _store.Catalog.Units
            .Where(unit => !string.IsNullOrEmpty(unit.Id))
            .Select(BalanceRow.From)
            .Where(row => type == 0 || (type == 1 && !row.IsStructure) || (type == 2 && row.IsStructure))
            .Where(row => MatchesRole(row, attackers, builders))
            .Where(row => query.Length == 0
                          || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sortColumn = BalanceColumns.ById(_sortColumnId)
                         ?? BalanceColumns.ById(BalanceColumns.DefaultSortColumnId);
        if (sortColumn != null)
            rows.Sort((a, b) => _sortAsc ? sortColumn.Compare(a, b) : -sortColumn.Compare(a, b));

        FillTree(_playerTree, rows.Where(row => !row.IsEnemy));
        FillTree(_enemyTree, rows.Where(row => row.IsEnemy));
    }

    /// <summary>Отбор по роли: без выбранных ролей показываются все.</summary>
    private static bool MatchesRole(BalanceRow row, bool attackers, bool builders) =>
        (!attackers && !builders)
        || (attackers && row.IsAttacker)
        || (builders && row.IsBuilder);

    // ── Снимок состояния ──────────────────────────────────────────────────────────

    public void CaptureInto(ContentEditorWorkspace workspace)
    {
        if (workspace == null || _playerTree == null)
            return;

        CaptureColumnWidths(_playerTree);
        workspace.Balance.ViewId = _viewId;
        workspace.Balance.Split = EditorControls.SplitOffset(_split);
        workspace.Balance.Current = CurrentView();
        workspace.Balance.SavedViews = _savedViews
            .Select(view => new ContentEditorSavedView
            {
                Name = view.Name,
                State = view.State.Clone(),
            })
            .ToList();
    }

    public void RestoreFrom(ContentEditorWorkspace workspace)
    {
        if (workspace?.Balance == null)
            return;

        EnsureUi();
        _restoring = true;
        try
        {
            var state = workspace.Balance;
            _savedViews.Clear();
            foreach (var view in state.SavedViews ?? new List<ContentEditorSavedView>())
            {
                if (string.IsNullOrEmpty(view.Name) || view.State?.ColumnIds is not { Length: > 0 })
                    continue;

                _savedViews.Add(new ContentEditorSavedView
                {
                    Name = view.Name,
                    State = view.State.Clone(),
                });
            }

            ApplyViewState(state.Current);
            _viewId = string.IsNullOrEmpty(state.ViewId) ? ResolveViewId() : state.ViewId;
            if (state.Split != 0)
                EditorControls.SetSplitOffset(_split, state.Split);

            RebuildViewOption();
        }
        finally
        {
            _restoring = false;
        }

        Refresh();
    }

    /// <summary>Действующее состояние в том же виде, в каком его хранит представление.</summary>
    private ContentEditorBalanceViewState CurrentView() => new()
    {
        ColumnIds = _visibleColumnIds.ToArray(),
        ColumnWidths = _visibleColumnIds
            .Select(id => _columnWidths.TryGetValue(id, out int width)
                ? width
                : BalanceColumns.ById(id)?.MinWidth ?? ResizableBalanceTree.MinimumColumnWidth)
            .ToArray(),
        SortColumnId = _sortColumnId,
        SortAsc = _sortAsc,
        Search = _search?.Text ?? "",
        TypeFilter = _typeFilter?.Selected ?? 0,
        Attackers = _attackers?.ButtonPressed ?? false,
        Builders = _builders?.ButtonPressed ?? false,
    };

    /// <summary>Применить состояние представления целиком: колонки, ширины, отбор, сортировку.</summary>
    private void ApplyViewState(ContentEditorBalanceViewState state)
    {
        if (state == null)
            return;

        if (state.ColumnIds is { Length: > 0 })
            SetVisibleColumns(state.ColumnIds);

        // Ширина приходит массивом, параллельным перечню колонок: сопоставляем по номеру
        // и пропускаем колонки, исчезнувшие из перечня после обновления редактора.
        for (int i = 0; i < (state.ColumnIds?.Length ?? 0) && i < (state.ColumnWidths?.Length ?? 0); i++)
        {
            if (BalanceColumns.Exists(state.ColumnIds[i]) && state.ColumnWidths[i] > 0)
                _columnWidths[state.ColumnIds[i]] = state.ColumnWidths[i];
        }

        if (!string.IsNullOrEmpty(state.SortColumnId) && BalanceColumns.Exists(state.SortColumnId))
        {
            _sortColumnId = state.SortColumnId;
            _sortAsc = state.SortAsc;
        }

        if (_search != null)
            _search.Text = state.Search ?? "";
        if (_typeFilter != null)
            _typeFilter.Selected = Mathf.Clamp(state.TypeFilter, 0, _typeFilter.ItemCount - 1);
        if (_attackers != null)
            _attackers.ButtonPressed = state.Attackers;
        if (_builders != null)
            _builders.ButtonPressed = state.Builders;

        _appliedColumnIds = null;
    }

    // ── Раскладка ─────────────────────────────────────────────────────────────────

    private void EnsureUi()
    {
        if (_playerTree != null)
            return;

        // Полоса относится к обеим таблицам сразу, поэтому вынесена в отдельную панель
        // над ними, а не в заголовок одной из них.
        var barPanel = new EditorPanel("Filters and columns")
        {
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        AddChild(barPanel);
        barPanel.AddContent(BuildBar());

        _split = new VSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _split.Dragged += _ => NotifyUiStateChanged();
        AddChild(_split);

        _split.AddChild(BuildTeamPane("Player", new Color(0.55f, 0.75f, 1f),
            "Everything the player can build", out _playerTree));
        _split.AddChild(BuildTeamPane("Enemy", new Color(1f, 0.62f, 0.55f),
            "Definitions of the class Enemy", out _enemyTree));

        BuildFieldsPopup();
        BuildSaveDialogs();
        RebuildViewOption();
    }

    private Control BuildBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);

        _search = new LineEdit
        {
            PlaceholderText = "Search units and structures…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => OnFilterChanged();
        bar.AddChild(_search);

        _typeFilter = new OptionButton();
        _typeFilter.AddItem("All", 0);
        _typeFilter.AddItem("Mobile", 1);
        _typeFilter.AddItem("Structures", 2);
        _typeFilter.ItemSelected += _ => OnFilterChanged();
        bar.AddChild(_typeFilter);

        _attackers = new CheckBox { Text = "Attackers" };
        _attackers.Toggled += _ => OnFilterChanged();
        bar.AddChild(_attackers);

        _builders = new CheckBox { Text = "Builders" };
        _builders.Toggled += _ => OnFilterChanged();
        bar.AddChild(_builders);

        bar.AddChild(new Label { Text = "View" });
        _viewOption = new OptionButton
        {
            TooltipText = "A view keeps its columns, their widths, the filters and the sorting",
        };
        _viewOption.ItemSelected += OnViewSelected;
        bar.AddChild(_viewOption);

        var fields = new Button
        {
            Text = "Fields",
            Icon = ContentEditorTheme.IconAny("ListSelect", "GuiTabMenuHl", "Tools"),
            TooltipText = "Choose which columns both tables show",
        };
        fields.Pressed += () => EditorControls.Run("open field list", ShowFieldsPopup);
        bar.AddChild(fields);

        EditorControls.Add(bar, "Save view…", "Save", RequestSaveView,
            "Store the current columns, widths, filters and sorting under a name");

        // EditorControls.Add уже помещает кнопку в полосу: второе добавление того же узла
        // было бы попыткой дать ему второго родителя.
        _deleteViewButton = EditorControls.Add(bar, "Delete view", "Remove", DeleteCurrentView);
        return bar;
    }

    private Control BuildTeamPane(
        string title, Color accent, string tooltip, out ResizableBalanceTree tree)
    {
        var pane = new EditorPanel(title) { TitleTooltip = tooltip };
        pane.SetTitleColor(accent);

        tree = CreateTree();
        pane.AddContent(tree);
        return pane;
    }

    private ResizableBalanceTree CreateTree()
    {
        var tree = new ResizableBalanceTree
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ColumnTitlesVisible = true,
            HideRoot = true,
            HideFolding = true,
            SelectMode = Tree.SelectModeEnum.Row,
        };

        tree.ColumnTitleClicked += (column, _) =>
        {
            // Отпускание кнопки после перетаскивания разделителя Godot сообщает и как
            // щелчок по заголовку; сортировку в этом случае менять не следует.
            if (tree.IsResizingColumns)
                return;

            EditorControls.Run("sort table", () => OnColumnTitleClicked((int)column));
        };
        tree.ColumnWidthsChanged += SyncColumnWidthsFrom;
        tree.ItemActivated += () => EditorControls.Run("open entity", () =>
        {
            string id = tree.GetSelected()?.GetMetadata(0).AsString();
            if (!string.IsNullOrEmpty(id))
                EntityOpenRequested?.Invoke(id);
        });
        return tree;
    }

    private void BuildFieldsPopup()
    {
        _fieldsPopup = new PopupPanel();
        AddChild(_fieldsPopup);

        _fieldsList = new VBoxContainer();
        _fieldsList.AddThemeConstantOverride("separation", 4);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddChild(_fieldsList);
        _fieldsPopup.AddChild(margin);
    }

    private void BuildSaveDialogs()
    {
        // Выбранное сохранённое представление перезаписывается только по явному указанию:
        // молчаливая перезапись уничтожала бы чужой набор колонок.
        _choiceDialog = new ConfirmationDialog
        {
            Title = "Save view",
            OkButtonText = "Update",
        };
        _choiceDialog.Confirmed += () => EditorControls.Run("update view", OverwriteCurrentView);
        AddChild(_choiceDialog);
        _choiceDialog.AddButton("Save as new…", true, "new");
        _choiceDialog.CustomAction += action =>
        {
            if (action != "new")
                return;

            _choiceDialog.Hide();
            OpenNameDialog();
        };

        _saveDialog = new ConfirmationDialog
        {
            Title = "New view",
            OkButtonText = "Save",
        };
        _saveName = new LineEdit { PlaceholderText = "View name" };
        // Подтверждение без имени не создавало представления и выглядело сбоем.
        _saveName.TextChanged += _ => UpdateSaveDialogState();
        _saveDialog.AddChild(_saveName);
        _saveDialog.Confirmed += () => EditorControls.Run("save view", SaveCurrentView);
        AddChild(_saveDialog);
        UpdateSaveDialogState();
    }

    // ── Колонки ───────────────────────────────────────────────────────────────────

    /// <summary>Согласовать состав и ширину колонок таблиц с текущим набором.</summary>
    private void ApplyColumns()
    {
        if (_visibleColumnIds.Count == 0)
            SetVisibleColumns(BalanceColumns.ColumnsForView(BalanceColumns.DefaultViewId));

        bool sameLayout = _appliedColumnIds != null
                          && _appliedColumnIds.SequenceEqual(_visibleColumnIds, StringComparer.Ordinal);
        if (!sameLayout)
        {
            CaptureColumnWidths(_playerTree);
            ConfigureTree(_playerTree);
            ConfigureTree(_enemyTree);
            _appliedColumnIds = _visibleColumnIds.ToArray();
        }

        ApplyColumnTitles(_playerTree);
        ApplyColumnTitles(_enemyTree);
    }

    private void ConfigureTree(Tree tree)
    {
        tree.Columns = _visibleColumnIds.Count;
        for (int i = 0; i < _visibleColumnIds.Count; i++)
        {
            var column = BalanceColumns.ById(_visibleColumnIds[i]);
            if (column == null)
                continue;

            bool stored = _columnWidths.TryGetValue(column.Id, out int width);
            tree.SetColumnExpand(i, column.Expand && !stored);
            tree.SetColumnCustomMinimumWidth(i, stored ? width : column.MinWidth);
            tree.SetColumnClipContent(i, true);
        }
    }

    private void ApplyColumnTitles(Tree tree)
    {
        for (int i = 0; i < _visibleColumnIds.Count && i < tree.Columns; i++)
        {
            var column = BalanceColumns.ById(_visibleColumnIds[i]);
            if (column == null)
                continue;

            tree.SetColumnTitle(i, column.Id == _sortColumnId
                ? column.Title + (_sortAsc ? " ▲" : " ▼")
                : column.Title);
        }
    }

    /// <summary>Запомнить ширину колонок таблицы, чтобы перенести её на другой набор.</summary>
    private void CaptureColumnWidths(Tree tree)
    {
        if (tree == null || _appliedColumnIds == null)
            return;

        int count = Mathf.Min(_appliedColumnIds.Length, tree.Columns);
        for (int i = 0; i < count; i++)
        {
            _columnWidths[_appliedColumnIds[i]] =
                Mathf.Max(ResizableBalanceTree.MinimumColumnWidth, tree.GetColumnWidth(i));
        }
    }

    /// <summary>Перенести ширину, заданную мышью, на вторую таблицу и в снимок.</summary>
    private void SyncColumnWidthsFrom(ResizableBalanceTree source)
    {
        if (_syncingColumnWidths || _playerTree == null || _enemyTree == null)
            return;

        var other = source == _playerTree ? _enemyTree : _playerTree;
        if (other.Columns != source.Columns)
            return;

        _syncingColumnWidths = true;
        try
        {
            for (int i = 0; i < source.Columns; i++)
            {
                int width = source.GetColumnWidth(i);
                other.SetColumnCustomMinimumWidth(i, width);
                other.SetColumnExpand(i, false);
            }

            CaptureColumnWidths(source);
        }
        finally
        {
            _syncingColumnWidths = false;
        }

        NotifyUiStateChanged();
    }

    /// <summary>Оставить видимыми перечисленные колонки, сохранив общий порядок перечня.</summary>
    private void SetVisibleColumns(IEnumerable<string> ids)
    {
        var enabled = new HashSet<string>(ids.Where(BalanceColumns.Exists), StringComparer.Ordinal)
        {
            BalanceColumns.NameColumnId,
        };

        _visibleColumnIds.Clear();
        foreach (var column in BalanceColumns.All)
        {
            if (enabled.Contains(column.Id))
                _visibleColumnIds.Add(column.Id);
        }

        _appliedColumnIds = null;
    }

    private void FillTree(Tree tree, IEnumerable<BalanceRow> rows)
    {
        tree.Clear();
        var root = tree.CreateItem();
        int index = 0;

        foreach (var row in rows)
        {
            var item = tree.CreateItem(root);
            item.SetMetadata(0, row.Id);

            // Чередование фона: без него строка теряется при десятке колонок.
            var stripe = index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.03f)
                : new Color(1f, 1f, 1f, 0.07f);

            for (int i = 0; i < _visibleColumnIds.Count; i++)
            {
                var column = BalanceColumns.ById(_visibleColumnIds[i]);
                if (column == null)
                    continue;

                item.SetText(i, column.Text(row));
                item.SetCustomBgColor(i, stripe);

                if (column.Id != BalanceColumns.NameColumnId || row.Definition == null || _icons == null)
                    continue;

                // Иконка в колонке имени — тот же силуэт, что в строительной панели игры.
                item.SetIconMaxWidth(i, ContentEditorIconCache.PixelSize);
                item.SetIcon(i, _icons.GetUnit(row.Definition));
            }

            index++;
        }
    }

    private void OnColumnTitleClicked(int visualColumn)
    {
        if (visualColumn < 0 || visualColumn >= _visibleColumnIds.Count)
            return;

        string id = _visibleColumnIds[visualColumn];
        if (_sortColumnId == id)
        {
            _sortAsc = !_sortAsc;
        }
        else
        {
            _sortColumnId = id;
            _sortAsc = BalanceColumns.ById(id)?.AscendingByDefault ?? true;
        }

        Refresh();
        NotifyUiStateChanged();
    }

    private void ShowFieldsPopup()
    {
        EditorControls.ClearChildren(_fieldsList);

        string section = null;
        foreach (var column in BalanceColumns.All)
        {
            if (column.Section != section)
            {
                section = column.Section;
                if (_fieldsList.GetChildCount() > 0)
                    _fieldsList.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

                var heading = new Label { Text = section };
                heading.AddThemeColorOverride("font_color", new Color(0.7f, 0.8f, 0.95f));
                heading.AddThemeFontSizeOverride("font_size", 13);
                _fieldsList.AddChild(heading);
            }

            string id = column.Id;
            var box = new CheckBox
            {
                Text = column.Title,
                ButtonPressed = _visibleColumnIds.Contains(id, StringComparer.Ordinal),
                // Колонка имени показывается всегда: строка без имени неразличима.
                Disabled = id == BalanceColumns.NameColumnId,
            };
            box.Toggled += on => EditorControls.Run("toggle column", () => ToggleColumn(id, on));
            _fieldsList.AddChild(box);
        }

        _fieldsPopup.PopupCentered(new Vector2I(260, 520));
    }

    private void ToggleColumn(string id, bool on)
    {
        if (id == BalanceColumns.NameColumnId)
            return;

        CaptureColumnWidths(_playerTree);
        var enabled = _visibleColumnIds.ToHashSet(StringComparer.Ordinal);
        if (on)
            enabled.Add(id);
        else
            enabled.Remove(id);

        SetVisibleColumns(enabled);
        SyncViewToState();
        Refresh();
        NotifyUiStateChanged();
    }

    // ── Представления ─────────────────────────────────────────────────────────────

    private void OnFilterChanged()
    {
        if (_restoring)
            return;

        EditorControls.Run("filter balance", () =>
        {
            SyncViewToState();
            Refresh();
            NotifyUiStateChanged();
        });
    }

    private void OnViewSelected(long index)
    {
        if (_refreshingViews)
            return;

        EditorControls.Run("switch view", () =>
        {
            string id = _viewOption.GetItemMetadata((int)index).AsString();
            ApplyView(id);
        });
    }

    /// <summary>
    /// Применить представление. Встроенное задаёт только колонки: отбор при этом
    /// сохраняется, поскольку заготовка отвечает на вопрос, какие величины сравнивать,
    /// а не какие строки. Сохранённое представление задано пользователем целиком
    /// и потому применяется целиком.
    /// </summary>
    private void ApplyView(string viewId)
    {
        if (viewId == BalanceColumns.CustomViewId)
            return;

        CaptureColumnWidths(_playerTree);
        _viewId = viewId;

        var saved = _savedViews.FirstOrDefault(view => SavedViewId(view) == viewId);
        if (saved != null)
        {
            _restoring = true;
            try
            {
                ApplyViewState(saved.State);
            }
            finally
            {
                _restoring = false;
            }
        }
        else
        {
            SetVisibleColumns(BalanceColumns.ColumnsForView(viewId));
        }

        RebuildViewOption();
        Refresh();
        NotifyUiStateChanged();
    }

    /// <summary>Согласовать имя представления с действующим состоянием.</summary>
    private void SyncViewToState()
    {
        _viewId = ResolveViewId();
        RebuildViewOption();
    }

    /// <summary>
    /// Какому представлению отвечает действующее состояние. Сохранённое сравнивается
    /// целиком, встроенное — только по колонкам: отбор в него не входит.
    /// </summary>
    private string ResolveViewId()
    {
        var current = CurrentView();

        foreach (var saved in _savedViews)
        {
            if (SameState(current, saved.State))
                return SavedViewId(saved);
        }

        foreach (var built in BalanceColumns.BuiltInViews)
        {
            if (SameColumnSet(_visibleColumnIds, built.Columns))
                return built.Id;
        }

        return BalanceColumns.CustomViewId;
    }

    private static bool SameColumnSet(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left != null
        && right != null
        && left.Count == right.Count
        && left.ToHashSet(StringComparer.Ordinal).SetEquals(right);

    /// <summary>
    /// Совпадают ли состояния по существу. Ширина колонок в сравнение не входит: она
    /// подбирается мышью постоянно, и представление не должно из-за неё становиться
    /// «Custom view».
    /// </summary>
    private static bool SameState(
        ContentEditorBalanceViewState left, ContentEditorBalanceViewState right) =>
        right != null
        && SameColumnSet(left.ColumnIds, right.ColumnIds)
        && string.Equals(left.SortColumnId, right.SortColumnId, StringComparison.Ordinal)
        && left.SortAsc == right.SortAsc
        && string.Equals(left.Search ?? "", right.Search ?? "", StringComparison.Ordinal)
        && left.TypeFilter == right.TypeFilter
        && left.Attackers == right.Attackers
        && left.Builders == right.Builders;

    private void RebuildViewOption()
    {
        if (_viewOption == null)
            return;

        _refreshingViews = true;
        try
        {
            _viewOption.Clear();
            int selected = 0;
            int index = 0;

            foreach (var built in BalanceColumns.BuiltInViews)
            {
                _viewOption.AddItem(built.Title);
                _viewOption.SetItemMetadata(index, built.Id);
                if (built.Id == _viewId)
                    selected = index;
                index++;
            }

            foreach (var saved in _savedViews)
            {
                _viewOption.AddItem(saved.Name);
                _viewOption.SetItemMetadata(index, SavedViewId(saved));
                if (SavedViewId(saved) == _viewId)
                    selected = index;
                index++;
            }

            _viewOption.Selected = selected;
            if (_viewId == BalanceColumns.CustomViewId)
            {
                _viewOption.AddItem("Custom view");
                _viewOption.SetItemMetadata(index, BalanceColumns.CustomViewId);
                _viewOption.Selected = index;
            }

            ContentEditorTheme.SetAction(_deleteViewButton, IsSavedView(_viewId),
                "Delete the selected saved view",
                "Built-in views cannot be deleted");
        }
        finally
        {
            _refreshingViews = false;
        }
    }

    private void UpdateSaveDialogState()
    {
        if (_saveDialog?.GetOkButton() is { } ok && GodotObject.IsInstanceValid(ok))
            ok.Disabled = (_saveName?.Text ?? "").Trim().Length == 0;
    }

    private void RequestSaveView()
    {
        if (!IsSavedView(_viewId))
        {
            OpenNameDialog();
            return;
        }

        string name = SavedViewName(_viewId);
        _choiceDialog.DialogText =
            $"The view “{name}” is selected. Update it with the current columns, filters "
            + "and sorting, or save them as a new view?";
        _choiceDialog.OkButtonText = $"Update “{name}”";
        _choiceDialog.PopupCentered();
    }

    private void OpenNameDialog()
    {
        _saveName.Text = "";
        UpdateSaveDialogState();
        _saveDialog.PopupCentered();
    }

    private void OverwriteCurrentView()
    {
        var view = _savedViews.FirstOrDefault(candidate => SavedViewId(candidate) == _viewId);
        if (view == null)
            return;

        CaptureColumnWidths(_playerTree);
        view.State = CurrentView();
        RebuildViewOption();
        NotifyUiStateChanged();
    }

    private void SaveCurrentView()
    {
        string name = (_saveName.Text ?? "").Trim();
        if (name.Length == 0)
            return;

        CaptureColumnWidths(_playerTree);
        var existing = _savedViews.FirstOrDefault(view =>
            string.Equals(view.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.State = CurrentView();
        }
        else
        {
            existing = new ContentEditorSavedView { Name = name, State = CurrentView() };
            _savedViews.Add(existing);
        }

        _viewId = SavedViewId(existing);
        RebuildViewOption();
        NotifyUiStateChanged();
    }

    private void DeleteCurrentView()
    {
        if (!IsSavedView(_viewId))
            return;

        _savedViews.RemoveAll(view => SavedViewId(view) == _viewId);
        _viewId = BalanceColumns.DefaultViewId;
        SetVisibleColumns(BalanceColumns.ColumnsForView(_viewId));
        RebuildViewOption();
        Refresh();
        NotifyUiStateChanged();
    }

    private void NotifyUiStateChanged()
    {
        if (!_restoring)
            UiStateChanged?.Invoke();
    }

    /// <summary>Идентификатор сохранённого представления выводится из имени и потому уникален.</summary>
    private static string SavedViewId(ContentEditorSavedView view) => "saved:" + view.Name;

    private static string SavedViewName(string viewId) =>
        IsSavedView(viewId) ? viewId["saved:".Length..] : viewId;

    private static bool IsSavedView(string viewId) =>
        viewId != null && viewId.StartsWith("saved:", StringComparison.Ordinal);
}
