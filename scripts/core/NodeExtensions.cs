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
}
