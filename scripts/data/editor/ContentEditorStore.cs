using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Tomlyn;
using Tomlyn.Model;

/// <summary>
/// Откуда взялось итоговое значение поля после Resolve.
/// Форма показывает эту метку рядом с подписью, чтобы было видно, правка ли это
/// текущего файла или унаследованное число.
/// </summary>
public enum FieldProvenance
{
    /// <summary>Ключ записан в черновике текущего файла.</summary>
    Local,

    /// <summary>Ключа в текущем файле нет; значение пришло из цепочки <c>base</c>.</summary>
    Base,

    /// <summary>Значение задано ссылкой на <c>*.vars.toml</c> либо пришло из vars.</summary>
    Vars,

    /// <summary>Ни в файле, ни в base ключа нет — действует умолчание из определения в коде.</summary>
    Default,

    /// <summary>Поле недоступно для этой сущности (нет сессии или нет определения).</summary>
    Missing,
}

/// <summary>Происхождение итогового значения и TOML-файл, в котором оно задано.</summary>
public sealed class FieldSourceInfo
{
    public FieldProvenance Provenance { get; init; }
    public string Path { get; init; }
}

public sealed class VarsPanelData
{
    public string ContextId { get; init; }
    public string Path { get; init; }
    public string FileName { get; init; }
    public List<VarsPanelRow> Rows { get; init; } = new();
}

public sealed class VarsPanelRow
{
    public string Section { get; init; }
    public string Key { get; init; }
    public string Value { get; init; }
}

public enum ContentGraphNodeKind
{
    Unit,
    Building,
    Base,
    Vars,
    Weapon,
    WorkTool,
}

public sealed class ContentGraphNode
{
    public string Key { get; init; }
    public string Title { get; init; }
    public string Detail { get; init; }
    public string Path { get; init; }
    public ContentGraphNodeKind Kind { get; init; }
}

public sealed class ContentGraphEdge
{
    public string From { get; init; }
    public string To { get; init; }
    public string Label { get; init; }
}

public sealed class ContentGraphData
{
    public string ContextId { get; init; }
    public string RootKey { get; init; }
    public List<ContentGraphNode> Nodes { get; init; } = new();
    public List<ContentGraphEdge> Edges { get; init; } = new();
}

/// <summary>Одна запись каталога редактора — файл с <c>id</c>, ещё до сборки Catalog.</summary>
public sealed class ContentEditorEntry
{
    public string Id;
    public string Path;
    public string FileName;
    public string DisplayName;
    public ContentEntityKind Kind;

    /// <summary>Шаблоны с <c>abstract = true</c> в каталог игры не попадают, но файл существует.</summary>
    public bool Abstract;
}

/// <summary>
/// Итог проверки внешних правок: что можно подтянуть молча и что конфликтует
/// с несохранённым черновиком.
/// </summary>
public sealed class ExternalChangeReport
{
    /// <summary>Открытые вкладки без локальных правок, уже перечитанные с диска.</summary>
    public readonly List<string> Reloaded = new();

    /// <summary>
    /// Открытые вкладки с Dirty=true, у которых метка времени на диске новее LoadedStamp.
    /// Черновик не трогаем: иначе правка в редакторе контента будет потеряна.
    /// </summary>
    public readonly List<string> Conflicts = new();

    /// <summary>Файлы каталога, появившиеся или изменившиеся без открытой вкладки.</summary>
    public int CatalogChanged;

    public bool Any => Reloaded.Count > 0 || Conflicts.Count > 0 || CatalogChanged > 0;
}

/// <summary>Сериализуемый снимок вкладок, переживающий перезагрузку C#-сборки.</summary>
public sealed class ContentEditorWorkspace
{
    public string ActiveId { get; set; }
    public List<ContentEditorSessionSnapshot> Sessions { get; set; } = new();
}

public sealed class ContentEditorSessionSnapshot
{
    public string Id { get; set; }
    public string DraftText { get; set; }
    public ulong LoadedStamp { get; set; }
    public bool Dirty { get; set; }
    public bool ShowOnField { get; set; }
    public float FieldX { get; set; }
    public float FieldY { get; set; }
}

/// <summary>
/// Рабочее пространство редактора контента.
///
/// ДВА СЛОЯ ТЕКСТА. <see cref="_texts"/> — последний известный снимок с диска.
/// У каждой открытой вкладки свой <see cref="OpenEntitySession.DraftText"/>: правки формы
/// идут только туда, пока пользователь не нажмёт «Применить».
///
/// СБОРКА. <see cref="Catalog"/> собирается через <see cref="ContentCompiler.CompileWithOverrides"/>
/// с подстановкой черновиков. Предпросмотр и таблица баланса читают именно этот каталог,
/// а не глобальный <see cref="Content.Catalog"/>, пока правки не сохранены.
///
/// ВНЕШНИЕ ПРАВКИ. Метка времени файла сравнивается с <see cref="OpenEntitySession.LoadedStamp"/>.
/// Чистую вкладку можно перечитать молча; грязную — только после явного решения пользователя.
/// </summary>
public sealed class ContentEditorStore
{
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> _timestamps = new(StringComparer.Ordinal);
    private readonly List<ContentEditorEntry> _entries = new();
    private readonly Dictionary<string, OpenEntitySession> _sessions = new(StringComparer.Ordinal);

    public Catalog Catalog { get; private set; } = new();
    public IReadOnlyList<ContentEditorEntry> Entries => _entries;
    public IReadOnlyCollection<OpenEntitySession> Sessions => _sessions.Values;

    public OpenEntitySession ActiveSession { get; private set; }

    /// <summary>Форма, поле и вкладки подписаны на это событие и перерисовываются целиком.</summary>
    public event Action Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public ContentEditorWorkspace CaptureWorkspace() => new()
    {
        ActiveId = ActiveSession?.Id,
        Sessions = _sessions.Values.Select(session => new ContentEditorSessionSnapshot
        {
            Id = session.Id,
            DraftText = session.DraftText,
            LoadedStamp = session.LoadedStamp,
            Dirty = session.Dirty,
            ShowOnField = session.ShowOnField,
            FieldX = session.FieldPosition.X,
            FieldY = session.FieldPosition.Y,
        }).ToList(),
    };

    /// <summary>
    /// Восстановить вкладки после загрузки файлов. Отсутствующие сущности пропускаются;
    /// сохранённый LoadedStamp позволяет обычной проверке обнаружить внешний конфликт.
    /// </summary>
    public void RestoreWorkspace(ContentEditorWorkspace workspace)
    {
        if (workspace?.Sessions == null)
            return;

        _sessions.Clear();
        foreach (var snapshot in workspace.Sessions)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == snapshot.Id);
            if (entry == null || !_texts.ContainsKey(entry.Path))
                continue;

            var session = new OpenEntitySession(
                entry,
                _texts[entry.Path],
                _timestamps.GetValueOrDefault(entry.Path));
            session.Restore(snapshot);
            _sessions[session.Id] = session;
        }

        ActiveSession = workspace.ActiveId != null
                        && _sessions.TryGetValue(workspace.ActiveId, out var active)
            ? active
            : _sessions.Values.LastOrDefault();
        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Полная перечитывка всех .toml определений.
    ///
    /// Открытые вкладки, чьи файлы исчезли, закрываются. Черновики оставшихся
    /// вкладок по умолчанию не сбрасываются: иначе «Перечитать» уничтожил бы
    /// несохранённую работу. Чистые вкладки UI обновляет отдельно через
    /// <see cref="AcceptExternalReload"/> либо через <see cref="CheckExternalChanges"/>.
    /// </summary>
    public void ReloadFromDisk()
    {
        _texts.Clear();
        _timestamps.Clear();
        _entries.Clear();

        foreach (string path in EnumerateToml())
        {
            string text = ReadText(path);
            if (text == null)
                continue;

            _texts[path] = text;
            _timestamps[path] = FileStamp(path);
            TryRegisterEntry(path, text);
        }

        // Сохранить черновики открытых вкладок, если файлы ещё существуют
        var alive = new List<string>();
        foreach (var session in _sessions.Values)
        {
            if (_texts.ContainsKey(session.Path))
                alive.Add(session.Id);
        }

        var drop = _sessions.Keys.Where(id => !alive.Contains(id)).ToList();
        foreach (string id in drop)
            _sessions.Remove(id);

        if (ActiveSession != null && !_sessions.ContainsKey(ActiveSession.Id))
            ActiveSession = _sessions.Values.LastOrDefault();

        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Сверить метки времени на диске с загруженными. Вызывать при возврате фокуса
    /// в Godot и при показе вкладки «Контент»: правка в Cursor или другом редакторе
    /// иначе останется незамеченной до явного «Перечитать».
    ///
    /// Чистые вкладки подтягиваются сразу. Грязные попадают в Conflicts — UI обязан
    /// спросить, сбросить черновик или оставить его.
    /// </summary>
    public ExternalChangeReport CheckExternalChanges()
    {
        var report = new ExternalChangeReport();
        bool catalogTouched = false;
        var pathsOnDisk = EnumerateToml().ToHashSet(StringComparer.Ordinal);

        // Сначала обновить снимки файлов, у которых нет открытой вкладки или вкладка чистая
        foreach (string path in pathsOnDisk)
        {
            ulong stamp = FileStamp(path);
            ulong known = _timestamps.GetValueOrDefault(path);
            if (stamp == 0 || stamp == known)
                continue;

            var open = _sessions.Values.FirstOrDefault(s => s.Path == path);
            if (open != null)
                continue;

            string text = ReadText(path);
            if (text == null)
                continue;

            _texts[path] = text;
            _timestamps[path] = stamp;
            catalogTouched = true;
        }

        // Удалённые закрытые файлы иначе навсегда оставались в индексе.
        foreach (string path in _texts.Keys.Where(path => !pathsOnDisk.Contains(path)).ToList())
        {
            if (_sessions.Values.Any(session => session.Path == path))
                continue;

            _texts.Remove(path);
            _timestamps.Remove(path);
            catalogTouched = true;
        }

        foreach (var session in _sessions.Values.ToList())
        {
            ulong stamp = FileStamp(session.Path);
            if (stamp == session.LoadedStamp)
                continue;

            if (stamp == 0)
            {
                // Dirty-вкладка может намеренно восстановить удалённый файл. Чистая
                // принимает удаление сразу; LoadedStamp=0 означает уже подтверждённое
                // решение «оставить черновик».
                if (session.LoadedStamp == 0)
                    continue;

                if (session.Dirty)
                {
                    report.Conflicts.Add(session.Id);
                    continue;
                }

                _sessions.Remove(session.Id);
                _texts.Remove(session.Path);
                _timestamps.Remove(session.Path);
                if (ActiveSession == session)
                    ActiveSession = _sessions.Values.LastOrDefault();
                report.Reloaded.Add($"{session.Id} (файл удалён)");
                catalogTouched = true;
                continue;
            }

            if (session.Dirty)
            {
                report.Conflicts.Add(session.Id);
                continue;
            }

            string text = ReadText(session.Path);
            if (text == null)
                continue;

            _texts[session.Path] = text;
            _timestamps[session.Path] = stamp;
            session.AcceptExternalReload(text, stamp);
            report.Reloaded.Add(session.Id);
        }

        if (catalogTouched)
        {
            _entries.Clear();
            foreach (var pair in _texts)
                TryRegisterEntry(pair.Key, pair.Value);
            report.CatalogChanged++;
        }

        if (report.Reloaded.Count > 0 || catalogTouched)
            Recompile();

        if (report.Any)
            Changed?.Invoke();

        return report;
    }

    /// <summary>
    /// Принять внешнюю версию файла для грязной вкладки: черновик заменяется текстом
    /// с диска. Вызывать только после подтверждения в UI.
    /// </summary>
    public void AcceptExternalReload(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        string text = ReadText(session.Path);
        if (text == null)
        {
            _sessions.Remove(id);
            _texts.Remove(session.Path);
            _timestamps.Remove(session.Path);
            _entries.RemoveAll(entry => entry.Path == session.Path);
            if (ActiveSession == session)
                ActiveSession = _sessions.Values.LastOrDefault();
            Recompile();
            Changed?.Invoke();
            return;
        }

        ulong stamp = FileStamp(session.Path);
        _texts[session.Path] = text;
        _timestamps[session.Path] = stamp;
        session.AcceptExternalReload(text, stamp);
        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Оставить черновик поверх внешнего файла: обновляем только LoadedStamp, чтобы
    /// следующая проверка не считала тот же конфликт повторно. Запись на диск по-прежнему
    /// потребует явного «Применить» и перезапишет внешнюю правку.
    /// </summary>
    public void KeepDraftDespiteExternal(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        ulong stamp = FileStamp(session.Path);
        if (stamp == 0)
        {
            session.AcknowledgeExternalStamp(0);
            _timestamps.Remove(session.Path);
            Changed?.Invoke();
            return;
        }

        session.AcknowledgeExternalStamp(stamp);
        _timestamps[session.Path] = stamp;
        Changed?.Invoke();
    }

    public OpenEntitySession Open(string id)
    {
        if (_sessions.TryGetValue(id, out var existing))
        {
            ActiveSession = existing;
            Changed?.Invoke();
            return existing;
        }

        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry == null || !_texts.TryGetValue(entry.Path, out string text))
            return null;

        var session = new OpenEntitySession(entry, text, _timestamps.GetValueOrDefault(entry.Path));
        AutoPlace(session);
        _sessions[id] = session;
        ActiveSession = session;
        Changed?.Invoke();
        return session;
    }

    public void Activate(string id)
    {
        if (_sessions.TryGetValue(id, out var session))
        {
            ActiveSession = session;
            Changed?.Invoke();
        }
    }

    public bool Close(string id, bool discard)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return true;

        if (session.Dirty && !discard)
            return false;

        _sessions.Remove(id);
        if (ActiveSession == session)
            ActiveSession = _sessions.Values.LastOrDefault();

        Recompile();
        Changed?.Invoke();
        return true;
    }

    public void SetField(OpenEntitySession session, ContentFieldSpec field, object value, bool clear)
    {
        if (session == null || field == null)
            return;

        string section = field.RootOnly ? null : field.Section;
        string prior = session.DraftText;

        if (clear)
        {
            session.DraftText = TomlPatchWriter.RemoveKey(session.DraftText, section, field.Key);
        }
        else
        {
            string encoded = Encode(field, value);
            if (encoded == null)
                return;

            // Если значение совпадает с унаследованным — локальный ключ не нужен
            string without = TomlPatchWriter.RemoveKey(session.DraftText, section, field.Key);
            if (InheritedEquals(session.Path, without, section, field.Key, encoded))
                session.DraftText = without;
            else
                session.DraftText = TomlPatchWriter.SetKey(session.DraftText, section, field.Key, encoded);
        }

        // TextSubmitted и FocusExited могут прийти для одного и того же LineEdit подряд.
        // Повторная сборка при неизменившемся тексте не нужна и порождала лишние Refresh.
        if (string.Equals(prior, session.DraftText, StringComparison.Ordinal))
            return;

        session.MarkDirty();
        Recompile();
        Changed?.Invoke();
    }

    public string Apply(OpenEntitySession session)
    {
        if (session == null)
            return "нет открытой сущности";

        ulong stamp = FileStamp(session.Path);
        if (stamp != 0 && session.LoadedStamp != 0 && stamp != session.LoadedStamp)
            return "файл изменён извне; перечитайте с диска";

        var probe = CompileTexts(DraftMap());
        if (probe.Errors > 0)
            return $"сборка с ошибками: {probe.Errors}";

        string absolute = ProjectSettings.GlobalizePath(session.Path);
        string temp = absolute + ".tmp";

        try
        {
            File.WriteAllText(temp, session.DraftText);
            if (File.Exists(absolute))
                File.Delete(absolute);
            File.Move(temp, absolute);
        }
        catch (Exception ex)
        {
            return $"запись не удалась: {ex.Message}";
        }

        _texts[session.Path] = session.DraftText;
        session.AcceptSaved(FileStamp(session.Path));
        _timestamps[session.Path] = session.LoadedStamp;

        Content.Reload();
        Recompile();
        Changed?.Invoke();
        return null;
    }

    public void Revert(OpenEntitySession session)
    {
        if (session == null || !_texts.TryGetValue(session.Path, out string text))
            return;

        session.Revert(text, _timestamps.GetValueOrDefault(session.Path));
        Recompile();
        Changed?.Invoke();
    }

    public FieldProvenance Provenance(OpenEntitySession session, ContentFieldSpec field)
        => FieldSource(session, field).Provenance;

    public FieldSourceInfo FieldSource(OpenEntitySession session, ContentFieldSpec field)
    {
        if (session == null || field == null)
            return new FieldSourceInfo { Provenance = FieldProvenance.Missing };

        string section = field.RootOnly ? null : field.Section;
        if (HasLocalKey(session.DraftText, section, field.Key))
        {
            var table = ParseTable(session.DraftText);
            if (TryRawField(table, section, field.Key, out object localValue)
                && TryVarsTarget(localValue, out string varsPath))
            {
                return new FieldSourceInfo
                {
                    Provenance = FieldProvenance.Vars,
                    Path = varsPath,
                };
            }

            return new FieldSourceInfo
            {
                Provenance = FieldProvenance.Local,
                Path = session.Path,
            };
        }

        if (InheritedSource(session.Path, session.DraftText, section, field.Key) is { } inherited)
            return inherited;

        return new FieldSourceInfo { Provenance = FieldProvenance.Default };
    }

    /// <summary>Открыть определение по пути; vars-файлы не являются сущностями каталога.</summary>
    public bool OpenPath(string path)
    {
        var entry = _entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.Ordinal));
        if (entry == null)
            return false;

        return Open(entry.Id) != null;
    }

    public VarsPanelData VarsPanel(string path)
    {
        var session = ActiveSession;
        if (session == null
            || string.IsNullOrEmpty(path)
            || !TomlResolver.IsVars(path)
            || !_texts.TryGetValue(path, out string text))
        {
            return null;
        }

        var table = ParseTable(text);
        if (table == null)
            return null;

        var result = new VarsPanelData
        {
            ContextId = session.Id,
            Path = path,
            FileName = FileNameOf(path),
        };
        AddVarsRows(result.Rows, null, table);
        return result;
    }

    /// <summary>
    /// Построить зависимости только активной сессии. Граф использует сырой TOML,
    /// поскольку разрешённый каталог уже не содержит путей base и vars.
    /// </summary>
    public ContentGraphData ActiveContextGraph()
    {
        var session = ActiveSession;
        if (session == null)
            return null;

        var texts = DraftMap();
        var graph = new ContentGraphData
        {
            ContextId = session.Id,
            RootKey = session.Path,
        };
        var nodeKeys = new HashSet<string>(StringComparer.Ordinal);
        var edgeKeys = new HashSet<string>(StringComparer.Ordinal);

        AddGraphNode(graph, nodeKeys, session.Path, session.Id, session.DisplayName,
            GraphKind(session.Kind, session.Id, session.Path));
        AddTomlDependencies(
            session.Path, session.Path, texts, graph, nodeKeys, edgeKeys,
            new HashSet<string>(StringComparer.Ordinal));

        if (session.Kind is ContentEntityKind.Unit or ContentEntityKind.Building)
        {
            var unit = PreviewUnit(session.Id);
            foreach (string toolId in unit?.ToolIds ?? Array.Empty<string>())
            {
                var entry = _entries.FirstOrDefault(candidate => candidate.Id == toolId);
                if (entry == null)
                    continue;

                var tool = PreviewTool(toolId);
                var kind = tool is WeaponDefinition
                    ? ContentGraphNodeKind.Weapon
                    : ContentGraphNodeKind.WorkTool;
                AddGraphNode(graph, nodeKeys, entry.Path, toolId,
                    tool?.DisplayName ?? entry.DisplayName, kind);
                AddGraphEdge(graph, edgeKeys, session.Path, entry.Path,
                    kind == ContentGraphNodeKind.Weapon ? "оружие" : "инструмент");
                AddTomlDependencies(
                    entry.Path, entry.Path, texts, graph, nodeKeys, edgeKeys,
                    new HashSet<string>(StringComparer.Ordinal));
            }
        }

        return graph;
    }

    /// <summary>
    /// Есть ли ключ непосредственно в черновике. Provenance=Vars не отвечает на этот
    /// вопрос: ссылка на vars может быть как локальной, так и унаследованной.
    /// </summary>
    public bool HasLocalValue(OpenEntitySession session, ContentFieldSpec field)
    {
        if (session == null || field == null)
            return false;

        string section = field.RootOnly ? null : field.Section;
        return HasLocalKey(session.DraftText, section, field.Key);
    }

    public object EffectiveValue(OpenEntitySession session, ContentFieldSpec field)
    {
        if (session == null)
            return null;

        if (session.Kind is ContentEntityKind.Weapon or ContentEntityKind.WorkTool)
        {
            var tool = Catalog.Tool(session.Id);
            return ReadToolValue(tool, field);
        }

        var unit = Catalog.Unit(session.Id);
        return ReadUnitValue(unit, field, Catalog.Tags);
    }

    public UnitDefinition PreviewUnit(string id) => Catalog.Unit(id);

    public ToolDefinition PreviewTool(string id) => Catalog.Tool(id);

    public void LayoutOpenSessions()
    {
        float x = 0f;
        float y = 0f;
        float rowHeight = 0f;
        const float gap = Const.Unit * 2.5f;
        const float rowWidth = Const.Unit * 14f;

        foreach (var session in _sessions.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var def = Catalog.Unit(session.Id);
            float width = def != null
                ? (def.IsStructure ? def.Width * Const.Unit : def.RadiusPx * 2f)
                : Const.Unit;
            float height = def != null
                ? (def.IsStructure ? def.Height * Const.Unit : def.RadiusPx * 2f)
                : Const.Unit;

            if (x > 0f && x + width > rowWidth)
            {
                x = 0f;
                y += rowHeight + gap;
                rowHeight = 0f;
            }

            session.FieldPosition = new Vector2(x + width * 0.5f, y + height * 0.5f);
            x += width + gap;
            rowHeight = Mathf.Max(rowHeight, height);
        }

        Changed?.Invoke();
    }

    private void AutoPlace(OpenEntitySession session)
    {
        // Новая сущность ставится правее всех уже открытых с учётом их габарита.
        // Постоянный шаг в три клетки приводил к наложению титанов и подписей.
        float left = 0f;
        float newHalf = HalfWidth(session.Id);

        foreach (var open in _sessions.Values)
            left = Mathf.Max(left, open.FieldPosition.X + HalfWidth(open.Id) + Const.Unit * 2.5f);

        session.FieldPosition = new Vector2(left + newHalf, 0f);
    }

    private float HalfWidth(string id)
    {
        var def = Catalog.Unit(id);
        if (def == null)
            return Const.Unit * 0.5f;

        return def.IsStructure
            ? Mathf.Max(def.Width, 1) * Const.Unit * 0.5f
            : Mathf.Max(UnitSilhouette.Extent(def, def.RadiusPx), def.RadiusPx);
    }

    private void Recompile()
    {
        var texts = DraftMap();
        var result = CompileTexts(texts);
        Catalog = result.Catalog;

        // id, name, abstract и локальный kind могут измениться как в форме, так и
        // во внешнем редакторе. Индекс должен соответствовать тем же текстам, из
        // которых только что собран Catalog.
        _entries.Clear();
        foreach (var pair in texts)
            TryRegisterEntry(pair.Key, pair.Value);

        ReclassifyEntries();
        ReconcileOpenSessions();
    }

    /// <summary>
    /// Локальный kind может находиться в base, поэтому окончательный вид определяется
    /// только после компиляции. Это существенно для оружия и рабочих инструментов.
    /// </summary>
    private void ReclassifyEntries()
    {
        foreach (var entry in _entries)
        {
            if (Catalog.Tool(entry.Id) is WeaponDefinition)
                entry.Kind = ContentEntityKind.Weapon;
            else if (Catalog.Tool(entry.Id) is WorkToolDefinition)
                entry.Kind = ContentEntityKind.WorkTool;
            else if (Catalog.Unit(entry.Id) is { } unit)
                entry.Kind = unit.IsStructure
                    ? ContentEntityKind.Building
                    : ContentEntityKind.Unit;
        }
    }

    private void ReconcileOpenSessions()
    {
        var sessions = _sessions.Values.ToList();
        _sessions.Clear();
        foreach (var session in sessions)
        {
            var entry = _entries.FirstOrDefault(candidate => candidate.Path == session.Path);
            if (entry == null)
                continue;

            session.UpdateEntry(entry);
            _sessions[session.Id] = session;
        }

        if (ActiveSession != null && !_sessions.ContainsValue(ActiveSession))
            ActiveSession = _sessions.Values.LastOrDefault();
    }

    private Dictionary<string, string> DraftMap()
    {
        var map = new Dictionary<string, string>(_texts, StringComparer.Ordinal);
        foreach (var session in _sessions.Values)
            map[session.Path] = session.DraftText;
        return map;
    }

    private (Catalog Catalog, int Errors) CompileTexts(Dictionary<string, string> texts)
    {
        var catalog = new Catalog();
        int errors = ContentCompiler.CompileWithOverrides(catalog, texts);
        return (catalog, errors);
    }

    private void TryRegisterEntry(string path, string text)
    {
        var table = ParseTable(text);
        if (table == null)
            return;

        if (!table.TryGetValue("id", out object idValue) || idValue is not string id || id.Length == 0)
            return;

        bool abstractFlag = table.TryGetValue("abstract", out object abs) && abs is true;
        string name = table.TryGetValue("name", out object nameValue) && nameValue is string n ? n : id;

        ContentEntityKind kind;
        if (table.TryGetValue("kind", out object kindValue) && kindValue is string kindName)
        {
            kind = kindName == "weapon" ? ContentEntityKind.Weapon : ContentEntityKind.WorkTool;
        }
        else
        {
            bool building = path.Contains("/buildings/", StringComparison.Ordinal);
            kind = building ? ContentEntityKind.Building : ContentEntityKind.Unit;
        }

        _entries.Add(new ContentEditorEntry
        {
            Id = id,
            Path = path,
            FileName = FileNameOf(path),
            DisplayName = name,
            Kind = kind,
            Abstract = abstractFlag,
        });
    }

    private static string FileNameOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private bool InheritedEquals(
        string path, string draftWithoutKey, string section, string key, string encoded)
    {
        // Сравниваем текстовое представление унаследованного значения с кандидатом
        if (!TryResolveValue(path, draftWithoutKey, section, key, out object inherited))
            return false;

        string inheritedText = EncodeObject(inherited);
        return string.Equals(NormalizeToml(inheritedText), NormalizeToml(encoded), StringComparison.Ordinal);
    }

    private FieldSourceInfo InheritedSource(
        string path, string draftText, string section, string key)
    {
        var texts = DraftMap();
        texts[path] = draftText;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return FindSourceInFoundation(path, section, key, texts, visited);
    }

    /// <summary>
    /// Найти исходный ключ в сырой цепочке base, не материализуя значение. После Resolve
    /// ссылка на vars уже превращена в число, поэтому прежняя проверка ошибочно помечала
    /// такие значения как обычный base.
    /// </summary>
    private static FieldSourceInfo FindSourceInFoundation(
        string path,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts,
        HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out string text))
            return null;

        var table = ParseTable(text);
        if (table == null)
            return null;

        if (!string.IsNullOrEmpty(section)
            && table.TryGetValue(section, out object sectionValue)
            && sectionValue is TomlTable localSection)
        {
            if (localSection.TryGetValue("base", out object sectionBase)
                && sectionBase is string sectionBasePath
                && TomlResolver.TryCanonicalPath(
                    sectionBasePath, out string targetPath, out _))
            {
                return FindSourceInChain(targetPath, section, key, texts, visited);
            }
        }

        if (table.TryGetValue("base", out object rootBase)
            && rootBase is string rootBasePath
            && TomlResolver.TryCanonicalPath(rootBasePath, out string basePath, out _))
        {
            return FindSourceInChain(basePath, section, key, texts, visited);
        }

        return null;
    }

    private static FieldSourceInfo FindSourceInChain(
        string path,
        string section,
        string key,
        IReadOnlyDictionary<string, string> texts,
        HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out string text))
            return null;

        var table = ParseTable(text);
        if (table == null)
            return null;

        // Секционный base на vars-файл использует его корневые ключи.
        if (TomlResolver.IsVars(path) && table.TryGetValue(key, out _))
        {
            return new FieldSourceInfo
            {
                Provenance = FieldProvenance.Vars,
                Path = path,
            };
        }

        if (string.IsNullOrEmpty(section))
        {
            if (table.TryGetValue(key, out object value))
                return SourceAt(path, value);
        }
        else if (table.TryGetValue(section, out object sectionValue)
                 && sectionValue is TomlTable sectionTable)
        {
            if (sectionTable.TryGetValue(key, out object value))
                return SourceAt(path, value);

            if (sectionTable.TryGetValue("base", out object sectionBase)
                && sectionBase is string sectionBasePath
                && TomlResolver.TryCanonicalPath(
                    sectionBasePath, out string sectionTarget, out _))
            {
                return FindSourceInChain(sectionTarget, section, key, texts, visited);
            }
        }

        if (table.TryGetValue("base", out object rootBase)
            && rootBase is string rootBasePath
            && TomlResolver.TryCanonicalPath(rootBasePath, out string basePath, out _))
        {
            return FindSourceInChain(basePath, section, key, texts, visited);
        }

        return null;
    }

    private static FieldSourceInfo SourceAt(string definingPath, object value)
    {
        if (TryVarsTarget(value, out string varsPath))
        {
            return new FieldSourceInfo
            {
                Provenance = FieldProvenance.Vars,
                Path = varsPath,
            };
        }

        return new FieldSourceInfo
        {
            Provenance = FieldProvenance.Base,
            Path = definingPath,
        };
    }

    private static bool TryVarsTarget(object value, out string path)
    {
        path = null;
        return value is string text
               && text.Contains(".vars.toml", StringComparison.Ordinal)
               && TomlResolver.TryCanonicalPath(text, out path, out _);
    }

    private static bool TryRawField(
        TomlTable table, string section, string key, out object value)
    {
        value = null;
        if (table == null)
            return false;

        if (string.IsNullOrEmpty(section))
            return table.TryGetValue(key, out value);

        return table.TryGetValue(section, out object sectionValue)
               && sectionValue is TomlTable sectionTable
               && sectionTable.TryGetValue(key, out value);
    }

    private static void AddVarsRows(
        List<VarsPanelRow> rows, string section, TomlTable table)
    {
        foreach (var pair in table.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string nestedSection = string.IsNullOrEmpty(section)
                ? pair.Key
                : $"{section}.{pair.Key}";
            if (pair.Value is TomlTable nested)
            {
                AddVarsRows(rows, nestedSection, nested);
                continue;
            }

            rows.Add(new VarsPanelRow
            {
                Section = section ?? "",
                Key = pair.Key,
                Value = EncodeObject(pair.Value),
            });
        }
    }

    private void AddTomlDependencies(
        string nodeKey,
        string path,
        IReadOnlyDictionary<string, string> texts,
        ContentGraphData graph,
        HashSet<string> nodeKeys,
        HashSet<string> edgeKeys,
        HashSet<string> visited)
    {
        if (!visited.Add(path) || !texts.TryGetValue(path, out string text))
            return;

        var table = ParseTable(text);
        if (table == null)
            return;

        if (table.TryGetValue("base", out object rootBase))
            AddBaseDependency(nodeKey, rootBase, "наследует", texts, graph, nodeKeys, edgeKeys, visited);

        foreach (var pair in table)
        {
            if (pair.Value is TomlTable section
                && section.TryGetValue("base", out object sectionBase))
            {
                AddBaseDependency(
                    nodeKey, sectionBase, $"секция [{pair.Key}]", texts,
                    graph, nodeKeys, edgeKeys, visited);
            }
        }

        AddVarsReferences(nodeKey, null, table, texts, graph, nodeKeys, edgeKeys);
    }

    private void AddBaseDependency(
        string from,
        object rawPath,
        string label,
        IReadOnlyDictionary<string, string> texts,
        ContentGraphData graph,
        HashSet<string> nodeKeys,
        HashSet<string> edgeKeys,
        HashSet<string> visited)
    {
        if (rawPath is not string text
            || !TomlResolver.TryCanonicalPath(text, out string target, out _)
            || !texts.ContainsKey(target))
        {
            return;
        }

        AddGraphNodeForPath(graph, nodeKeys, target);
        AddGraphEdge(graph, edgeKeys, from, target, label);
        AddTomlDependencies(target, target, texts, graph, nodeKeys, edgeKeys, visited);
    }

    private void AddVarsReferences(
        string from,
        string section,
        TomlTable table,
        IReadOnlyDictionary<string, string> texts,
        ContentGraphData graph,
        HashSet<string> nodeKeys,
        HashSet<string> edgeKeys)
    {
        foreach (var pair in table)
        {
            if (pair.Value is TomlTable nested)
            {
                string nestedSection = string.IsNullOrEmpty(section)
                    ? pair.Key
                    : $"{section}.{pair.Key}";
                AddVarsReferences(from, nestedSection, nested, texts, graph, nodeKeys, edgeKeys);
                continue;
            }

            if (pair.Value is not string value
                || !TomlResolver.TryCanonicalPath(value, out string target, out string property)
                || !TomlResolver.IsVars(target)
                || !texts.ContainsKey(target))
            {
                continue;
            }

            AddGraphNodeForPath(graph, nodeKeys, target);
            string field = string.IsNullOrEmpty(section) ? pair.Key : $"{section}.{pair.Key}";
            string label = string.IsNullOrEmpty(property)
                ? field
                : $"{field} → {property}";
            AddGraphEdge(graph, edgeKeys, from, target, label);
        }
    }

    private void AddGraphNodeForPath(
        ContentGraphData graph, HashSet<string> nodeKeys, string path)
    {
        var entry = _entries.FirstOrDefault(candidate => candidate.Path == path);
        string id = entry?.Id ?? FileNameOf(path);
        string title = entry?.DisplayName ?? FileNameOf(path);
        AddGraphNode(graph, nodeKeys, path, id, title, GraphKind(entry?.Kind, id, path, entry?.Abstract == true));
    }

    private static void AddGraphNode(
        ContentGraphData graph,
        HashSet<string> nodeKeys,
        string key,
        string id,
        string title,
        ContentGraphNodeKind kind)
    {
        if (!nodeKeys.Add(key))
            return;

        graph.Nodes.Add(new ContentGraphNode
        {
            Key = key,
            Title = FileNameOf(key),
            Detail = string.IsNullOrEmpty(title) || string.Equals(title, id, StringComparison.Ordinal)
                ? id
                : $"{id} · {title}",
            Path = key,
            Kind = kind,
        });
    }

    private static void AddGraphEdge(
        ContentGraphData graph,
        HashSet<string> edgeKeys,
        string from,
        string to,
        string label)
    {
        string edgeKey = $"{from}\n{to}\n{label}";
        if (!edgeKeys.Add(edgeKey))
            return;

        graph.Edges.Add(new ContentGraphEdge { From = from, To = to, Label = label });
    }

    private ContentGraphNodeKind GraphKind(
        ContentEntityKind? kind, string id, string path = null, bool abstractEntry = false)
    {
        if (!string.IsNullOrEmpty(path) && TomlResolver.IsVars(path))
            return ContentGraphNodeKind.Vars;
        if (abstractEntry)
            return ContentGraphNodeKind.Base;

        return kind switch
        {
            ContentEntityKind.Building => ContentGraphNodeKind.Building,
            ContentEntityKind.Weapon => ContentGraphNodeKind.Weapon,
            ContentEntityKind.WorkTool => ContentGraphNodeKind.WorkTool,
            ContentEntityKind.Unit => ContentGraphNodeKind.Unit,
            _ when PreviewTool(id) is WeaponDefinition => ContentGraphNodeKind.Weapon,
            _ when PreviewTool(id) is WorkToolDefinition => ContentGraphNodeKind.WorkTool,
            _ => ContentGraphNodeKind.Base,
        };
    }

    private bool TryResolveValue(
        string path, string draftText, string section, string key, out object value)
    {
        value = null;
        var texts = DraftMap();
        texts[path] = draftText;

        var raw = new Dictionary<string, TomlTable>(StringComparer.Ordinal);
        foreach (var pair in texts)
        {
            var table = ParseTable(pair.Value);
            if (table != null)
                raw[pair.Key] = table;
        }

        if (TomlResolver.Resolve(raw, out var resolved) > 0)
            return false;

        if (!resolved.TryGetValue(path, out var doc))
            return false;

        if (string.IsNullOrEmpty(section))
            return doc.TryGetValue(key, out value);

        if (!doc.TryGetValue(section, out object sectionValue) || sectionValue is not TomlTable sectionTable)
            return false;

        return sectionTable.TryGetValue(key, out value);
    }

    private static bool HasLocalKey(string text, string section, string key)
    {
        var table = ParseTable(text);
        if (table == null)
            return false;

        if (string.IsNullOrEmpty(section))
            return table.ContainsKey(key);

        return table.TryGetValue(section, out object sectionValue)
               && sectionValue is TomlTable sectionTable
               && sectionTable.ContainsKey(key);
    }

    private static string Encode(ContentFieldSpec field, object value)
    {
        if (value == null)
            return null;

        return field.Type switch
        {
            ContentFieldType.Bool => TomlPatchWriter.FormatBool((bool)value),
            ContentFieldType.Float or ContentFieldType.NullableFloat =>
                TomlPatchWriter.FormatFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Int => TomlPatchWriter.FormatInt(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ContentFieldType.String or ContentFieldType.RequiredString or ContentFieldType.Path =>
                TomlPatchWriter.FormatString(Convert.ToString(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Enum => TomlPatchWriter.FormatEnum(Convert.ToString(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Color when value is Color color =>
                TomlPatchWriter.FormatColor(color.R, color.G, color.B, color.A),
            ContentFieldType.StringList when value is IList<string> list =>
                TomlPatchWriter.FormatStringList(list.ToList()),
            ContentFieldType.FloatList when value is IList<float> floats =>
                TomlPatchWriter.FormatFloatList(floats.ToList()),
            ContentFieldType.Scale when value is string scaleName =>
                TomlPatchWriter.FormatString(scaleName),
            ContentFieldType.Scale =>
                TomlPatchWriter.FormatFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            ContentFieldType.Vector2List when value is IList<Vector2> vectors =>
                FormatVectors(vectors),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    private static string FormatVectors(IList<Vector2> vectors)
    {
        var parts = new List<string>();
        foreach (var v in vectors)
            parts.Add($"[{TomlPatchWriter.FormatFloat(v.X)}, {TomlPatchWriter.FormatFloat(v.Y)}]");
        return "[" + string.Join(", ", parts) + "]";
    }

    private static string EncodeObject(object value) => value switch
    {
        null => "",
        bool flag => TomlPatchWriter.FormatBool(flag),
        double number => TomlPatchWriter.FormatFloat((float)number),
        long number => TomlPatchWriter.FormatInt((int)number),
        float number => TomlPatchWriter.FormatFloat(number),
        int number => TomlPatchWriter.FormatInt(number),
        string text => text.StartsWith('/') || text.StartsWith("res://")
            ? TomlPatchWriter.FormatString(text)
            : TomlPatchWriter.FormatString(text),
        TomlArray array => FormatTomlArray(array),
        _ => value.ToString(),
    };

    private static string FormatTomlArray(TomlArray array)
    {
        var parts = new List<string>();
        foreach (object item in array)
            parts.Add(EncodeObject(item));
        return "[" + string.Join(", ", parts) + "]";
    }

    private static string NormalizeToml(string text) =>
        (text ?? "").Replace(" ", "").Replace("\"", "").ToLowerInvariant();

    private static object ReadUnitValue(UnitDefinition def, ContentFieldSpec field, TagRegistry tags)
    {
        if (def == null || field == null)
            return null;

        string key = field.Key;
        string section = field.Section ?? "";

        if (field.RootOnly)
            return key switch
            {
                "id" => def.Id,
                "name" => def.DisplayName,
                "class" => def.Class.ToString(),
                "color" => def.Color,
                "tags" => TagNames(def, tags),
                "tools" => def.ToolIds,
                "buildbar" => def.Buildbar,
                _ => null,
            };

        return section switch
        {
            "body" => key switch
            {
                "max_health" => def.MaxHealth,
                "radius" => def.Radius,
                "vision_range" => def.VisionRange,
                "hull" => def.Hull.ToString(),
                "hull_trim" => def.HullTrim.ToString(),
                "hull_aspect" => def.HullAspect,
                "sprite" => def.Sprite,
                "sprite_scale" => def.SpriteScale,
                "sprite_rotation" => def.SpriteRotationDegrees,
                "ao_inner" => def.AmbientOcclusionInner,
                "ao_outer" => def.AmbientOcclusionOuter,
                "armor_rings" => def.ArmorRings,
                "front_plate" => def.FrontPlate,
                _ => null,
            },
            "movement" => key switch
            {
                "speed" => def.Speed,
                "turn_speed" => def.TurnSpeedDegrees,
                "acceleration" => def.Acceleration,
                "brake" => def.Brake,
                _ => null,
            },
            "footprint" => key switch
            {
                "rows" => def.Rows,
                "facing_degrees" => def.FacingDegrees,
                "requires_metal_spot" => def.RequiresMetalSpot,
                "pattern" => def.Pattern.ToString(),
                "pattern_alt" => def.PatternAlt.ToString(),
                "pattern_step" => def.PatternStep,
                _ => null,
            },
            "assembly" => key switch
            {
                "cost_metal" => def.Assembly?.CostMetal,
                "frame_health" => def.Assembly?.FrameHealth,
                _ => null,
            },
            "conversion" => key switch
            {
                "energy_drain" => def.Conversion?.EnergyDrain,
                "metal_output" => def.Conversion?.MetalOutput,
                _ => null,
            },
            "production" => key switch
            {
                "energy" => def.EnergyProduction,
                "metal" => def.MetalProduction,
                _ => null,
            },
            "storage" => key switch
            {
                "metal" => def.MetalStorage,
                "energy" => def.EnergyStorage,
                _ => null,
            },
            "terror" => key switch
            {
                "expansion_power" => def.ExpansionPowerWeight,
                "army_power" => def.ArmyPowerWeight,
                "ignore_modifiers" => def.IgnoreTerrorModifiers,
                _ => null,
            },
            "battle" => key switch
            {
                "approach_hold" => def.ApproachHoldFraction,
                _ => null,
            },
            "plant" => key switch
            {
                "factory_cooldown" => def.Plant?.FactoryCooldown,
                "build_power" => def.Plant?.BuildPower,
                "energy_per_power" => def.Plant?.EnergyPerPower,
                "rolloff_directions" => def.Plant?.RolloffDirections,
                "rolloff_clearance" => def.Plant?.RolloffClearance,
                _ => null,
            },
            "orders" => key switch
            {
                "allow" => OrderNames(def.DeclaredOrders),
                "deny" => OrderNames(def.DeniedOrders),
                _ => null,
            },
            _ => null,
        };
    }

    private static object ReadToolValue(ToolDefinition tool, ContentFieldSpec field)
    {
        if (tool == null || field == null)
            return null;

        return field.Key switch
        {
            "id" => tool.Id,
            "name" => tool.DisplayName,
            "kind" => tool is WeaponDefinition ? "weapon" : "work",
            "range" => tool.Range,
            "aim_while_moving" => tool.AimWhileMoving,
            "sprite" => tool.Sprite,
            "sprite_rotation" => tool.SpriteRotationDegrees,
            "damage" => (tool as WeaponDefinition)?.Damage,
            "fire_interval" => (tool as WeaponDefinition)?.FireInterval,
            "projectile_speed" => (tool as WeaponDefinition)?.ProjectileSpeed,
            "spread_degrees" => (tool as WeaponDefinition)?.SpreadDegrees,
            "aim_cone_degrees" => (tool as WeaponDefinition)?.AimConeDegrees,
            "projectile_radius" => (tool as WeaponDefinition)?.ProjectileRadius,
            "projectile_color" => (tool as WeaponDefinition)?.ProjectileColor,
            "power" => (tool as WorkToolDefinition)?.Power,
            "energy_per_power" => (tool as WorkToolDefinition)?.EnergyPerPower,
            "works" => WorkNames(tool as WorkToolDefinition),
            "repairs_units" => (tool as WorkToolDefinition)?.RepairsUnits,
            _ => null,
        };
    }

    private static string[] TagNames(UnitDefinition def, TagRegistry tags)
    {
        if (def == null || tags == null)
            return System.Array.Empty<string>();

        var names = new List<string>();
        foreach (string name in tags.Names)
        {
            if (tags.TryParse(new[] { name }, "editor", out var single) && def.Tags.Has(single))
                names.Add(name);
        }

        return names.ToArray();
    }

    private static string[] OrderNames(OrderSet set)
    {
        var names = new List<string>();
        foreach (OrderKind kind in Enum.GetValues<OrderKind>())
        {
            if (set.Allows(kind))
                names.Add(TomlPatchWriter.ToSnake(kind.ToString()));
        }

        return names.ToArray();
    }

    private static string[] WorkNames(WorkToolDefinition work)
    {
        if (work == null)
            return Array.Empty<string>();

        var names = new List<string>();
        if (work.Kinds.HasFlag(WorkKinds.Build))
            names.Add("build");
        if (work.Kinds.HasFlag(WorkKinds.Repair) && !work.Kinds.HasFlag(WorkKinds.Build))
            names.Add("repair");
        else if (work.Kinds.HasFlag(WorkKinds.Repair) && work.Kinds.HasFlag(WorkKinds.Build))
        {
            // стройка уже подразумевает ремонт; явное repair нужно только без build
        }

        if (work.Kinds.HasFlag(WorkKinds.Build))
            return new[] { "build" };

        if (work.Kinds.HasFlag(WorkKinds.Repair))
            return new[] { "repair" };

        return names.ToArray();
    }

    private static IEnumerable<string> EnumerateToml()
    {
        string[] roots =
        {
            "res://resources/content/",
            "res://resources/tools/",
            "res://resources/units/",
            "res://resources/buildings/",
        };

        foreach (string root in roots)
        foreach (string path in Files(root))
            yield return path;
    }

    private static IEnumerable<string> Files(string dir)
    {
        using var access = DirAccess.Open(dir);
        if (access == null)
            yield break;

        foreach (string file in access.GetFiles())
            if (file.EndsWith(".toml"))
                yield return dir + file;

        foreach (string sub in access.GetDirectories())
        {
            if (sub.StartsWith('.'))
                continue;

            foreach (string path in Files(dir + sub + "/"))
                yield return path;
        }
    }

    private static string ReadText(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    private static ulong FileStamp(string path)
    {
        string absolute = ProjectSettings.GlobalizePath(path);
        if (!File.Exists(absolute))
            return 0;

        return (ulong)File.GetLastWriteTimeUtc(absolute).Ticks;
    }

    private static TomlTable ParseTable(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var syntax = Toml.Parse(text);
        return syntax.HasErrors ? null : syntax.ToModel();
    }
}

/// <summary>
/// Открытая вкладка сущности. Черновик живёт здесь до «Применить» или «Отменить».
/// Положение на общем поле и флаг показа принадлежат вкладке, а не определению в Catalog.
/// </summary>
public sealed class OpenEntitySession
{
    public string Id { get; private set; }
    public string Path { get; }
    public string FileName { get; private set; }
    public string DisplayName { get; private set; }
    public ContentEntityKind Kind { get; private set; }

    /// <summary>Рабочий текст файла. Правки формы меняют только его.</summary>
    public string DraftText { get; set; }

    /// <summary>
    /// Метка времени файла на момент последней синхронизации с диском.
    /// Сравнение с текущей меткой обнаруживает внешнюю правку.
    /// </summary>
    public ulong LoadedStamp { get; private set; }

    public bool Dirty { get; private set; }

    /// <summary>Если false — сущность остаётся во вкладке, но не рисуется на поле сравнения.</summary>
    public bool ShowOnField { get; set; } = true;

    /// <summary>Центр силуэта на измерительном поле, в пикселях мира.</summary>
    public Vector2 FieldPosition { get; set; }

    public OpenEntitySession(ContentEditorEntry entry, string text, ulong stamp)
    {
        Id = entry.Id;
        Path = entry.Path;
        FileName = entry.FileName;
        DisplayName = entry.DisplayName;
        Kind = entry.Kind;
        DraftText = text;
        LoadedStamp = stamp;
    }

    public void MarkDirty() => Dirty = true;

    public void UpdateEntry(ContentEditorEntry entry)
    {
        if (entry == null || !string.Equals(entry.Path, Path, StringComparison.Ordinal))
            return;

        Id = entry.Id;
        FileName = entry.FileName;
        DisplayName = entry.DisplayName;
        Kind = entry.Kind;
    }

    public void Restore(ContentEditorSessionSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        DraftText = snapshot.DraftText ?? DraftText;
        LoadedStamp = snapshot.LoadedStamp;
        Dirty = snapshot.Dirty;
        ShowOnField = snapshot.ShowOnField;
        FieldPosition = new Vector2(snapshot.FieldX, snapshot.FieldY);
    }

    public void AcceptSaved(ulong stamp)
    {
        Dirty = false;
        LoadedStamp = stamp;
    }

    public void Revert(string text, ulong stamp)
    {
        DraftText = text;
        LoadedStamp = stamp;
        Dirty = false;
    }

    /// <summary>Подтянуть внешний файл вместо черновика (чистая вкладка или после согласия).</summary>
    public void AcceptExternalReload(string text, ulong stamp)
    {
        DraftText = text;
        LoadedStamp = stamp;
        Dirty = false;
    }

    /// <summary>
    /// Запомнить новую метку времени, не трогая черновик: конфликт больше не всплывает,
    /// пока файл снова не изменится снаружи.
    /// </summary>
    public void AcknowledgeExternalStamp(ulong stamp) => LoadedStamp = stamp;

    public string TabTitle =>
        Dirty
            ? $"{FileName} ({DisplayName}) *"
            : $"{FileName} ({DisplayName})";
}
