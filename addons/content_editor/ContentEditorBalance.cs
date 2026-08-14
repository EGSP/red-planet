using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Таблица баланса по итоговому Catalog редактора (с учётом несохранённых черновиков).
///
/// Две таблицы по вертикали: Player сверху, Enemy снизу. Фильтры, вид колонок и
/// сортировка общие. DPS = damage/fire_interval. Двойной щелчок шлёт OpenRequested.
/// </summary>
[Tool]
public partial class ContentEditorBalance : VBoxContainer
{
    public const string DefaultViewId = "combat";
    public const string CustomViewId = "custom";
    public const string DefaultSortColumnId = "army";

    private static readonly BalanceColumn[] AllColumns =
    {
        new("name", "Name", "Identity", 160, true),
        new("id", "Id", "Identity", 90, false),
        new("class", "Class", "Identity", 80, false),
        new("army", "Army Power", "Combat", 88, false),
        new("hp", "Health", "Combat", 64, false),
        new("dps", "DPS", "Combat", 64, false),
        new("range", "Range", "Combat", 64, false),
        new("vision", "Vision", "Combat", 64, false),
        new("speed", "Speed", "Combat", 64, false),
        new("size", "Size", "Combat", 72, false),
        new("cost", "Cost", "Economy", 64, false),
        new("hp_per_metal", "Health/Metal", "Economy", 96, false),
        new("energy_net", "Net Energy", "Economy", 88, false),
        new("metal_net", "Net Metal", "Economy", 80, false),
        new("expansion", "Expansion Power", "Economy", 112, false),
        new("build_power", "Build Power", "Construction", 88, false),
    };

    private static readonly string[] CombatColumns =
        { "name", "class", "army", "hp", "cost", "speed", "vision", "dps", "range", "size" };

    private static readonly string[] EconomyColumns =
        { "name", "class", "army", "cost", "energy_net", "metal_net", "hp" };

    private static readonly string[] ConstructionColumns =
        { "name", "class", "army", "cost", "build_power", "size", "hp" };

    private static readonly (string Id, string Title)[] BuiltInViews =
    {
        ("all", "All"),
        ("combat", "Combat"),
        ("economy", "Economy"),
        ("construction", "Construction"),
    };

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
    private Tree _playerTree;
    private Tree _enemyTree;
    private bool _syncingColumnWidths;
    private readonly List<string> _visibleColumnIds = new(CombatColumns);
    private readonly List<ContentEditorSavedView> _savedViews = new();
    private string _viewId = DefaultViewId;
    private string _sortColumnId = DefaultSortColumnId;
    private bool _sortAsc;
    private string[] _appliedColumnIds;
    private bool _refreshingViews;
    private readonly Dictionary<string, int> _columnWidths = new(StringComparer.Ordinal);

    public event Action<string> OpenRequested;
    public event Action UiStateChanged;

    public void Bind(ContentEditorStore store, ContentEditorIconCache icons = null)
    {
        _store = store;
        _icons = icons;
        EnsureUi();
        Refresh();
    }

    public ContentEditorBalanceUiState CaptureUiState() => new()
    {
        ViewId = _viewId,
        VisibleColumnIds = _visibleColumnIds.ToArray(),
        SortColumnId = _sortColumnId,
        SortAsc = _sortAsc,
        SavedViews = _savedViews
            .Select(view => new ContentEditorSavedView
            {
                Name = view.Name,
                ColumnIds = view.ColumnIds?.ToArray(),
            })
            .ToList(),
    };

    public void RestoreUiState(ContentEditorBalanceUiState state)
    {
        if (state == null)
            return;

        _savedViews.Clear();
        if (state.SavedViews != null)
        {
            foreach (var view in state.SavedViews)
            {
                if (string.IsNullOrEmpty(view.Name) || view.ColumnIds == null || view.ColumnIds.Length == 0)
                    continue;
                _savedViews.Add(new ContentEditorSavedView
                {
                    Name = view.Name,
                    ColumnIds = view.ColumnIds.ToArray(),
                });
            }
        }

        _viewId = string.IsNullOrEmpty(state.ViewId) ? DefaultViewId : state.ViewId;
        _sortColumnId = string.IsNullOrEmpty(state.SortColumnId) ? DefaultSortColumnId : state.SortColumnId;
        _sortAsc = state.SortAsc;
        if (state.VisibleColumnIds is { Length: > 0 })
            SetVisibleColumns(state.VisibleColumnIds);
        else
            SetVisibleColumns(ColumnsForView(string.IsNullOrEmpty(state.ViewId) ? DefaultViewId : state.ViewId));

        EnsureUi();
        SyncViewToVisibleColumns();
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
            .Where(u => !string.IsNullOrEmpty(u.Id))
            .Select(BalanceRow.From)
            .Where(r => type == 0 || (type == 1 && !r.IsStructure) || (type == 2 && r.IsStructure))
            .Where(r => MatchesRole(r, attackers, builders))
            .Where(r => query.Length == 0
                        || r.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        rows.Sort((a, b) =>
        {
            int cmp = Compare(a, b, _sortColumnId);
            return _sortAsc ? cmp : -cmp;
        });

        FillTree(_playerTree, rows.Where(r => !r.IsEnemy));
        FillTree(_enemyTree, rows.Where(r => r.IsEnemy));
    }

    private static bool MatchesRole(BalanceRow row, bool attackers, bool builders)
    {
        if (!attackers && !builders)
            return true;
        return attackers && row.IsAttacker || builders && row.IsBuilder;
    }

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

        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);
        barPanel.AddContent(bar);

        _search = new LineEdit
        {
            PlaceholderText = "Search units and structures…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _search.TextChanged += _ => Refresh();
        bar.AddChild(_search);

        _typeFilter = new OptionButton();
        _typeFilter.AddItem("All", 0);
        _typeFilter.AddItem("Mobile", 1);
        _typeFilter.AddItem("Structures", 2);
        _typeFilter.ItemSelected += _ => Refresh();
        bar.AddChild(_typeFilter);

        _attackers = new CheckBox { Text = "Attackers" };
        _attackers.Toggled += _ => Refresh();
        bar.AddChild(_attackers);

        _builders = new CheckBox { Text = "Builders" };
        _builders.Toggled += _ => Refresh();
        bar.AddChild(_builders);

        bar.AddChild(new Label { Text = "View" });
        _viewOption = new OptionButton();
        _viewOption.ItemSelected += OnViewSelected;
        bar.AddChild(_viewOption);

        var fieldsButton = new Button
        {
            Text = "Fields",
            Icon = ContentEditorTheme.IconAny("ListSelect", "GuiTabMenuHl", "Tools"),
            TooltipText = "Choose which columns both tables show",
        };
        fieldsButton.Pressed += ShowFieldsPopup;
        bar.AddChild(fieldsButton);

        var saveButton = new Button
        {
            Text = "Save view…",
            Icon = ContentEditorTheme.Icon("Save"),
            TooltipText = "Store the current column set under a name",
        };
        saveButton.Pressed += RequestSaveView;
        bar.AddChild(saveButton);

        _deleteViewButton = new Button
        {
            Text = "Delete view",
            Icon = ContentEditorTheme.IconAny("Remove", "Close"),
        };
        _deleteViewButton.Pressed += DeleteCurrentView;
        bar.AddChild(_deleteViewButton);

        var split = new VSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(split);

        split.AddChild(BuildTeamPane("Player", new Color(0.55f, 0.75f, 1f), out _playerTree));
        split.AddChild(BuildTeamPane("Enemy", new Color(1f, 0.62f, 0.55f), out _enemyTree));

        _fieldsPopup = new PopupPanel();
        AddChild(_fieldsPopup);
        _fieldsList = new VBoxContainer();
        _fieldsList.AddThemeConstantOverride("separation", 4);
        var fieldsMargin = new MarginContainer();
        fieldsMargin.AddThemeConstantOverride("margin_left", 10);
        fieldsMargin.AddThemeConstantOverride("margin_right", 10);
        fieldsMargin.AddThemeConstantOverride("margin_top", 8);
        fieldsMargin.AddThemeConstantOverride("margin_bottom", 8);
        fieldsMargin.AddChild(_fieldsList);
        _fieldsPopup.AddChild(fieldsMargin);

        // Выбранный сохранённый вид перезаписывается только по явному указанию:
        // молчаливая перезапись уничтожала бы чужой набор колонок при попытке
        // сохранить свой.
        _choiceDialog = new ConfirmationDialog
        {
            Title = "Save view",
            OkButtonText = "Update",
        };
        _choiceDialog.Confirmed += OverwriteCurrentView;
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
        // Подтверждение без имени не создавало вид и выглядело сбоем; кнопка выключена,
        // пока имя пусто.
        _saveName.TextChanged += _ => UpdateSaveDialogState();
        _saveDialog.AddChild(_saveName);
        _saveDialog.Confirmed += SaveCurrentView;
        AddChild(_saveDialog);
        UpdateSaveDialogState();

        RebuildViewOption();
    }

    private Control BuildTeamPane(string title, Color accent, out Tree tree)
    {
        var pane = new EditorPanel(title);
        pane.TitleTooltip = title == "Player"
            ? "Everything the player can build"
            : "Definitions of the class Enemy";
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
            if (tree.IsResizingColumns)
                return;
            OnColumnTitleClicked((int)column);
        };
        tree.ColumnWidthsChanged += SyncColumnWidthsFrom;
        tree.ItemActivated += () =>
        {
            var item = tree.GetSelected();
            if (item == null)
                return;
            string id = item.GetMetadata(0).AsString();
            if (!string.IsNullOrEmpty(id))
                OpenRequested?.Invoke(id);
        };
        return tree;
    }

    private void SyncColumnWidthsFrom(ResizableBalanceTree source)
    {
        if (_syncingColumnWidths || _playerTree == null || _enemyTree == null)
            return;

        var other = source == _playerTree ? _enemyTree : _playerTree;
        if (other == null || other.Columns != source.Columns)
            return;

        _syncingColumnWidths = true;
        for (int i = 0; i < source.Columns; i++)
        {
            int width = source.GetColumnWidth(i);
            other.SetColumnCustomMinimumWidth(i, width);
            other.SetColumnExpand(i, false);
        }

        CaptureColumnWidths(source);
        _syncingColumnWidths = false;
    }

    private void ApplyColumns()
    {
        if (_visibleColumnIds.Count == 0)
            SetVisibleColumns(CombatColumns);

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

    private void CaptureColumnWidths(Tree tree)
    {
        if (tree == null || _appliedColumnIds == null)
            return;

        int count = Mathf.Min(_appliedColumnIds.Length, tree.Columns);
        for (int i = 0; i < count; i++)
            _columnWidths[_appliedColumnIds[i]] = Mathf.Max(36, tree.GetColumnWidth(i));
    }

    private void ConfigureTree(Tree tree)
    {
        tree.Columns = _visibleColumnIds.Count;
        for (int i = 0; i < _visibleColumnIds.Count; i++)
        {
            var column = Column(_visibleColumnIds[i]);
            int width = _columnWidths.TryGetValue(column.Id, out int stored)
                ? stored
                : column.MinWidth;
            bool expand = column.Expand && !_columnWidths.ContainsKey(column.Id);
            tree.SetColumnExpand(i, expand);
            tree.SetColumnCustomMinimumWidth(i, width);
            tree.SetColumnClipContent(i, true);
        }
    }

    private void ApplyColumnTitles(Tree tree)
    {
        for (int i = 0; i < _visibleColumnIds.Count; i++)
        {
            var column = Column(_visibleColumnIds[i]);
            tree.SetColumnTitle(i, ColumnTitle(column));
        }
    }

    private string ColumnTitle(BalanceColumn column)
    {
        if (column.Id != _sortColumnId)
            return column.Title;
        return column.Title + (_sortAsc ? " ▲" : " ▼");
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
            var stripe = index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.03f)
                : new Color(1f, 1f, 1f, 0.07f);
            for (int i = 0; i < _visibleColumnIds.Count; i++)
            {
                FillCell(item, i, _visibleColumnIds[i], row);
                item.SetCustomBgColor(i, stripe);
            }

            index++;
        }
    }

    private void FillCell(TreeItem item, int column, string id, BalanceRow row)
    {
        switch (id)
        {
            case "name":
                item.SetText(column, row.Name);
                item.SetIconMaxWidth(column, ContentEditorIconCache.PixelSize);
                if (row.Definition != null && _icons != null)
                    item.SetIcon(column, _icons.GetUnit(row.Definition));
                break;
            case "id":
                item.SetText(column, row.Id);
                break;
            case "class":
                item.SetText(column, row.Class);
                break;
            case "army":
                item.SetText(column, F(row.ArmyPower));
                break;
            case "hp":
                item.SetText(column, F(row.Health));
                break;
            case "cost":
                item.SetText(column, F(row.Cost));
                break;
            case "speed":
                item.SetText(column, F(row.Speed));
                break;
            case "vision":
                item.SetText(column, F(row.Vision));
                break;
            case "size":
                item.SetText(column, row.Size);
                break;
            case "dps":
                item.SetText(column, F(row.Dps));
                break;
            case "range":
                item.SetText(column, F(row.WeaponRange));
                break;
            case "hp_per_metal":
                item.SetText(column, F(row.HealthPerMetal));
                break;
            case "energy_net":
                item.SetText(column, F(row.EnergyNet));
                break;
            case "metal_net":
                item.SetText(column, F(row.MetalNet));
                break;
            case "expansion":
                item.SetText(column, F(row.ExpansionPower));
                break;
            case "build_power":
                item.SetText(column, F(row.BuildPower));
                break;
        }
    }

    private void OnColumnTitleClicked(int visualColumn)
    {
        if (visualColumn < 0 || visualColumn >= _visibleColumnIds.Count)
            return;

        string id = _visibleColumnIds[visualColumn];
        if (_sortColumnId == id)
            _sortAsc = !_sortAsc;
        else
        {
            _sortColumnId = id;
            _sortAsc = id is "name" or "id" or "class" or "size";
        }

        Refresh();
        NotifyUiStateChanged();
    }

    private void OnViewSelected(long index)
    {
        if (_refreshingViews)
            return;

        string id = _viewOption.GetItemMetadata((int)index).AsString();
        ApplyView(id);
    }

    private void ApplyView(string viewId)
    {
        if (viewId == CustomViewId)
            return;

        _viewId = viewId;
        SetVisibleColumns(ColumnsForView(viewId));
        Refresh();
        NotifyUiStateChanged();
    }

    private void SetVisibleColumns(IEnumerable<string> ids)
    {
        var enabled = new HashSet<string>(ids.Where(ColumnExists), StringComparer.Ordinal);
        enabled.Add("name");
        _visibleColumnIds.Clear();
        foreach (var column in AllColumns)
        {
            if (enabled.Contains(column.Id))
                _visibleColumnIds.Add(column.Id);
        }

        _appliedColumnIds = null;
    }

    private void SyncViewToVisibleColumns()
    {
        _viewId = ResolveViewId(_visibleColumnIds);
        RebuildViewOption();
    }

    private string ResolveViewId(IReadOnlyList<string> visible)
    {
        foreach (var built in BuiltInViews)
        {
            if (SameColumnSet(visible, ColumnsForView(built.Id)))
                return built.Id;
        }

        foreach (var saved in _savedViews)
        {
            if (saved.ColumnIds != null && SameColumnSet(visible, saved.ColumnIds))
                return SavedViewId(saved);
        }

        return CustomViewId;
    }

    private static bool SameColumnSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;
        return left.ToHashSet(StringComparer.Ordinal).SetEquals(right);
    }

    private string[] ColumnsForView(string viewId)
    {
        foreach (var built in BuiltInViews)
        {
            if (built.Id == viewId)
            {
                return built.Id switch
                {
                    "combat" => CombatColumns,
                    "economy" => EconomyColumns,
                    "construction" => ConstructionColumns,
                    _ => AllColumns.Select(c => c.Id).ToArray(),
                };
            }
        }

        var saved = _savedViews.FirstOrDefault(v => SavedViewId(v) == viewId);
        if (saved?.ColumnIds is { Length: > 0 })
            return saved.ColumnIds;

        return _visibleColumnIds.Count > 0 ? _visibleColumnIds.ToArray() : CombatColumns;
    }

    private void RebuildViewOption()
    {
        if (_viewOption == null)
            return;

        _refreshingViews = true;
        _viewOption.Clear();
        int selected = 0;
        int index = 0;
        foreach (var built in BuiltInViews)
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
        if (_viewId == CustomViewId)
        {
            _viewOption.AddItem("Custom view");
            _viewOption.SetItemMetadata(index, CustomViewId);
            _viewOption.Selected = index;
        }

        ContentEditorTheme.SetAction(_deleteViewButton, IsSavedView(_viewId),
            "Delete the selected saved view",
            "Built-in views cannot be deleted");
        _refreshingViews = false;
    }

    private void UpdateSaveDialogState()
    {
        if (_saveDialog == null || _saveName == null)
            return;

        var ok = _saveDialog.GetOkButton();
        if (GodotObject.IsInstanceValid(ok))
            ok.Disabled = (_saveName.Text ?? "").Trim().Length == 0;
    }

    private void ShowFieldsPopup()
    {
        foreach (Node child in _fieldsList.GetChildren())
        {
            _fieldsList.RemoveChild(child);
            child.QueueFree();
        }

        string section = null;
        foreach (var column in AllColumns)
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

            bool locked = column.Id == "name";
            var box = new CheckBox
            {
                Text = column.Title,
                ButtonPressed = _visibleColumnIds.Contains(column.Id, StringComparer.Ordinal),
                Disabled = locked,
            };
            string id = column.Id;
            box.Toggled += on => ToggleColumn(id, on);
            _fieldsList.AddChild(box);
        }

        _fieldsPopup.PopupCentered(new Vector2I(260, 520));
    }

    private void ToggleColumn(string id, bool on)
    {
        if (id == "name")
            return;

        var enabled = _visibleColumnIds.ToHashSet(StringComparer.Ordinal);
        if (on)
            enabled.Add(id);
        else
            enabled.Remove(id);

        SetVisibleColumns(enabled);
        SyncViewToVisibleColumns();
        Refresh();
        NotifyUiStateChanged();
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
            $"The view “{name}” is selected. Update it with the current columns, "
            + "or save the current columns as a new view?";
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

        view.ColumnIds = _visibleColumnIds.ToArray();
        RebuildViewOption();
        NotifyUiStateChanged();
    }

    private void SaveCurrentView()
    {
        string name = (_saveName.Text ?? "").Trim();
        if (name.Length == 0)
            return;

        var existing = _savedViews.FirstOrDefault(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.ColumnIds = _visibleColumnIds.ToArray();
        else
        {
            _savedViews.Add(new ContentEditorSavedView
            {
                Name = name,
                ColumnIds = _visibleColumnIds.ToArray(),
            });
        }

        _viewId = SavedViewId(_savedViews.First(v =>
            string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)));
        RebuildViewOption();
        NotifyUiStateChanged();
    }

    private void DeleteCurrentView()
    {
        if (!IsSavedView(_viewId))
            return;

        _savedViews.RemoveAll(v => SavedViewId(v) == _viewId);
        _viewId = DefaultViewId;
        SetVisibleColumns(CombatColumns);
        RebuildViewOption();
        Refresh();
        NotifyUiStateChanged();
    }

    private void NotifyUiStateChanged() => UiStateChanged?.Invoke();

    private static string SavedViewId(ContentEditorSavedView view) => "saved:" + view.Name;

    private static string SavedViewName(string viewId) =>
        IsSavedView(viewId) ? viewId["saved:".Length..] : viewId;

    private static bool IsSavedView(string viewId) =>
        viewId != null && viewId.StartsWith("saved:", StringComparison.Ordinal);

    private static bool ColumnExists(string id) => AllColumns.Any(c => c.Id == id);

    private static BalanceColumn Column(string id) =>
        AllColumns.FirstOrDefault(c => c.Id == id);

    private static int Compare(BalanceRow a, BalanceRow b, string column) => column switch
    {
        "name" => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        "id" => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase),
        "class" => string.Compare(a.Class, b.Class, StringComparison.OrdinalIgnoreCase),
        "army" => a.ArmyPower.CompareTo(b.ArmyPower),
        "hp" => a.Health.CompareTo(b.Health),
        "cost" => a.Cost.CompareTo(b.Cost),
        "speed" => a.Speed.CompareTo(b.Speed),
        "vision" => a.Vision.CompareTo(b.Vision),
        "size" => string.Compare(a.Size, b.Size, StringComparison.Ordinal),
        "dps" => a.Dps.CompareTo(b.Dps),
        "range" => a.WeaponRange.CompareTo(b.WeaponRange),
        "hp_per_metal" => a.HealthPerMetal.CompareTo(b.HealthPerMetal),
        "energy_net" => a.EnergyNet.CompareTo(b.EnergyNet),
        "metal_net" => a.MetalNet.CompareTo(b.MetalNet),
        "expansion" => a.ExpansionPower.CompareTo(b.ExpansionPower),
        "build_power" => a.BuildPower.CompareTo(b.BuildPower),
        _ => 0,
    };

    private static string F(float value) =>
        value == 0f ? "—" : value.ToString("0.##", CultureInfo.InvariantCulture);

    private readonly struct BalanceColumn
    {
        public string Id { get; }
        public string Title { get; }
        public string Section { get; }
        public int MinWidth { get; }
        public bool Expand { get; }

        public BalanceColumn(string id, string title, string section, int minWidth, bool expand)
        {
            Id = id;
            Title = title;
            Section = section;
            MinWidth = minWidth;
            Expand = expand;
        }
    }

    private readonly struct BalanceRow
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Class { get; init; }
        public bool IsStructure { get; init; }
        public bool IsEnemy { get; init; }
        public bool IsAttacker { get; init; }
        public bool IsBuilder { get; init; }
        public UnitDefinition Definition { get; init; }
        public float Health { get; init; }
        public float Cost { get; init; }
        public float Speed { get; init; }
        public float Vision { get; init; }
        public string Size { get; init; }
        public float Dps { get; init; }
        public float WeaponRange { get; init; }
        public float HealthPerMetal { get; init; }
        public float EnergyNet { get; init; }
        public float MetalNet { get; init; }
        public float ArmyPower { get; init; }
        public float ExpansionPower { get; init; }
        public float BuildPower { get; init; }

        public static BalanceRow From(UnitDefinition def)
        {
            float dps = 0f;
            float range = 0f;
            if (def.Weapon != null && def.Weapon.FireInterval > 0f)
            {
                dps = def.Weapon.Damage / def.Weapon.FireInterval;
                range = def.Weapon.Range;
            }

            float cost = def.Assembly?.CostMetal ?? 0f;
            float buildPower = def.BuildTool?.Power ?? def.Plant?.BuildPower ?? 0f;
            float energyDrain = (def.Conversion?.EnergyDrain ?? 0f)
                                + (def.BuildTool?.EnergyDrain ?? 0f)
                                + ((def.Plant?.BuildPower ?? 0f) * (def.Plant?.EnergyPerPower ?? 0f));

            return new BalanceRow
            {
                Id = def.Id,
                Name = string.IsNullOrEmpty(def.DisplayName) ? def.Id : def.DisplayName,
                Class = def.Class.ToString(),
                IsStructure = def.IsStructure,
                IsEnemy = def.Class == UnitClass.Enemy,
                IsAttacker = def.Weapon != null,
                IsBuilder = def.CanBuild,
                Definition = def,
                Health = def.MaxHealth,
                Cost = cost,
                Speed = def.Speed,
                Vision = def.VisionRange,
                Size = def.IsStructure ? $"{def.Width}×{def.Height}" : $"r={def.Radius:0.##}",
                Dps = dps,
                WeaponRange = range,
                HealthPerMetal = def.HealthPerMetal,
                EnergyNet = def.EnergyProduction - energyDrain,
                MetalNet = def.MetalProduction + (def.Conversion?.MetalOutput ?? 0f),
                ArmyPower = def.ArmyPower,
                ExpansionPower = def.ExpansionPower,
                BuildPower = buildPower,
            };
        }
    }
}

public sealed class ContentEditorBalanceUiState
{
    public string ViewId { get; set; }
    public string[] VisibleColumnIds { get; set; }
    public string SortColumnId { get; set; }
    public bool SortAsc { get; set; }
    public List<ContentEditorSavedView> SavedViews { get; set; } = new();
}

/// <summary>
/// Tree без штатного ColumnsResizable: ширину колонки меняет перетаскивание
/// разделителя в полосе заголовков.
/// </summary>
[Tool]
public partial class ResizableBalanceTree : Tree
{
    private const float SeparatorHit = 6f;
    private int _dragColumn = -1;
    private float _dragStartX;
    private int _dragStartWidth;

    public event Action<ResizableBalanceTree> ColumnWidthsChanged;

    public bool IsResizingColumns => _dragColumn >= 0;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                int column = SeparatorAt(mouse.Position);
                if (column >= 0)
                {
                    _dragColumn = column;
                    _dragStartX = mouse.Position.X;
                    _dragStartWidth = GetColumnWidth(column);
                    MouseDefaultCursorShape = CursorShape.Hsize;
                    AcceptEvent();
                    return;
                }
            }
            else if (_dragColumn >= 0)
            {
                _dragColumn = -1;
                MouseDefaultCursorShape = CursorShape.Arrow;
                ColumnWidthsChanged?.Invoke(this);
                AcceptEvent();
                return;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_dragColumn >= 0)
            {
                int width = Mathf.Max(36, _dragStartWidth + (int)(motion.Position.X - _dragStartX));
                SetColumnExpand(_dragColumn, false);
                SetColumnCustomMinimumWidth(_dragColumn, width);
                AcceptEvent();
                return;
            }

            MouseDefaultCursorShape = SeparatorAt(motion.Position) >= 0
                ? CursorShape.Hsize
                : CursorShape.Arrow;
        }

        base._GuiInput(@event);
    }

    private int SeparatorAt(Vector2 position)
    {
        float titleHeight = GetThemeConstant("title_button_height");
        if (titleHeight <= 0f)
            titleHeight = 24f;
        if (position.Y < 0f || position.Y > titleHeight)
            return -1;

        float x = 0f;
        for (int i = 0; i < Columns; i++)
        {
            x += GetColumnWidth(i);
            if (Mathf.Abs(position.X - x) <= SeparatorHit)
                return i;
        }

        return -1;
    }
}
