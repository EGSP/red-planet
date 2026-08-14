using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Форма свойств активной сущности режима «Entities».
///
/// ПОЛЯ СТРОЯТСЯ ИЗ ContentSchema, а не вручную: новый ключ в схеме появляется здесь сам.
/// Значения читаются из Catalog после CompileWithOverrides, то есть с учётом черновика;
/// источник значения считает Store по наличию ключа в черновике и в цепочке base.
///
/// ПРАВКА. Любое изменение виджета сразу пишет ключ в черновик. Если новое значение
/// совпало с унаследованным, локальный ключ удаляется: файл не раздувается копиями base.
/// Кнопка возврата делает то же явно.
///
/// _rebuildGuard защищает от рекурсии: обновление пересоздаёт виджеты, а их сигналы при
/// выставлении ButtonPressed или Selected не должны снова записывать значение.
/// </summary>
[Tool]
public partial class ContentEditorForm : ScrollContainer
{
    private ContentEditorStore _store;
    private LineEdit _fieldFilter;
    private VBoxContainer _root;
    private bool _rebuildGuard;
    private string _fieldFilterText = "";

    /// <summary>Раскрытые секции. Умолчание показывает то, что правят чаще всего.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal)
    {
        "",
        "body",
        "movement",
    };

    /// <summary>Открыть страницу значений файла переменных.</summary>
    public event Action<string> VarsPanelRequested;

    /// <summary>Открыть страницу графа зависимостей.</summary>
    public event Action GraphPanelRequested;

    /// <summary>Настройка формы изменилась: отбор полей либо состав раскрытых секций.</summary>
    public event Action UiStateChanged;

    /// <summary>Текст отбора полей. Не сбрасывается при смене сущности и обновлении.</summary>
    public string FieldFilter
    {
        get => _fieldFilterText;
        set
        {
            _fieldFilterText = value ?? "";
            if (IsInstanceValid(_fieldFilter) && _fieldFilter.Text != _fieldFilterText)
                _fieldFilter.Text = _fieldFilterText;
        }
    }

    /// <summary>Раскрытые секции для снимка рабочего пространства.</summary>
    public string[] ExpandedSections
    {
        get => _expanded.ToArray();
        set
        {
            _expanded.Clear();
            foreach (string section in value ?? Array.Empty<string>())
                _expanded.Add(section);
        }
    }

    public void Bind(ContentEditorStore store)
    {
        _store = store;
        EnsureRoot();
        Refresh();
    }

    public void Refresh()
    {
        if (_store == null)
            return;

        EnsureRoot();
        EditorControls.ClearChildren(_root);

        var session = _store.ActiveSession;
        if (session == null)
        {
            _root.AddChild(new Label { Text = "Open an entity from the catalog" });
            return;
        }

        // Имя сущности несёт заголовок панели, внутри которой стоит форма; повторять его
        // строкой формы значит занимать место одним и тем же.
        _root.AddChild(new Label
        {
            Text = session.Path,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        });

        _root.AddChild(BuildContextActions(session));

        UnitDefinition unit = session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? _store.PreviewUnit(session.Id)
            : null;

        var sections = ContentSchema.FieldsFor(session.Kind)
            .Where(field => field.Key is not ("id" or "kind"))
            .Where(field => unit == null || field.VisibleForUnit == null || field.VisibleForUnit(unit))
            .Where(MatchesFieldFilter)
            .GroupBy(field => field.RootOnly ? "" : field.Section ?? "");

        foreach (var section in sections)
        {
            string key = section.Key;
            bool expanded = _expanded.Contains(key);
            _root.AddChild(EditorControls.SectionHeading(
                string.IsNullOrEmpty(key) ? "General" : SectionTitle(key),
                expanded,
                () => ToggleSection(key)));

            if (!expanded)
                continue;

            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 8);
            foreach (var field in section)
                body.AddChild(BuildRow(session, field));
            _root.AddChild(body);
        }

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
            AddLinkedTools(unit);
    }

    private void ToggleSection(string key)
    {
        if (!_expanded.Add(key))
            _expanded.Remove(key);

        Refresh();
        UiStateChanged?.Invoke();
    }

    /// <summary>Действия над сущностью целиком: показ на поле и граф зависимостей.</summary>
    private Control BuildContextActions(OpenEntitySession session)
    {
        var actions = new HBoxContainer();

        var showToggle = new CheckBox
        {
            Text = "Show on field",
            ButtonPressed = session.ShowOnField,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        showToggle.Toggled += on => EditorControls.Run("toggle field visibility", () =>
        {
            session.ShowOnField = on;
            _store.NotifyChanged();
        });
        actions.AddChild(showToggle);

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            var graph = new Button
            {
                Text = "Relations",
                TooltipText = "Show inheritance, vars files and tools",
                Icon = ContentEditorTheme.IconAny("GraphEdit", "Groups", "Node"),
            };
            graph.Pressed += () => EditorControls.Run("open relations",
                () => GraphPanelRequested?.Invoke());
            actions.AddChild(graph);
        }

        return actions;
    }

    private void AddLinkedTools(UnitDefinition unit)
    {
        if (unit?.ToolIds == null || unit.ToolIds.Length == 0)
            return;

        _root.AddChild(new Label { Text = "Linked tools" });
        foreach (string toolId in unit.ToolIds)
        {
            string id = toolId;
            var button = new Button
            {
                Text = $"Open {toolId}",
                Icon = ContentEditorTheme.IconAny("ExternalLink", "Forward"),
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"Open the tool {toolId} in its own tab",
            };
            button.Pressed += () => EditorControls.Run("open tool", () => _store.Open(id));
            _root.AddChild(button);
        }
    }

    private Control BuildRow(OpenEntitySession session, ContentFieldSpec field)
    {
        var source = _store.FieldSource(session, field);
        bool local = _store.HasLocalValue(session, field);
        bool hasSourceFile = source.Provenance is FieldProvenance.Base or FieldProvenance.Vars
                             && !string.IsNullOrEmpty(source.Path);

        return EditorFieldRow.Build(new EditorFieldRowSpec
        {
            Field = field,
            Value = _store.EffectiveValue(session, field),
            SourceLabel = ProvenanceLabel(source),
            SourceTooltip = ProvenanceTooltip(source),
            SourceColor = ProvenanceColor(source.Provenance),
            // Поле всегда редактируемо: первое изменение унаследованного значения создаёт
            // локальный ключ. Отдельная кнопка «Переопределить» занимала половину строки
            // и не добавляла действия.
            CanReset = local,
            ResetTooltip = "Remove the local value and restore inheritance",
            Commit = value => Commit(session, field, value),
            Reset = local ? () => _store.SetField(session, field, null, clear: true) : null,
            OpenSource = hasSourceFile ? () => OpenSource(source.Path) : null,
            OpenSourceTooltip = hasSourceFile ? $"Open source file\n{source.Path}" : "",
        });
    }

    private void Commit(OpenEntitySession session, ContentFieldSpec field, object value)
    {
        if (_rebuildGuard)
            return;

        _rebuildGuard = true;
        try
        {
            _store.SetField(session, field, value, clear: false);
        }
        finally
        {
            _rebuildGuard = false;
        }
    }

    /// <summary>
    /// Определения с <c>id</c> открываются второй вкладкой редактора. Файлы переменных
    /// сущностями каталога не являются, поэтому показываются контекстной страницей,
    /// а при невозможности — системным редактором TOML.
    /// </summary>
    private void OpenSource(string path)
    {
        if (TomlResolver.IsVars(path)
            && _store.ActiveSession?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            VarsPanelRequested?.Invoke(path);
            return;
        }

        if (_store.OpenPath(path))
            return;

        OS.ShellOpen(ProjectSettings.GlobalizePath(path));
    }

    private bool MatchesFieldFilter(ContentFieldSpec field)
    {
        string query = _fieldFilterText.Trim();
        if (query.Length == 0)
            return true;

        return field.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
               || field.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
               || (field.Section ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;

        var outer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        outer.AddThemeConstantOverride("separation", 6);
        AddChild(outer);

        _fieldFilter = new LineEdit
        {
            PlaceholderText = "Filter fields…",
            Text = _fieldFilterText,
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _fieldFilter.TextChanged += text => EditorControls.Run("filter fields", () =>
        {
            _fieldFilterText = text ?? "";
            Refresh();
            UiStateChanged?.Invoke();
        });
        outer.AddChild(_fieldFilter);

        _root = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _root.AddThemeConstantOverride("separation", 6);
        outer.AddChild(_root);
    }

    private static string ProvenanceLabel(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "in this file",
        FieldProvenance.Base => $"inherited · {TomlText.FileName(source.Path)}",
        FieldProvenance.Vars => $"from vars · {TomlText.FileName(source.Path)}",
        FieldProvenance.Default => "default",
        _ => "none",
    };

    private static string ProvenanceTooltip(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "Value is written in the current file",
        FieldProvenance.Base => $"Value comes from the parent definition\n{source.Path}",
        FieldProvenance.Vars => $"Value comes from a shared vars file\n{source.Path}",
        FieldProvenance.Default => "Value is the code default",
        _ => "Value is missing",
    };

    private static Color ProvenanceColor(FieldProvenance provenance) => provenance switch
    {
        FieldProvenance.Local => new Color(0.55f, 0.9f, 0.65f),
        FieldProvenance.Base => new Color(0.65f, 0.75f, 0.95f),
        FieldProvenance.Vars => new Color(0.85f, 0.7f, 1f),
        _ => new Color(1f, 1f, 1f, 0.5f),
    };

    /// <summary>Название секции для человека: ключ TOML в подписи выглядит техническим.</summary>
    private static string SectionTitle(string section) => section switch
    {
        "body" => "Body",
        "movement" => "Movement",
        "footprint" => "Footprint",
        "assembly" => "Assembly",
        "conversion" => "Conversion",
        "production" => "Production",
        "storage" => "Storage",
        "terror" => "Terror",
        "battle" => "Battle",
        "plant" => "Plant",
        "orders" => "Orders",
        "spawn" => "Spawn shape",
        _ => section,
    };
}
