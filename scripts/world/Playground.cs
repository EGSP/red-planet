using Godot;

/// <summary>
/// Слои мира. Порядок объявления и есть порядок отрисовки: что ниже в списке — то выше
/// на экране. Раньше эту роль исполняли ZIndex, расставленные вручную в трёх разных файлах
/// (снаряд знал про шестёрку, призрак постройки — про десятку), и добавление нового рода
/// сущностей означало угадывание свободного числа.
/// </summary>
public enum WorldLayer
{
    /// <summary>Земля, сетка застройки, границы карты.</summary>
    Terrain = 0,

    /// <summary>Месторождения — лежат под всем, что на них ставят.</summary>
    Deposits = 1,

    /// <summary>Постройки и каркасы.</summary>
    Structures = 2,

    /// <summary>Всё, что ходит: юниты игрока и враги.</summary>
    Actors = 3,

    Projectiles = 4,

    /// <summary>
    /// Туман войны. Лежит поверх мира, но под служебной графикой: приказы, призрак постройки
    /// и отладочные выкладки закрываться туманом не должны — они принадлежат не миру,
    /// а тому, кто на мир смотрит.
    /// </summary>
    Fog = 5,

    /// <summary>Служебная графика поверх мира: призрак постройки, отрисовка приказов.</summary>
    Effects = 6,
}

/// <summary>
/// Игровая площадка — контейнер всего видимого и физического. Сюда и только сюда
/// Spawner складывает рождённые сущности, разводя их по слоям.
///
/// Логики здесь нет намеренно: площадка ничего не считает и ни на что не подписана,
/// она лишь даёт миру структуру. Тем самым она безболезненно переживает подмену —
/// если однажды понадобится вид без симуляции (реплей, клиент в сетевой игре),
/// заменяется ровно эта ветка дерева.
/// </summary>
public partial class Playground : Node2D
{
    [Export] public Node2D Terrain;
    [Export] public Node2D Deposits;
    [Export] public Node2D Structures;
    [Export] public Node2D Actors;
    [Export] public Node2D Projectiles;
    [Export] public Node2D Fog;
    [Export] public Node2D Effects;

    private Node2D[] _layers;

    public override void _EnterTree() => Resolve();

    /// <summary>
    /// Узел слоя. Если слой в сцене не завели, отдаём саму площадку: мир от этого
    /// потеряет порядок отрисовки, но не сущность — она всё равно окажется в дереве.
    /// </summary>
    public Node2D Layer(WorldLayer layer)
    {
        if (_layers == null)
            Resolve();

        return _layers[(int)layer] ?? this;
    }

    /// <summary>Положить сущность в мир, в свой слой.</summary>
    public T Add<T>(WorldLayer layer, T node) where T : Node
    {
        Layer(layer).AddChild(node);
        return node;
    }

    private void Resolve()
    {
        Terrain ??= GetNodeOrNull<Node2D>(nameof(Terrain));
        Deposits ??= GetNodeOrNull<Node2D>(nameof(Deposits));
        Structures ??= GetNodeOrNull<Node2D>(nameof(Structures));
        Actors ??= GetNodeOrNull<Node2D>(nameof(Actors));
        Projectiles ??= GetNodeOrNull<Node2D>(nameof(Projectiles));
        Fog ??= GetNodeOrNull<Node2D>(nameof(Fog));
        Effects ??= GetNodeOrNull<Node2D>(nameof(Effects));

        _layers = new[] { Terrain, Deposits, Structures, Actors, Projectiles, Fog, Effects };
    }
}
