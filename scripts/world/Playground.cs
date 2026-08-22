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

    /// <summary>
    /// Тень препятствий. Лежит на земле и на месторождениях, но под тем, что тень отбрасывает,
    /// и под всем, что ходит: юнит закрывается тенью ни при каких настройках.
    /// </summary>
    Shadows = 2,

    /// <summary>
    /// Наземные эффекты: пыль из-под корпуса, следы, копоть. Лежат на земле и на тени,
    /// но под всем, что по земле ходит и стоит, поскольку поднимаются они от грунта,
    /// а не над машиной.
    /// </summary>
    GroundEffects = 3,

    /// <summary>Постройки и каркасы.</summary>
    Structures = 4,

    /// <summary>Всё, что ходит: юниты игрока и враги.</summary>
    Actors = 5,

    Projectiles = 6,

    /// <summary>
    /// Облака и их тень. Лежат поверх всего, что стоит и ходит по земле, поскольку тень
    /// падает сверху, но под туманом войны и служебной графикой.
    /// </summary>
    Clouds = 7,

    /// <summary>
    /// Туман войны. Лежит поверх мира, но под служебной графикой: приказы, призрак постройки
    /// и отладочные выкладки закрываться туманом не должны — они принадлежат не миру,
    /// а тому, кто на мир смотрит.
    /// </summary>
    Fog = 8,

    /// <summary>
    /// Служебная графика поверх мира: призрак постройки, отрисовка приказов, отладочные
    /// выкладки. Миру не принадлежит и потому эффектом не является — эффекты мира лежат
    /// в <see cref="GroundEffects"/>.
    /// </summary>
    Overlay = 9,
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
    [Export] public Node2D Shadows;
    [Export] public Node2D GroundEffects;
    [Export] public Node2D Structures;
    [Export] public Node2D Actors;
    [Export] public Node2D Projectiles;
    [Export] public Node2D Clouds;
    [Export] public Node2D Fog;
    [Export] public Node2D Overlay;

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
        Shadows ??= GetNodeOrNull<Node2D>(nameof(Shadows));
        GroundEffects ??= GetNodeOrNull<Node2D>(nameof(GroundEffects));
        Structures ??= GetNodeOrNull<Node2D>(nameof(Structures));
        Actors ??= GetNodeOrNull<Node2D>(nameof(Actors));
        Projectiles ??= GetNodeOrNull<Node2D>(nameof(Projectiles));
        Clouds ??= GetNodeOrNull<Node2D>(nameof(Clouds));
        Fog ??= GetNodeOrNull<Node2D>(nameof(Fog));
        Overlay ??= GetNodeOrNull<Node2D>(nameof(Overlay));

        _layers = new[]
        {
            Terrain, Deposits, Shadows, GroundEffects, Structures, Actors, Projectiles, Clouds, Fog, Overlay,
        };
    }
}
