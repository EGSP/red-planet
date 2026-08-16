using System;
using Godot;

/// <summary>
/// Переход к ресурсу движка и его выбор из файловой системы проекта.
///
/// ЗАЧЕМ ОБЩЕЕ МЕСТО. К ресурсу ведут два разных пути — щелчок по узлу графа связей
/// и кнопка в строке поля, — и оба обязаны открывать его одинаково. Держать два вызова
/// <see cref="EditorInterface"/> в разных файлах означало бы, что различие между сценой
/// и прочими ресурсами придётся помнить в обоих.
/// </summary>
public static class EditorResourceLink
{
    /// <summary>
    /// Открыть ресурс его собственным редактором: сцену — в двумерном редакторе, прочее —
    /// инспектором. Отсутствующий файл пропускается молча: путь виден в самом поле,
    /// и сообщение о нём ничего не добавит.
    /// </summary>
    public static void Open(string path)
    {
        if (string.IsNullOrEmpty(path) || !FileAccess.FileExists(path))
            return;

        var editor = EditorInterface.Singleton;
        editor.SelectFile(path);

        if (path.EndsWith(".tscn", StringComparison.Ordinal))
            editor.OpenSceneFromPath(path);
        else if (ResourceLoader.Load(path) is { } resource)
            editor.EditResource(resource);
    }

    /// <summary>
    /// Выбрать ресурс диалогом файловой системы проекта. <paramref name="filters"/> задаются
    /// в виде <c>"*.tscn ; Scenes"</c>; пустой набор показывает все файлы.
    ///
    /// Диалог создаётся на время выбора и освобождается по его завершении: держать его
    /// живым между вызовами значило бы хранить в дереве редактора узел ради одного нажатия.
    /// </summary>
    public static void Pick(string current, string[] filters, Action<string> chosen)
    {
        var baseControl = EditorInterface.Singleton?.GetBaseControl();

        if (!GodotObject.IsInstanceValid(baseControl))
            return;

        var dialog = new EditorFileDialog
        {
            FileMode = EditorFileDialog.FileModeEnum.OpenFile,
            Access = EditorFileDialog.AccessEnum.Resources,
            Title = "Выбор ресурса",
        };

        foreach (string filter in filters ?? Array.Empty<string>())
            dialog.AddFilter(filter);

        if (!string.IsNullOrEmpty(current) && FileAccess.FileExists(current))
            dialog.CurrentPath = current;

        dialog.FileSelected += path =>
        {
            chosen?.Invoke(path);
            dialog.QueueFree();
        };

        dialog.Canceled += dialog.QueueFree;

        baseControl.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(900, 620));
    }
}
