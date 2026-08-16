using System;
using Godot;

/// <summary>
/// Открытая вкладка сущности. Черновик живёт здесь до «Применить» или «Отменить».
/// Положение на общем поле и флаг показа принадлежат вкладке, а не определению в Catalog.
/// </summary>
public sealed class OpenEntitySession
{
    public string Id { get; private set; }

    /// <summary>Путь файла. Неизменен: смена пути означала бы другую сессию.</summary>
    public string Path { get; }

    public string FileName { get; private set; }
    public string DisplayName { get; private set; }
    public ContentEntityKind Kind { get; private set; }

    /// <summary>Область редактора, в перечне вкладок которой показывается эта сессия.</summary>
    public ContentEditorScope Scope =>
        Kind == ContentEntityKind.Wave ? ContentEditorScope.Waves : ContentEditorScope.Entities;

    /// <summary>Рабочий текст файла. Правки формы меняют только его.</summary>
    public string DraftText { get; set; }

    /// <summary>
    /// Метка времени файла на момент последней синхронизации с диском.
    /// Сравнение с текущей меткой обнаруживает внешнюю правку.
    /// </summary>
    public ulong LoadedStamp { get; private set; }

    /// <summary>Отличается ли черновик от последнего известного текста на диске.</summary>
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

    /// <summary>
    /// Согласовать описание сессии с обновлённой записью каталога: <c>id</c> и имя могут
    /// измениться правкой самого файла. Запись другого файла отвергается.
    /// </summary>
    public void UpdateEntry(ContentEditorEntry entry)
    {
        if (entry == null || !string.Equals(entry.Path, Path, StringComparison.Ordinal))
            return;

        Id = entry.Id;
        FileName = entry.FileName;
        DisplayName = entry.DisplayName;
        Kind = entry.Kind;
    }

    /// <summary>Восстановить черновик и положение из снимка рабочего пространства.</summary>
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

    /// <summary>Черновик записан на диск: он же стал последним известным текстом файла.</summary>
    public void AcceptSaved(ulong stamp)
    {
        Dirty = false;
        LoadedStamp = stamp;
    }

    /// <summary>Вернуть черновик к тексту с диска.</summary>
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

    /// <summary>Подпись вкладки: имя файла, отображаемое имя и признак несохранённой правки.</summary>
    public string TabTitle =>
        Dirty
            ? $"{FileName} ({DisplayName}) *"
            : $"{FileName} ({DisplayName})";
}
