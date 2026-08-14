using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

/// <summary>Чем правится поле настроечного ресурса.</summary>
public enum TuningFieldKind
{
    Float,
    Int,
    Bool,
    String,
    Color,

    /// <summary>
    /// Кривая, вложенный ресурс или массив. Такие поля правятся мышью в инспекторе Godot,
    /// и повторять его здесь незачем: на плитке остаётся сводка и переход.
    /// </summary>
    Complex,
}

/// <summary>Одно поле настроечного ресурса, приведённое к виду, пригодному для формы.</summary>
public sealed class TuningField
{
    /// <summary>Имя свойства, как его знает движок.</summary>
    public string Name { get; init; }

    /// <summary>Подпись для человека: имя свойства, разделённое по словам.</summary>
    public string Label { get; init; }

    /// <summary>Группа <c>[ExportGroup]</c>, в которой объявлено поле; пусто — вне групп.</summary>
    public string Group { get; init; }

    /// <summary>Пояснение из комментария исходника. Показывается подсказкой.</summary>
    public string Hint { get; init; }

    public TuningFieldKind Kind { get; init; }

    /// <summary>Значение, прочитанное с диска. По нему определяется наличие правки.</summary>
    public Variant Original { get; init; }

    /// <summary>Краткое описание значения для полей вида <see cref="TuningFieldKind.Complex"/>.</summary>
    public string Summary { get; init; }
}

/// <summary>
/// Черновик настроечного ресурса: прочитанные поля, правки в памяти и запись на диск.
///
/// ПОЧЕМУ ЧЕРНОВИК, А НЕ ПРЯМАЯ ПРАВКА РЕСУРСА. Инспектор Godot пишет значение в ресурс
/// сразу, и ошибочное движение ползунка немедленно меняет файл. Здесь принят тот же
/// порядок, что и для остального содержимого редактора: правка живёт в памяти, плитка
/// помечается несохранённой, а запись выполняется по явному действию. Отсюда же
/// возможность отменить правку целиком.
///
/// ДВА СПОСОБА ЗАПИСИ. «Применить» переписывает действующий файл. «Сохранить как» создаёт
/// отдельный вариант и переводит на него сцену партии, оставляя прежний файл нетронутым:
/// подбор баланса обычно ведётся сравнением нескольких наборов чисел, а не правкой
/// единственного.
/// </summary>
public sealed class TuningResourceDraft
{
    private readonly List<TuningField> _fields = new();
    private readonly System.Collections.Generic.Dictionary<string, Variant> _edited = new(StringComparer.Ordinal);

    /// <summary>Описание ресурса из каталога страницы.</summary>
    public TuningResourceSpec Spec { get; }

    /// <summary>Путь действующего варианта: он же назначен сцене партии.</summary>
    public string Path { get; private set; }

    /// <summary>Загруженный ресурс. Правки на него не наносятся до записи.</summary>
    public Resource Resource { get; private set; }

    /// <summary>Поля в порядке объявления, вместе с разделителями групп.</summary>
    public IReadOnlyList<TuningField> Fields => _fields;

    /// <summary>Причина, по которой ресурс не прочитан. Пусто — прочитан.</summary>
    public string LoadError { get; private set; }

    /// <summary>Есть ли несохранённые правки.</summary>
    public bool Dirty => _edited.Count > 0;

    /// <summary>Правится ли вариант, а не файл, назначенный проекту изначально.</summary>
    public bool IsVariant =>
        Path != null && !string.Equals(Path, Spec.DefaultPath, StringComparison.Ordinal);

    public TuningResourceDraft(TuningResourceSpec spec)
    {
        Spec = spec;
        Reload();
    }

    /// <summary>Перечитать ресурс с диска и забыть правки.</summary>
    public void Reload()
    {
        _fields.Clear();
        _edited.Clear();
        LoadError = null;
        Path = TuningCatalog.ActivePath(Spec);

        try
        {
            Resource = ResourceLoader.Exists(Path) ? ResourceLoader.Load<Resource>(Path) : null;
        }
        catch (Exception ex)
        {
            Resource = null;
            LoadError = ex.Message;
        }

        if (Resource == null)
        {
            LoadError ??= $"resource {Path} was not read";
            return;
        }

        ReadFields();
    }

    /// <summary>Действующее значение поля: правка, если она есть, иначе значение с диска.</summary>
    public Variant Value(TuningField field) =>
        field != null && _edited.TryGetValue(field.Name, out Variant edited) ? edited : field?.Original ?? default;

    /// <summary>Изменено ли поле относительно диска.</summary>
    public bool IsEdited(TuningField field) => field != null && _edited.ContainsKey(field.Name);

    /// <summary>
    /// Записать значение в черновик. Значение, совпавшее с прочитанным, правкой не считается:
    /// иначе плитка помечалась бы несохранённой после простого перехода по полям.
    /// </summary>
    public void Set(TuningField field, Variant value)
    {
        if (field == null)
            return;

        if (Same(field.Original, value, field.Kind))
            _edited.Remove(field.Name);
        else
            _edited[field.Name] = value;
    }

    /// <summary>Отменить правку одного поля.</summary>
    public void Reset(TuningField field)
    {
        if (field != null)
            _edited.Remove(field.Name);
    }

    /// <summary>Отменить все правки, не обращаясь к диску.</summary>
    public void Revert() => _edited.Clear();

    /// <summary>
    /// Записать правки в действующий файл. Возвращает описание отказа либо <c>null</c>.
    /// </summary>
    public string Apply() => WriteTo(Path);

    /// <summary>
    /// Сохранить правки отдельным вариантом и назначить его сцене партии.
    /// <paramref name="name"/> — имя файла без расширения.
    /// </summary>
    public string SaveAsVariant(string name)
    {
        if (Resource == null)
            return "nothing to save";

        string safe = SafeFileName(name);
        if (safe.Length == 0)
            return "the variant needs a name";

        string path = Spec.Directory + safe + ".tres";
        if (string.Equals(path, Path, StringComparison.Ordinal))
            return "this name belongs to the resource being edited";

        string error = WriteTo(path);
        if (error != null)
            return error;

        // Файл должен быть замечен движком до того, как на него сошлётся сцена: иначе
        // ссылка укажет на ещё не импортированный ресурс.
        EditorInterface.Singleton?.GetResourceFilesystem()?.Scan();

        error = TuningSceneBinding.Rebind(Spec, path);
        if (error != null)
            return error;

        Path = path;
        Reload();
        return null;
    }

    /// <summary>Вернуть партию к ресурсу, назначенному изначально.</summary>
    public string UseDefault()
    {
        if (!IsVariant)
            return null;

        string error = TuningSceneBinding.Rebind(Spec, Spec.DefaultPath);
        if (error != null)
            return error;

        Reload();
        return null;
    }

    // ── Снимок рабочего места ─────────────────────────────────────────────────────

    /// <summary>Правки строками «имя=значение»: снимок переживает перезагрузку сборки.</summary>
    public string[] CaptureEdits() =>
        _edited
            .Select(pair => $"{pair.Key}={Text(pair.Value, KindOf(pair.Key))}")
            .ToArray();

    /// <summary>Восстановить правки из снимка. Поля, которых больше нет, пропускаются.</summary>
    public void RestoreEdits(string[] edits)
    {
        foreach (string entry in edits ?? Array.Empty<string>())
        {
            int split = entry.IndexOf('=');
            if (split <= 0)
                continue;

            var field = _fields.FirstOrDefault(candidate =>
                candidate.Name == entry[..split] && candidate.Kind != TuningFieldKind.Complex);
            if (field == null)
                continue;

            if (Parse(entry[(split + 1)..], field.Kind) is { } value)
                Set(field, value);
        }
    }

    // ── Чтение и запись ───────────────────────────────────────────────────────────

    /// <summary>
    /// Разобрать список свойств движка. Порядок сохраняется: он совпадает с порядком
    /// объявления в исходнике и потому осмыслен, а разделители групп дают заголовки.
    /// </summary>
    private void ReadFields()
    {
        var hints = TuningDocReader.For(Resource);
        string group = "";

        foreach (Godot.Collections.Dictionary property in Resource.GetPropertyList())
        {
            string name = property["name"].AsString();
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();

            if (usage.HasFlag(PropertyUsageFlags.Group))
            {
                group = name;
                continue;
            }

            if (usage.HasFlag(PropertyUsageFlags.Category)
                || usage.HasFlag(PropertyUsageFlags.Subgroup))
            {
                continue;
            }

            // Правятся только свойства, объявленные скриптом: служебные свойства Resource
            // к настройке отношения не имеют.
            if (!usage.HasFlag(PropertyUsageFlags.ScriptVariable)
                || !usage.HasFlag(PropertyUsageFlags.Editor))
            {
                continue;
            }

            Variant value = Resource.Get(name);
            var kind = KindOf((Variant.Type)property["type"].AsInt64());
            _fields.Add(new TuningField
            {
                Name = name,
                Label = Humanize(name),
                Group = group,
                Hint = hints.TryGetValue(name, out string hint) ? hint : "",
                Kind = kind,
                Original = value,
                Summary = kind == TuningFieldKind.Complex ? Describe(value) : null,
            });
        }
    }

    /// <summary>
    /// Записать копию ресурса с наложенными правками. Копия нужна затем, что загруженный
    /// экземпляр разделяется с инспектором Godot: правка черновика не должна проявляться
    /// в нём до записи.
    /// </summary>
    private string WriteTo(string path)
    {
        if (Resource == null)
            return LoadError ?? "nothing to save";

        try
        {
            var copy = (Resource)Resource.Duplicate(true);
            foreach (var pair in _edited)
                copy.Set(pair.Key, pair.Value);

            // Путь ресурса задаётся до записи: сохранение под прежним путём иначе
            // перезаписало бы исходный файл вместо нового варианта.
            copy.ResourcePath = path;
            copy.TakeOverPath(path);

            Error saved = ResourceSaver.Save(copy, path);
            if (saved != Error.Ok)
                return $"save failed: {saved}";
        }
        catch (Exception ex)
        {
            return $"save failed: {ex.Message}";
        }

        if (string.Equals(path, Path, StringComparison.Ordinal))
        {
            // Записанное стало новым состоянием диска: правки больше не отличаются от него.
            Reload();
        }

        return null;
    }

    private TuningFieldKind KindOf(string name) =>
        _fields.FirstOrDefault(field => field.Name == name)?.Kind ?? TuningFieldKind.String;

    private static TuningFieldKind KindOf(Variant.Type type) => type switch
    {
        Variant.Type.Float => TuningFieldKind.Float,
        Variant.Type.Int => TuningFieldKind.Int,
        Variant.Type.Bool => TuningFieldKind.Bool,
        Variant.Type.String or Variant.Type.StringName => TuningFieldKind.String,
        Variant.Type.Color => TuningFieldKind.Color,
        _ => TuningFieldKind.Complex,
    };

    /// <summary>Краткое описание значения, которое не правится на плитке.</summary>
    private static string Describe(Variant value)
    {
        switch (value.VariantType)
        {
            case Variant.Type.Nil:
                return "not assigned";

            case Variant.Type.Object:
                var resource = value.As<Resource>();
                if (resource == null)
                    return "not assigned";
                if (resource is Curve curve)
                    return $"curve · {curve.PointCount} points";
                return string.IsNullOrEmpty(resource.ResourcePath)
                    ? resource.GetType().Name
                    : $"{resource.GetType().Name} · {TomlText.FileName(resource.ResourcePath)}";

            case Variant.Type.Array:
                return $"array · {value.AsGodotArray().Count} items";

            default:
                return value.ToString();
        }
    }

    /// <summary>Имя свойства словами: <c>ProductionTail</c> становится «Production tail».</summary>
    private static string Humanize(string name)
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(c));
                continue;
            }

            text.Append(c);
        }

        return text.ToString();
    }

    /// <summary>Имя файла без разделителей пути и пробелов: вариант сохраняется рядом.</summary>
    private static string SafeFileName(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (char c in (name ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                text.Append(c);
            else if (char.IsWhiteSpace(c))
                text.Append('_');
        }

        return text.ToString();
    }

    private static bool Same(Variant left, Variant right, TuningFieldKind kind) => kind switch
    {
        TuningFieldKind.Float => Mathf.IsEqualApprox(left.AsSingle(), right.AsSingle()),
        TuningFieldKind.Int => left.AsInt64() == right.AsInt64(),
        TuningFieldKind.Bool => left.AsBool() == right.AsBool(),
        TuningFieldKind.Color => left.AsColor() == right.AsColor(),
        _ => string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal),
    };

    /// <summary>Значение строкой в инвариантной культуре: снимок читается на любой машине.</summary>
    private static string Text(Variant value, TuningFieldKind kind) => kind switch
    {
        TuningFieldKind.Float => value.AsSingle().ToString("R", CultureInfo.InvariantCulture),
        TuningFieldKind.Int => value.AsInt64().ToString(CultureInfo.InvariantCulture),
        TuningFieldKind.Bool => value.AsBool() ? "true" : "false",
        TuningFieldKind.Color => value.AsColor().ToHtml(true),
        _ => value.AsString(),
    };

    private static Variant? Parse(string text, TuningFieldKind kind)
    {
        switch (kind)
        {
            case TuningFieldKind.Float:
                return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                    ? number
                    : null;

            case TuningFieldKind.Int:
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
                    ? integer
                    : null;

            case TuningFieldKind.Bool:
                return text == "true";

            case TuningFieldKind.Color:
                return Color.FromString(text, Colors.White);

            case TuningFieldKind.String:
                return text;

            default:
                return null;
        }
    }

    /// <summary>Существует ли файл варианта с таким именем: перезапись требует подтверждения.</summary>
    public bool VariantExists(string name)
    {
        string safe = SafeFileName(name);
        return safe.Length > 0
               && File.Exists(ProjectSettings.GlobalizePath(Spec.Directory + safe + ".tres"));
    }
}
