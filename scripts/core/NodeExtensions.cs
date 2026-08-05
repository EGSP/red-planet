using Godot;

/// <summary>Общие приёмы работы с деревом сцены.</summary>
public static class NodeExtensions
{
    /// <summary>
    /// Ближайший предок нужного рода. Так узел находит владельца своей ветки, не завися
    /// ни от глубины вложенности, ни от статических ссылок: система ищет композиционный
    /// корень, элемент интерфейса — сессию.
    /// </summary>
    public static T Ancestor<T>(this Node node) where T : Node
    {
        for (var current = node?.GetParent(); current != null; current = current.GetParent())
            if (current is T found)
                return found;

        return null;
    }

    /// <summary>
    /// Соседний узел нужного рода — под тем же родителем. Нужен элементам интерфейса,
    /// которые встают друг над другом: ярус стопки обязан знать высоту нижнего яруса,
    /// но привязываться к имени узла в сцене не должен.
    /// </summary>
    public static T Sibling<T>(this Node node) where T : Node
    {
        var parent = node?.GetParent();

        if (parent == null)
            return null;

        foreach (var child in parent.GetChildren())
            if (child is T found && !ReferenceEquals(found, node))
                return found;

        return null;
    }
}
