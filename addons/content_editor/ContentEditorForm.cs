using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

/// <summary>
/// Форма свойств активной вкладки.
///
/// ПОЛЯ СТРОЯТСЯ ИЗ ContentSchema, а не вручную: новый ключ в схеме появляется здесь сам.
/// Значения читаются из Catalog после CompileWithOverrides (итог с учётом черновика).
/// Provenance считает Store по наличию ключа в DraftText и в цепочке base.
///
/// ПРАВКА. Любое изменение виджета сразу пишет ключ в DraftText через SetField.
/// Если новое значение совпало с унаследованным, локальный ключ удаляется — файл
/// не раздувается копиями base. Кнопка «Сбросить» делает то же явно.
///
/// _rebuildGuard защищает от рекурсии: Refresh пересоздаёт виджеты, а их сигналы
/// при выставлении ButtonPressed/Selected не должны снова вызывать SetField.
/// </summary>
[Tool]
public partial class ContentEditorForm : ScrollContainer
{
    private ContentEditorStore _store;
    private LineEdit _fieldFilter;
    private VBoxContainer _root;
    private bool _rebuildGuard;
    private string _fieldFilterText = "";
    private readonly HashSet<string> _expandedSections = new()
    {
        "",
        "body",
        "movement",
    };

    public event Action<string> VarsPanelRequested;
    public event Action GraphPanelRequested;
    public event Action FieldFilterChanged;

    /// <summary>Текст фильтра полей. Не сбрасывается при смене сущности и Refresh.</summary>
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
        ClearChildren(_root);

        var session = _store?.ActiveSession;
        if (session == null)
        {
            _root.AddChild(new Label { Text = "Open an entity from the catalog" });
            return;
        }

        // Имя сущности несёт заголовок панели, внутри которой стоит форма; повторять
        // его строкой формы значит занимать место одним и тем же.
        var path = new Label
        {
            Text = session.Path,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        _root.AddChild(path);

        var contextActions = new HBoxContainer();
        // У волны нет силуэта, и на измерительном поле она не появляется, поэтому
        // переключатель показа для неё бессмыслен.
        if (session.Kind != ContentEntityKind.Wave)
        {
            var showToggle = new CheckBox
            {
                Text = "Show on field",
                ButtonPressed = session.ShowOnField,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            showToggle.Toggled += on =>
            {
                session.ShowOnField = on;
                _store.NotifyChanged();
            };
            contextActions.AddChild(showToggle);
        }

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            var graph = new Button
            {
                Text = "Relations",
                TooltipText = "Show inheritance, vars files and tools",
                Icon = ContentEditorTheme.IconAny("GraphEdit", "Groups", "Node"),
            };
            graph.Pressed += () => GraphPanelRequested?.Invoke();
            contextActions.AddChild(graph);
        }
        _root.AddChild(contextActions);

        var fields = ContentSchema.FieldsFor(session.Kind);

        UnitDefinition unit = session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building
            ? _store.PreviewUnit(session.Id)
            : null;

        var visibleFields = fields
            .Where(field => field.Key is not ("id" or "kind"))
            .Where(field => unit == null || field.VisibleForUnit == null || field.VisibleForUnit(unit))
            .Where(MatchesFieldFilter)
            .GroupBy(field => field.RootOnly ? "" : field.Section ?? "");

        foreach (var section in visibleFields)
        {
            string sectionKey = section.Key;
            bool expanded = _expandedSections.Contains(sectionKey);
            Texture2D arrow = expanded
                ? ContentEditorTheme.Icon("GuiTreeArrowDown")
                : ContentEditorTheme.Icon("GuiTreeArrowRight");
            var heading = new Button
            {
                Text = (arrow == null ? $"{(expanded ? "▼" : "▶")}  " : "")
                       + (string.IsNullOrEmpty(sectionKey) ? "General" : SectionTitle(sectionKey)),
                Icon = arrow,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                TooltipText = expanded ? "Collapse section" : "Expand section",
            };
            heading.AddThemeColorOverride("font_color", new Color(0.72f, 0.84f, 1f));
            heading.Pressed += () =>
            {
                if (!_expandedSections.Add(sectionKey))
                    _expandedSections.Remove(sectionKey);
                Refresh();
            };
            _root.AddChild(heading);

            if (!expanded)
                continue;

            var sectionBody = new VBoxContainer();
            sectionBody.AddThemeConstantOverride("separation", 8);
            foreach (var field in section)
                sectionBody.AddChild(BuildRow(session, field));
            _root.AddChild(sectionBody);
        }

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
            AddLinkedTools(session, unit);
    }

    private void AddLinkedTools(OpenEntitySession session, UnitDefinition unit)
    {
        if (unit?.ToolIds == null || unit.ToolIds.Length == 0)
            return;

        _root.AddChild(new Label { Text = "Linked tools" });
        foreach (string toolId in unit.ToolIds)
        {
            var button = new Button
            {
                Text = $"Open {toolId}",
                Icon = ContentEditorTheme.IconAny("ExternalLink", "Forward"),
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"Open the tool {toolId} in its own tab",
            };
            string id = toolId;
            button.Pressed += () => _store.Open(id);
            _root.AddChild(button);
        }
    }

    private Control BuildRow(OpenEntitySession session, ContentFieldSpec field)
    {
        var sourceInfo = _store.FieldSource(session, field);
        var provenance = sourceInfo.Provenance;
        bool local = _store.HasLocalValue(session, field);
        var sourceAndActions = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(176, 0),
        };

        var source = new Label
        {
            Text = ProvenanceLabel(sourceInfo),
            TooltipText = ProvenanceTooltip(sourceInfo),
            Modulate = ProvenanceColor(provenance),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        source.AddThemeFontSizeOverride("font_size", 11);
        sourceAndActions.AddChild(source);

        // Поле всегда редактируемо: первое изменение унаследованного значения создаёт
        // локальный override. Отдельная широкая кнопка «Переопределить» занимала почти
        // половину узкой формы и не добавляла действия.
        if (local)
        {
            Texture2D resetIcon = ContentEditorTheme.IconAny("Reload", "Undo");
            var reset = new Button
            {
                Text = resetIcon == null ? "↶" : "",
                Icon = resetIcon,
                Flat = true,
                CustomMinimumSize = new Vector2(24, 24),
                TooltipText = "Remove the local value and restore inheritance",
            };
            reset.Pressed += () =>
            {
                _store.SetField(session, field, null, clear: true);
            };
            sourceAndActions.AddChild(reset);
        }
        if ((provenance is FieldProvenance.Base or FieldProvenance.Vars)
            && !string.IsNullOrEmpty(sourceInfo.Path))
        {
            string sourcePath = sourceInfo.Path;
            Texture2D openIcon = ContentEditorTheme.IconAny("ExternalLink", "ActionCopy", "Forward");
            var openSource = new Button
            {
                Text = openIcon == null ? "↗" : "",
                Icon = openIcon,
                Flat = true,
                CustomMinimumSize = new Vector2(24, 24),
                TooltipText = $"Open source file\n{sourcePath}",
            };
            openSource.Pressed += () => OpenSource(sourcePath);
            sourceAndActions.AddChild(openSource);
        }

        object value = _store.EffectiveValue(session, field);
        Control editor = BuildEditor(session, field, value);
        string tooltip = $"{field.Section ?? "root"}.{field.Key}";
        if (!string.IsNullOrEmpty(field.Hint))
            tooltip += "\n" + field.Hint;

        var row = new EditorPropertyRow();
        row.Configure(
            field.Label,
            tooltip,
            editor,
            sourceAndActions,
            EditorFieldEditor.BelowTitle(field));
        return row;
    }

    private void OpenSource(string path)
    {
        // Определения с id открываются второй вкладкой редактора контента. Vars-файлы
        // не являются сущностями каталога, поэтому для них используется системный
        // редактор, связанный с расширением TOML.
        if (TomlResolver.IsVars(path)
            && (_store.ActiveSession?.Kind is ContentEntityKind.Unit or ContentEntityKind.Building))
        {
            VarsPanelRequested?.Invoke(path);
            return;
        }

        if (_store.OpenPath(path))
            return;

        OS.ShellOpen(ProjectSettings.GlobalizePath(path));
    }

    private Control BuildEditor(OpenEntitySession session, ContentFieldSpec field, object value) =>
        EditorFieldEditor.Build(field, value, editable: true, committed =>
        {
            if (_rebuildGuard)
                return;

            _rebuildGuard = true;
            _store.SetField(session, field, committed, clear: false);
            _rebuildGuard = false;
        });

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

        var outer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        outer.AddThemeConstantOverride("separation", 6);
        AddChild(outer);

        _fieldFilter = new LineEdit
        {
            PlaceholderText = "Filter fields…",
            Text = _fieldFilterText,
            RightIcon = ContentEditorTheme.Icon("Search"),
        };
        _fieldFilter.TextChanged += text =>
        {
            _fieldFilterText = text ?? "";
            Refresh();
            FieldFilterChanged?.Invoke();
        };
        outer.AddChild(_fieldFilter);

        _root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _root.AddThemeConstantOverride("separation", 6);
        outer.AddChild(_root);
    }

    private static void ClearChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            // RemoveChild исключает старый Control из раскладки немедленно. Один QueueFree
            // оставлял старую и новую формы одновременно до конца кадра.
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string ProvenanceLabel(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "in this file",
        FieldProvenance.Base => $"inherited · {SourceFileName(source.Path)}",
        FieldProvenance.Vars => $"from vars · {SourceFileName(source.Path)}",
        FieldProvenance.Default => "default",
        _ => "none",
    };

    private static string SourceFileName(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "unknown file";
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string ProvenanceTooltip(FieldSourceInfo source) => source.Provenance switch
    {
        FieldProvenance.Local => "Value is written in the current file",
        FieldProvenance.Base =>
            $"Value comes from the parent definition\n{source.Path}",
        FieldProvenance.Vars =>
            $"Value comes from a shared vars file\n{source.Path}",
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
