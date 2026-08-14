using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// Чтение и запись снимка рабочего пространства.
///
/// ПОЧЕМУ ConfigFile, А НЕ JSON. <c>System.Text.Json</c> кэширует сведения о типах и
/// является известной причиной сообщения «Failed to unload assemblies» в tool-коде Godot.
/// <see cref="ConfigFile"/> ничего не кэширует между сборками.
///
/// ПОЧЕМУ ЗАПИСЬ ЧЕРЕЗ ВРЕМЕННЫЙ ФАЙЛ. Godot может выгрузить сборку в любой момент, в том
/// числе посреди записи. Запись во временный файл с последующим перемещением оставляет на
/// диске либо прежний снимок целиком, либо новый целиком, но никогда не половину.
/// </summary>
public static class ContentEditorWorkspaceFile
{
    /// <summary>Снимок лежит в <c>.godot/</c>: это состояние рабочего места, а не содержимое игры.</summary>
    public const string Path = "res://.godot/content_editor_workspace.cfg";

    /// <summary>
    /// Прочитать снимок. Возвращает <c>null</c>, если файла нет или он испорчен: редактор
    /// в этом случае открывается с умолчаниями, а не отказывается работать.
    /// </summary>
    public static ContentEditorWorkspace Load()
    {
        try
        {
            string absolute = ProjectSettings.GlobalizePath(Path);
            if (!File.Exists(absolute))
                return null;

            var config = new ConfigFile();
            if (config.Load(absolute) != Error.Ok)
                return null;

            var workspace = new ContentEditorWorkspace
            {
                ModeTab = config.GetValue("workspace", "mode_tab", 0).AsInt32(),
                ActiveEntityId = config.GetValue("workspace", "active_id", "").AsString(),
                ActiveWaveId = config.GetValue("workspace", "active_wave_id", "").AsString(),
            };

            ReadSessions(config, workspace);
            ReadEntities(config, workspace.Entities);
            ReadBalance(config, workspace.Balance);
            ReadWaves(config, workspace.Waves);
            ReadGlobals(config, workspace.Globals);
            return workspace;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] tab state was not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Записать снимок. Ошибка записи не должна прерывать работу редактора.</summary>
    public static void Save(ContentEditorWorkspace workspace)
    {
        if (workspace == null)
            return;

        try
        {
            string absolute = ProjectSettings.GlobalizePath(Path);
            string temp = absolute + ".tmp";

            var config = new ConfigFile();
            config.SetValue("workspace", "mode_tab", workspace.ModeTab);
            config.SetValue("workspace", "active_id", workspace.ActiveEntityId ?? "");
            config.SetValue("workspace", "active_wave_id", workspace.ActiveWaveId ?? "");

            WriteSessions(config, workspace);
            WriteEntities(config, workspace.Entities);
            WriteBalance(config, workspace.Balance);
            WriteWaves(config, workspace.Waves);
            WriteGlobals(config, workspace.Globals);

            Error saved = config.Save(temp);
            if (saved != Error.Ok)
                throw new IOException($"ConfigFile.Save: {saved}");

            File.Move(temp, absolute, overwrite: true);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Content editor] tab state was not saved: {ex.Message}");
        }
    }

    // ── Вкладки ───────────────────────────────────────────────────────────────────

    private static void ReadSessions(ConfigFile config, ContentEditorWorkspace workspace)
    {
        int count = config.GetValue("workspace", "session_count", 0).AsInt32();
        for (int i = 0; i < count; i++)
        {
            string section = $"session_{i}";
            // Метка времени хранится строкой: ulong не помещается в int64 ConfigFile
            // без потери старшего разряда.
            ulong.TryParse(config.GetValue(section, "loaded_stamp", "0").AsString(), out ulong stamp);
            workspace.Sessions.Add(new ContentEditorSessionSnapshot
            {
                Id = config.GetValue(section, "id", "").AsString(),
                DraftText = config.GetValue(section, "draft_text", "").AsString(),
                LoadedStamp = stamp,
                Dirty = config.GetValue(section, "dirty", false).AsBool(),
                ShowOnField = config.GetValue(section, "show_on_field", true).AsBool(),
                FieldX = config.GetValue(section, "field_x", 0f).AsSingle(),
                FieldY = config.GetValue(section, "field_y", 0f).AsSingle(),
            });
        }
    }

    private static void WriteSessions(ConfigFile config, ContentEditorWorkspace workspace)
    {
        config.SetValue("workspace", "session_count", workspace.Sessions.Count);
        for (int i = 0; i < workspace.Sessions.Count; i++)
        {
            var session = workspace.Sessions[i];
            string section = $"session_{i}";
            config.SetValue(section, "id", session.Id ?? "");
            config.SetValue(section, "draft_text", session.DraftText ?? "");
            config.SetValue(section, "loaded_stamp", session.LoadedStamp.ToString());
            config.SetValue(section, "dirty", session.Dirty);
            config.SetValue(section, "show_on_field", session.ShowOnField);
            config.SetValue(section, "field_x", session.FieldX);
            config.SetValue(section, "field_y", session.FieldY);
        }
    }

    // ── Режим сущностей ───────────────────────────────────────────────────────────

    private static void ReadEntities(ConfigFile config, ContentEditorEntitiesState state)
    {
        state.CatalogTab = config.GetValue("entities", "catalog_tab", 0).AsInt32();
        state.CatalogSearch = config.GetValue("entities", "catalog_search", "").AsString();
        state.FormFieldFilter = config.GetValue("entities", "field_filter", "").AsString();
        state.CatalogSplit = config.GetValue("entities", "catalog_split", 0).AsInt32();
        state.WorkspaceSplit = config.GetValue("entities", "workspace_split", 0).AsInt32();
        state.CenterSplit = config.GetValue("entities", "center_split", 0).AsInt32();
        state.HasExpandedSections = config.HasSectionKey("entities", "expanded_sections");
        state.ExpandedSections = config
            .GetValue("entities", "expanded_sections", Array.Empty<string>())
            .AsStringArray();
    }

    private static void WriteEntities(ConfigFile config, ContentEditorEntitiesState state)
    {
        config.SetValue("entities", "catalog_tab", state.CatalogTab);
        config.SetValue("entities", "catalog_search", state.CatalogSearch ?? "");
        config.SetValue("entities", "field_filter", state.FormFieldFilter ?? "");
        config.SetValue("entities", "catalog_split", state.CatalogSplit);
        config.SetValue("entities", "workspace_split", state.WorkspaceSplit);
        config.SetValue("entities", "center_split", state.CenterSplit);
        if (state.HasExpandedSections)
            config.SetValue("entities", "expanded_sections", state.ExpandedSections ?? Array.Empty<string>());
    }

    // ── Режим баланса ─────────────────────────────────────────────────────────────

    private static void ReadBalance(ConfigFile config, ContentEditorBalanceState state)
    {
        state.ViewId = config.GetValue("balance", "view_id", "").AsString();
        state.Split = config.GetValue("balance", "split", 0).AsInt32();
        state.Current = ReadView(config, "balance");

        int count = config.GetValue("balance", "saved_count", 0).AsInt32();
        for (int i = 0; i < count; i++)
        {
            string section = $"balance_view_{i}";
            string name = config.GetValue(section, "name", "").AsString();
            if (string.IsNullOrEmpty(name))
                continue;

            state.SavedViews.Add(new ContentEditorSavedView
            {
                Name = name,
                State = ReadView(config, section),
            });
        }
    }

    private static void WriteBalance(ConfigFile config, ContentEditorBalanceState state)
    {
        config.SetValue("balance", "view_id", state.ViewId ?? "");
        config.SetValue("balance", "split", state.Split);
        WriteView(config, "balance", state.Current);

        var views = state.SavedViews ?? new List<ContentEditorSavedView>();
        config.SetValue("balance", "saved_count", views.Count);
        for (int i = 0; i < views.Count; i++)
        {
            string section = $"balance_view_{i}";
            config.SetValue(section, "name", views[i].Name ?? "");
            WriteView(config, section, views[i].State);
        }
    }

    /// <summary>
    /// Чтение состояния представления. Одно и то же и для действующего набора, и для
    /// сохранённого: иначе сохранённое представление отставало бы от действующего
    /// на каждое новое запоминаемое свойство.
    /// </summary>
    private static ContentEditorBalanceViewState ReadView(ConfigFile config, string section) => new()
    {
        ColumnIds = config.GetValue(section, "columns", Array.Empty<string>()).AsStringArray(),
        ColumnWidths = config.GetValue(section, "column_widths", Array.Empty<int>()).AsInt32Array(),
        SortColumnId = config.GetValue(section, "sort_column", "").AsString(),
        SortAsc = config.GetValue(section, "sort_asc", false).AsBool(),
        Search = config.GetValue(section, "search", "").AsString(),
        TypeFilter = config.GetValue(section, "type_filter", 0).AsInt32(),
        Attackers = config.GetValue(section, "attackers", false).AsBool(),
        Builders = config.GetValue(section, "builders", false).AsBool(),
    };

    private static void WriteView(ConfigFile config, string section, ContentEditorBalanceViewState view)
    {
        view ??= new ContentEditorBalanceViewState();
        config.SetValue(section, "columns", view.ColumnIds ?? Array.Empty<string>());
        config.SetValue(section, "column_widths", view.ColumnWidths ?? Array.Empty<int>());
        config.SetValue(section, "sort_column", view.SortColumnId ?? "");
        config.SetValue(section, "sort_asc", view.SortAsc);
        config.SetValue(section, "search", view.Search ?? "");
        config.SetValue(section, "type_filter", view.TypeFilter);
        config.SetValue(section, "attackers", view.Attackers);
        config.SetValue(section, "builders", view.Builders);
    }

    // ── Режим волн ────────────────────────────────────────────────────────────────

    private static void ReadWaves(ConfigFile config, ContentEditorWavesState state)
    {
        state.SelectedId = config.GetValue("waves", "selected_id", "").AsString();
        state.Search = config.GetValue("waves", "search", "").AsString();
        state.LogScale = config.GetValue("waves", "log_scale", false).AsBool();
        state.Probe = config.GetValue("waves", "probe", 0f).AsSingle();
        state.Split = config.GetValue("waves", "split", 0).AsInt32();
        state.ShapeSplit = config.GetValue("waves", "shape_split", 0).AsInt32();
        state.ShapeShowUnits = config.GetValue("waves", "shape_units", true).AsBool();
        state.ShapeShowWorld = config.GetValue("waves", "shape_world", true).AsBool();
        state.ShapeDirectionDegrees = config.GetValue("waves", "shape_direction", -90f).AsSingle();
        state.ShapeSeed = config.GetValue("waves", "shape_seed", 1).AsInt32();
        state.HasExpandedSections = config.HasSectionKey("waves", "expanded_sections");
        state.ExpandedSections = config
            .GetValue("waves", "expanded_sections", Array.Empty<string>())
            .AsStringArray();
    }

    private static void WriteWaves(ConfigFile config, ContentEditorWavesState state)
    {
        config.SetValue("waves", "selected_id", state.SelectedId ?? "");
        config.SetValue("waves", "search", state.Search ?? "");
        config.SetValue("waves", "log_scale", state.LogScale);
        config.SetValue("waves", "probe", state.Probe);
        config.SetValue("waves", "split", state.Split);
        config.SetValue("waves", "shape_split", state.ShapeSplit);
        config.SetValue("waves", "shape_units", state.ShapeShowUnits);
        config.SetValue("waves", "shape_world", state.ShapeShowWorld);
        config.SetValue("waves", "shape_direction", state.ShapeDirectionDegrees);
        config.SetValue("waves", "shape_seed", state.ShapeSeed);
        if (state.HasExpandedSections)
            config.SetValue("waves", "expanded_sections", state.ExpandedSections ?? Array.Empty<string>());
    }

    // ── Режим глобальных параметров ───────────────────────────────────────────────

    private static void ReadGlobals(ConfigFile config, ContentEditorGlobalsState state)
    {
        state.Collapsed = config.GetValue("globals", "collapsed", Array.Empty<string>()).AsStringArray();

        int count = config.GetValue("globals", "draft_count", 0).AsInt32();
        for (int i = 0; i < count; i++)
        {
            string section = $"globals_draft_{i}";
            string id = config.GetValue(section, "id", "").AsString();
            if (string.IsNullOrEmpty(id))
                continue;

            state.Drafts.Add(new ContentEditorTuningDraftState
            {
                Id = id,
                Edits = config.GetValue(section, "edits", Array.Empty<string>()).AsStringArray(),
            });
        }
    }

    private static void WriteGlobals(ConfigFile config, ContentEditorGlobalsState state)
    {
        config.SetValue("globals", "collapsed", state.Collapsed ?? Array.Empty<string>());

        var drafts = state.Drafts ?? new List<ContentEditorTuningDraftState>();
        config.SetValue("globals", "draft_count", drafts.Count);
        for (int i = 0; i < drafts.Count; i++)
        {
            string section = $"globals_draft_{i}";
            config.SetValue(section, "id", drafts[i].Id ?? "");
            config.SetValue(section, "edits", drafts[i].Edits ?? Array.Empty<string>());
        }
    }
}
