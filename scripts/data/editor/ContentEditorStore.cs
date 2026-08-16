using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// Рабочее пространство редактора контента: тексты файлов, черновики открытых вкладок
/// и собранный по ним каталог.
///
/// ДВА СЛОЯ ТЕКСТА. <see cref="ContentFileIndex"/> хранит последний известный снимок диска.
/// У каждой открытой вкладки свой <see cref="OpenEntitySession.DraftText"/>: правки формы
/// идут только туда, пока не нажато «Применить».
///
/// СБОРКА. <see cref="Catalog"/> собирается через <see cref="ContentCompiler.CompileWithOverrides"/>
/// с подстановкой черновиков. Предпросмотр, таблица баланса и панель формы волны читают
/// именно его, а не глобальный <see cref="Content.Catalog"/>, пока правки не сохранены.
///
/// ОБЛАСТИ. Сессии разделены на области <see cref="ContentEditorScope"/>: вкладки режима
/// сущностей и правку волн. Общими остаются тексты, черновики и сборка; активная сессия
/// у каждой области своя, поэтому выбор волны на карте не подменяет открытую сущность.
///
/// РАЗДЕЛЕНИЕ ТРУДА. Обход файлов и опознание записей — <see cref="ContentFileIndex"/>;
/// соответствие ключей и полей определения — <see cref="ContentValueReader"/>; поиск
/// источника значения — <see cref="ContentProvenance"/>; граф — <see cref="ContentGraphBuilder"/>;
/// снисходительное чтение волн — <see cref="WaveDraftReader"/>. Здесь остаётся состояние
/// и порядок действий над ним.
/// </summary>
public sealed class ContentEditorStore
{
    private readonly ContentFileIndex _index = new();
    private readonly Dictionary<string, OpenEntitySession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Активная сессия каждой области. Ключ отсутствует, пока область пуста.</summary>
    private readonly Dictionary<ContentEditorScope, string> _activeByScope = new();

    private List<WaveOverview> _waveOverviews = new();

    /// <summary>Каталог, собранный с учётом несохранённых черновиков.</summary>
    public Catalog Catalog { get; private set; } = new();

    /// <summary>Записи каталога редактора, включая шаблоны с <c>abstract = true</c>.</summary>
    public IReadOnlyList<ContentEditorEntry> Entries => _index.Entries;

    /// <summary>Все открытые сессии независимо от области.</summary>
    public IReadOnlyCollection<OpenEntitySession> Sessions => _sessions.Values;

    /// <summary>Обзор всех волн по черновикам, в том числе не прошедших проверку.</summary>
    public IReadOnlyList<WaveOverview> WaveOverviews => _waveOverviews;

    /// <summary>Активная вкладка режима сущностей.</summary>
    public OpenEntitySession ActiveSession => ActiveIn(ContentEditorScope.Entities);

    /// <summary>Волна, выбранная в режиме волн.</summary>
    public OpenEntitySession ActiveWave => ActiveIn(ContentEditorScope.Waves);

    /// <summary>Форма, поле, карта волн и вкладки подписаны на это событие.</summary>
    public event Action Changed;

    /// <summary>Оповестить интерфейс о правке, не менявшей текст (например, показ на поле).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    // ── Области и сессии ──────────────────────────────────────────────────────────

    /// <summary>Открытые сессии одной области в порядке словаря (порядке открытия).</summary>
    public IEnumerable<OpenEntitySession> SessionsIn(ContentEditorScope scope) =>
        _sessions.Values.Where(session => session.Scope == scope);

    /// <summary>Активная сессия области либо <c>null</c>.</summary>
    public OpenEntitySession ActiveIn(ContentEditorScope scope) =>
        _activeByScope.TryGetValue(scope, out string id)
        && _sessions.TryGetValue(id, out var session)
        && session.Scope == scope
            ? session
            : null;

    /// <summary>Сессия по идентификатору либо <c>null</c>.</summary>
    public OpenEntitySession Session(string id) =>
        id != null && _sessions.TryGetValue(id, out var session) ? session : null;

    /// <summary>
    /// Открыть файл во вкладке своей области. Уже открытая сессия просто становится
    /// активной, поэтому повторный щелчок по записи каталога не создаёт второй вкладки.
    /// </summary>
    public OpenEntitySession Open(string id)
    {
        if (_sessions.TryGetValue(id, out var existing))
        {
            SetActive(existing);
            Changed?.Invoke();
            return existing;
        }

        var entry = _index.ById(id);
        if (entry == null || _index.Text(entry.Path) is not { } text)
            return null;

        var session = new OpenEntitySession(entry, text, _index.KnownStamp(entry.Path));
        if (session.Scope == ContentEditorScope.Entities)
            AutoPlace(session);

        _sessions[id] = session;
        SetActive(session);
        Changed?.Invoke();
        return session;
    }

    /// <summary>Открыть определение по пути; файлы переменных сущностями не являются.</summary>
    public bool OpenPath(string path) =>
        _index.ByPath(path) is { } entry && Open(entry.Id) != null;

    /// <summary>Сделать сессию активной в её области.</summary>
    public void Activate(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        SetActive(session);
        Changed?.Invoke();
    }

    /// <summary>
    /// Закрыть вкладку. Сессия с черновиком закрывается только при <paramref name="discard"/>:
    /// решение о судьбе несохранённой правки принимает пользователь, а не Store.
    /// </summary>
    public bool Close(string id, bool discard)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return true;

        if (session.Dirty && !discard)
            return false;

        Drop(session);
        Recompile();
        Changed?.Invoke();
        return true;
    }

    private void SetActive(OpenEntitySession session) =>
        _activeByScope[session.Scope] = session.Id;

    /// <summary>Убрать сессию и передать признак активной соседней вкладке той же области.</summary>
    private void Drop(OpenEntitySession session)
    {
        _sessions.Remove(session.Id);
        if (_activeByScope.GetValueOrDefault(session.Scope) != session.Id)
            return;

        var next = SessionsIn(session.Scope).LastOrDefault();
        if (next != null)
            _activeByScope[session.Scope] = next.Id;
        else
            _activeByScope.Remove(session.Scope);
    }

    // ── Снимок рабочего пространства ──────────────────────────────────────────────

    /// <summary>Снять состояние вкладок для записи в снимок.</summary>
    public void CaptureInto(ContentEditorWorkspace workspace)
    {
        if (workspace == null)
            return;

        workspace.ActiveEntityId = ActiveSession?.Id;
        workspace.ActiveWaveId = ActiveWave?.Id;
        workspace.Sessions = _sessions.Values.Select(session => new ContentEditorSessionSnapshot
        {
            Id = session.Id,
            DraftText = session.DraftText,
            LoadedStamp = session.LoadedStamp,
            Dirty = session.Dirty,
            ShowOnField = session.ShowOnField,
            FieldX = session.FieldPosition.X,
            FieldY = session.FieldPosition.Y,
        }).ToList();
    }

    /// <summary>
    /// Восстановить вкладки после чтения файлов с диска. Записи, которых больше нет,
    /// пропускаются; сохранённая метка времени позволяет обычной проверке обнаружить
    /// внешний конфликт.
    /// </summary>
    public void RestoreWorkspace(ContentEditorWorkspace workspace)
    {
        if (workspace?.Sessions == null)
            return;

        _sessions.Clear();
        _activeByScope.Clear();

        foreach (var snapshot in workspace.Sessions)
        {
            var entry = _index.ById(snapshot.Id);
            if (entry == null || !_index.Has(entry.Path))
                continue;

            var session = new OpenEntitySession(
                entry, _index.Text(entry.Path), _index.KnownStamp(entry.Path));
            session.Restore(snapshot);
            _sessions[session.Id] = session;
        }

        RestoreActive(ContentEditorScope.Entities, workspace.ActiveEntityId);
        RestoreActive(ContentEditorScope.Waves, workspace.ActiveWaveId);

        Recompile();
        Changed?.Invoke();
    }

    private void RestoreActive(ContentEditorScope scope, string id)
    {
        if (id != null && _sessions.TryGetValue(id, out var session) && session.Scope == scope)
        {
            _activeByScope[scope] = id;
            return;
        }

        if (SessionsIn(scope).LastOrDefault() is { } fallback)
            _activeByScope[scope] = fallback.Id;
    }

    // ── Чтение с диска и внешние правки ───────────────────────────────────────────

    /// <summary>
    /// Полная перечитка всех <c>.toml</c> определений.
    ///
    /// Вкладки, чьи файлы исчезли, закрываются. Черновики оставшихся вкладок не
    /// сбрасываются: иначе «Перечитать» уничтожил бы несохранённую работу. Чистые вкладки
    /// интерфейс подтягивает отдельно через <see cref="AcceptExternalReload"/>.
    /// </summary>
    public void ReloadFromDisk()
    {
        _index.ReloadAll();

        foreach (var session in _sessions.Values.Where(s => !_index.Has(s.Path)).ToList())
            Drop(session);

        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Сверить метки времени на диске с загруженными. Вызывать при возврате фокуса в Godot
    /// и при показе вкладки «Content»: правка во внешнем редакторе иначе останется
    /// незамеченной до явного «Перечитать».
    ///
    /// Чистые вкладки подтягиваются сразу. Грязные попадают в <see cref="ExternalChangeReport.Conflicts"/> —
    /// интерфейс обязан спросить, сбросить черновик или оставить его.
    /// </summary>
    public ExternalChangeReport CheckExternalChanges()
    {
        var report = new ExternalChangeReport();
        var onDisk = ContentFileIndex.EnumerateToml().ToHashSet(StringComparer.Ordinal);
        bool catalogTouched = RefreshClosedFiles(onDisk) | ForgetDeletedFiles(onDisk);

        foreach (var session in _sessions.Values.ToList())
            catalogTouched |= RefreshOpenFile(session, report);

        if (catalogTouched)
        {
            _index.RebuildEntries(_index.Texts);
            report.CatalogChanged++;
        }

        if (report.Reloaded.Count > 0 || catalogTouched)
            Recompile();

        if (report.Any)
            Changed?.Invoke();

        return report;
    }

    /// <summary>Подтянуть файлы без открытой вкладки: конфликтовать здесь не с чем.</summary>
    private bool RefreshClosedFiles(IReadOnlyCollection<string> onDisk)
    {
        bool touched = false;
        foreach (string path in onDisk)
        {
            ulong stamp = ContentFileIndex.Stamp(path);
            if (stamp == 0 || stamp == _index.KnownStamp(path))
                continue;

            if (_sessions.Values.Any(session => session.Path == path))
                continue;

            if (ContentFileIndex.ReadText(path) is not { } text)
                continue;

            _index.Remember(path, text, stamp);
            touched = true;
        }

        return touched;
    }

    /// <summary>Убрать из индекса удалённые файлы, у которых нет открытой вкладки.</summary>
    private bool ForgetDeletedFiles(IReadOnlyCollection<string> onDisk)
    {
        bool touched = false;
        foreach (string path in _index.Texts.Keys.Where(path => !onDisk.Contains(path)).ToList())
        {
            if (_sessions.Values.Any(session => session.Path == path))
                continue;

            _index.Forget(path);
            touched = true;
        }

        return touched;
    }

    /// <summary>
    /// Сверить один открытый файл. Возвращает признак того, что каталог требует пересборки.
    /// </summary>
    private bool RefreshOpenFile(OpenEntitySession session, ExternalChangeReport report)
    {
        ulong stamp = ContentFileIndex.Stamp(session.Path);
        if (stamp == session.LoadedStamp)
            return false;

        if (stamp == 0)
            return AcceptDeletion(session, report);

        if (session.Dirty)
        {
            report.Conflicts.Add(session.Id);
            return false;
        }

        if (ContentFileIndex.ReadText(session.Path) is not { } text)
            return false;

        _index.Remember(session.Path, text, stamp);
        session.AcceptExternalReload(text, stamp);
        report.Reloaded.Add(session.Id);
        return false;
    }

    /// <summary>
    /// Файл открытой вкладки исчез с диска. Чистая вкладка принимает удаление сразу;
    /// вкладка с черновиком спрашивает: черновиком можно намеренно восстановить файл.
    /// Метка ноль означает уже принятое решение «оставить черновик».
    /// </summary>
    private bool AcceptDeletion(OpenEntitySession session, ExternalChangeReport report)
    {
        if (session.LoadedStamp == 0)
            return false;

        if (session.Dirty)
        {
            report.Conflicts.Add(session.Id);
            return false;
        }

        Drop(session);
        _index.Forget(session.Path);
        report.Reloaded.Add($"{session.Id} (file deleted)");
        return true;
    }

    /// <summary>
    /// Принять внешнюю версию файла: черновик заменяется текстом с диска. Вызывать только
    /// после подтверждения в интерфейсе.
    /// </summary>
    public void AcceptExternalReload(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        if (ContentFileIndex.ReadText(session.Path) is not { } text)
        {
            // Файла больше нет: вкладку держать не на чем.
            Drop(session);
            _index.Forget(session.Path);
            Recompile();
            Changed?.Invoke();
            return;
        }

        ulong stamp = ContentFileIndex.Stamp(session.Path);
        _index.Remember(session.Path, text, stamp);
        session.AcceptExternalReload(text, stamp);
        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Оставить черновик поверх внешнего файла: обновляется только метка времени, чтобы
    /// тот же конфликт не всплыл повторно. Запись на диск по-прежнему потребует «Применить»
    /// и перезапишет внешнюю правку.
    /// </summary>
    public void KeepDraftDespiteExternal(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
            return;

        ulong stamp = ContentFileIndex.Stamp(session.Path);
        session.AcknowledgeExternalStamp(stamp);
        if (stamp == 0)
            _index.Forget(session.Path);
        else
            _index.Remember(session.Path, _index.Text(session.Path) ?? session.DraftText, stamp);

        Changed?.Invoke();
    }

    // ── Правка черновика ──────────────────────────────────────────────────────────

    /// <summary>
    /// Записать значение поля в черновик либо удалить ключ. Значение, совпавшее
    /// с унаследованным, локальным ключом не становится: файл не должен содержать копий
    /// родительских чисел.
    /// </summary>
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
            string encoded = ContentValueReader.Encode(field, value);
            if (encoded == null)
                return;

            string without = TomlPatchWriter.RemoveKey(session.DraftText, section, field.Key);
            var texts = DraftMap();
            texts[session.Path] = without;

            session.DraftText =
                ContentProvenance.InheritedEquals(session.Path, section, field.Key, encoded, texts)
                    ? without
                    : TomlPatchWriter.SetKey(session.DraftText, section, field.Key, encoded);
        }

        CommitDraft(session, prior);
    }

    /// <summary>Значение ключа внутри блока массива таблиц.</summary>
    public object ArrayItemValue(
        OpenEntitySession session, string array, int index, ContentFieldSpec field) =>
        session == null || field == null
            ? null
            : WaveDraftReader.ArrayItemValue(session.DraftText, array, index, field.Key);

    /// <summary>Записан ли ключ в этом блоке массива таблиц.</summary>
    public bool HasArrayItemValue(
        OpenEntitySession session, string array, int index, ContentFieldSpec field) =>
        ArrayItemValue(session, array, index, field) != null;

    /// <summary>Записать либо удалить ключ внутри блока массива таблиц.</summary>
    public void SetArrayItemField(
        OpenEntitySession session, string array, int index,
        ContentFieldSpec field, object value, bool clear)
    {
        if (session == null || field == null)
            return;

        string prior = session.DraftText;
        if (clear)
        {
            session.DraftText = TomlPatchWriter.RemoveArrayItemKey(
                session.DraftText, array, index, field.Key);
        }
        else
        {
            string encoded = ContentValueReader.Encode(field, value);
            if (encoded == null)
                return;

            session.DraftText = TomlPatchWriter.SetArrayItemKey(
                session.DraftText, array, index, field.Key, encoded);
        }

        CommitDraft(session, prior);
    }

    /// <summary>Добавить блок массива таблиц с начальными ключами.</summary>
    public void AddArrayItem(
        OpenEntitySession session, string array, IReadOnlyList<(string Key, string Value)> initial)
    {
        if (session == null)
            return;

        string prior = session.DraftText;
        session.DraftText = TomlPatchWriter.AddArrayItem(session.DraftText, array, initial);
        CommitDraft(session, prior);
    }

    /// <summary>Удалить блок массива таблиц вместе с относящимся к нему пояснением.</summary>
    public void RemoveArrayItem(OpenEntitySession session, string array, int index)
    {
        if (session == null)
            return;

        string prior = session.DraftText;
        session.DraftText = TomlPatchWriter.RemoveArrayItem(session.DraftText, array, index);
        CommitDraft(session, prior);
    }

    /// <summary>
    /// Общий хвост правки черновика: отметить, пересобрать и оповестить. Правка, не
    /// изменившая текст, ничего не запускает: <c>TextSubmitted</c> и <c>FocusExited</c>
    /// приходят для одного и того же поля подряд.
    /// </summary>
    private void CommitDraft(OpenEntitySession session, string prior)
    {
        if (string.Equals(prior, session.DraftText, StringComparison.Ordinal))
            return;

        session.MarkDirty();
        Recompile();
        Changed?.Invoke();
    }

    /// <summary>
    /// Записать черновик на диск. Возвращает описание отказа либо <c>null</c> при успехе.
    /// Запись идёт через временный файл: прерывание не должно оставить половину файла.
    /// </summary>
    public string Apply(OpenEntitySession session)
    {
        if (session == null)
            return "no open entity";

        ulong stamp = ContentFileIndex.Stamp(session.Path);
        if (stamp != 0 && session.LoadedStamp != 0 && stamp != session.LoadedStamp)
            return "file changed externally; reload from disk";

        var probe = new Catalog();
        int errors = ContentCompiler.CompileWithOverrides(probe, DraftMap());
        if (errors > 0)
            return $"compile failed: {errors} errors";

        string absolute = ProjectSettings.GlobalizePath(session.Path);
        string temp = absolute + ".tmp";

        try
        {
            File.WriteAllText(temp, session.DraftText);
            File.Move(temp, absolute, overwrite: true);
        }
        catch (Exception ex)
        {
            return $"write failed: {ex.Message}";
        }

        ulong saved = ContentFileIndex.Stamp(session.Path);
        _index.Remember(session.Path, session.DraftText, saved);
        session.AcceptSaved(saved);

        Content.Reload();
        Recompile();
        Changed?.Invoke();
        return null;
    }

    /// <summary>Вернуть черновик к последнему известному тексту файла.</summary>
    public void Revert(OpenEntitySession session)
    {
        if (session == null || _index.Text(session.Path) is not { } text)
            return;

        session.Revert(text, _index.KnownStamp(session.Path));
        Recompile();
        Changed?.Invoke();
    }

    // ── Чтение значений ───────────────────────────────────────────────────────────

    /// <summary>Происхождение значения поля: локальное, унаследованное, из vars или умолчание.</summary>
    public FieldSourceInfo FieldSource(OpenEntitySession session, ContentFieldSpec field)
    {
        if (session == null || field == null)
            return new FieldSourceInfo { Provenance = FieldProvenance.Missing };

        return ContentProvenance.Of(
            session.Path,
            session.DraftText,
            field.RootOnly ? null : field.Section,
            field.Key,
            DraftMap());
    }

    /// <summary>
    /// Есть ли ключ непосредственно в черновике. Происхождение Vars на этот вопрос
    /// не отвечает: ссылка на файл переменных может быть и локальной, и унаследованной.
    /// </summary>
    public bool HasLocalValue(OpenEntitySession session, ContentFieldSpec field) =>
        session != null
        && field != null
        && TomlText.HasKey(session.DraftText, field.RootOnly ? null : field.Section, field.Key);

    /// <summary>Действующее значение поля: итог наследования, подстановки и умолчаний кода.</summary>
    public object EffectiveValue(OpenEntitySession session, ContentFieldSpec field)
    {
        if (session == null)
            return null;

        return session.Kind switch
        {
            ContentEntityKind.Wave =>
                ContentValueReader.Wave(Catalog.Wave(session.Id), session.DraftText, field),
            ContentEntityKind.Weapon or ContentEntityKind.WorkTool =>
                ContentValueReader.Tool(Catalog.Tool(session.Id), field),
            _ => ContentValueReader.Unit(Catalog.Unit(session.Id), field, Catalog.Tags),
        };
    }

    /// <summary>Собранное определение юнита либо постройки с учётом черновиков.</summary>
    public UnitDefinition PreviewUnit(string id) => Catalog.Unit(id);

    /// <summary>Собранное определение оружия либо рабочего инструмента с учётом черновиков.</summary>
    public ToolDefinition PreviewTool(string id) => Catalog.Tool(id);

    /// <summary>Содержимое файла переменных для контекстной панели активной сущности.</summary>
    public VarsPanelData VarsPanel(string path)
    {
        var session = ActiveSession;
        if (session == null
            || string.IsNullOrEmpty(path)
            || !TomlResolver.IsVars(path)
            || _index.Text(path) is not { } text)
        {
            return null;
        }

        var table = TomlText.Parse(text);
        if (table == null)
            return null;

        var result = new VarsPanelData
        {
            ContextId = session.Id,
            Path = path,
            FileName = TomlText.FileName(path),
        };
        VarsPanelReader.Fill(result.Rows, null, table);
        return result;
    }

    /// <summary>Граф зависимостей активной сущности режима «Entities».</summary>
    public ContentGraphData ActiveContextGraph() =>
        new ContentGraphBuilder(_index, Catalog, DraftMap()).Build(ActiveSession);

    // ── Волны ─────────────────────────────────────────────────────────────────────

    /// <summary>Виды противника, пригодные для списков волны.</summary>
    public IReadOnlyList<string> EnemyUnitIds() =>
        Catalog.Units
            .Where(unit => unit.Class == UnitClass.Enemy && !string.IsNullOrEmpty(unit.Id))
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .Select(unit => unit.Id)
            .ToList();

    /// <summary>Отображаемое имя вида либо его идентификатор, если имени нет.</summary>
    public string UnitDisplayName(string id) =>
        Catalog.Unit(id) is { } unit && !string.IsNullOrEmpty(unit.DisplayName)
            ? unit.DisplayName
            : id;

    /// <summary>Блоки <c>[[unit_list]]</c> черновика волны в порядке файла.</summary>
    public IReadOnlyList<WaveUnitListView> WaveLists(OpenEntitySession session) =>
        session == null
            ? new List<WaveUnitListView>()
            : WaveDraftReader.UnitLists(session.DraftText);

    /// <summary>Обзор волны по идентификатору.</summary>
    public WaveOverview WaveOverview(string id) =>
        id == null ? null : _waveOverviews.FirstOrDefault(wave => wave.Id == id);

    // ── Измерительное поле ────────────────────────────────────────────────────────

    /// <summary>Расставить открытые сущности рядами по их действительным габаритам.</summary>
    public void LayoutOpenSessions()
    {
        float x = 0f;
        float y = 0f;
        float rowHeight = 0f;
        const float gap = Const.Unit * 2.5f;
        const float rowWidth = Const.Unit * 14f;

        foreach (var session in SessionsIn(ContentEditorScope.Entities)
                     .OrderBy(s => s.Id, StringComparer.Ordinal))
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

    /// <summary>
    /// Поставить впервые открытую сущность правее всех уже открытых с учётом их габарита.
    /// Постоянный шаг приводил к наложению крупных силуэтов и подписей.
    /// </summary>
    private void AutoPlace(OpenEntitySession session)
    {
        float left = 0f;
        foreach (var open in SessionsIn(ContentEditorScope.Entities))
            left = Mathf.Max(left, open.FieldPosition.X + HalfWidth(open.Id) + Const.Unit * 2.5f);

        session.FieldPosition = new Vector2(left + HalfWidth(session.Id), 0f);
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

    // ── Сборка ────────────────────────────────────────────────────────────────────

    /// <summary>Тексты всех файлов с подстановкой черновиков открытых вкладок.</summary>
    public Dictionary<string, string> DraftMap()
    {
        var map = new Dictionary<string, string>(_index.Texts, StringComparer.Ordinal);
        foreach (var session in _sessions.Values)
            map[session.Path] = session.DraftText;
        return map;
    }

    /// <summary>
    /// Пересобрать каталог по черновикам и согласовать с ним индекс и открытые вкладки.
    /// Порядок важен: вид записи известен только после сборки, а сессия обращается
    /// к записи за именем и видом.
    /// </summary>
    private void Recompile()
    {
        var texts = DraftMap();
        var catalog = new Catalog();
        ContentCompiler.CompileWithOverrides(catalog, texts);
        Catalog = catalog;

        _index.RebuildEntries(texts);
        _index.Reclassify(catalog);
        ReconcileOpenSessions();
        _waveOverviews = WaveDraftReader.Overviews(_index.Entries, texts, catalog);
    }

    /// <summary>
    /// Согласовать сессии с обновлёнными записями: правка ключа <c>id</c> меняет ключ
    /// сессии, а исчезнувшая запись означает закрытие вкладки.
    /// </summary>
    private void ReconcileOpenSessions()
    {
        var sessions = _sessions.Values.ToList();
        var previousActive = new Dictionary<ContentEditorScope, string>(_activeByScope);
        _sessions.Clear();

        foreach (var session in sessions)
        {
            var entry = _index.ByPath(session.Path);
            if (entry == null)
                continue;

            string oldId = session.Id;
            session.UpdateEntry(entry);
            _sessions[session.Id] = session;

            foreach (var pair in previousActive.ToList())
            {
                if (pair.Value == oldId)
                    previousActive[pair.Key] = session.Id;
            }
        }

        _activeByScope.Clear();
        foreach (var pair in previousActive)
        {
            if (_sessions.TryGetValue(pair.Value ?? "", out var session) && session.Scope == pair.Key)
                _activeByScope[pair.Key] = pair.Value;
        }

        foreach (ContentEditorScope scope in Enum.GetValues<ContentEditorScope>())
        {
            if (!_activeByScope.ContainsKey(scope) && SessionsIn(scope).LastOrDefault() is { } fallback)
                _activeByScope[scope] = fallback.Id;
        }
    }
}
