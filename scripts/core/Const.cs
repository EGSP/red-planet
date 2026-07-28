using Godot;

/// <summary>
/// Единицы измерения и мировые константы.
/// 1 unit = 64 px. Все размеры сущностей кратны юниту.
/// </summary>
public static class Const
{
    public const int Unit = 64;

    /// <summary>Мир — квадрат 41x41 клетка с центром в (0,0).</summary>
    public const int WorldRadiusCells = 20;

    // Месторождения появляются в кольце вокруг базы
    public const int OreRingMin = 6;
    public const int OreRingMax = 14;
    public const int OreSlots = 12;
    public const float OreRespawnDelay = 8f;

    /// <summary>Сколько метала можно выкопать из одного месторождения.</summary>
    public const float OreDepositAmount = 200f;

    /// <summary>На каком расстоянии бот держится от коммандера, когда идёт следом.</summary>
    public static float FollowDistancePx => Unit * 3f;

    // Запас без единой постройки. Дальше потолок поднимают сами постройки (MetalStorage
    // и EnergyStorage в справочнике) — запас нужен как демпфер, иначе любой всплеск
    // спроса мгновенно роняет производительность базы
    public const float BaseMetalCapacity = 150f;
    public const float BaseEnergyCapacity = 150f;

    // Враги приходят из-за кольца руды: дальние месторождения лежат на линии их подхода
    public const float EnemyRingFactor = 1.3f;

    // Держим давление таким, чтобы коммандер в одиночку успевал отстреливаться:
    // своих турелей у игрока пока нет, и толпа сносит базу за полминуты
    public const int EnemyPopulation = 4;
    public const float EnemySpawnDelay = 10f;
    public const float EnemyFirstDelay = 30f;

    /// <summary>Как часто враг переигрывает выбор цели, секунд.</summary>
    public const float EnemyRetargetDelay = 1.5f;

    /// <summary>
    /// Доля прочности готовой постройки, которая есть у её каркаса.
    /// Недостроенное ломается заметно легче — стройка под обстрелом должна быть рискованной.
    /// </summary>
    public const float BlueprintHealthFactor = 0.4f;

    /// <summary>Радиус окружности появления врагов в пикселях.</summary>
    public static float EnemySpawnRadiusPx => OreRingMax * EnemyRingFactor * Unit;

    public static Vector2 CellCorner(Vector2I cell) => new(cell.X * Unit, cell.Y * Unit);

    public static Vector2 CellCenter(Vector2I cell) =>
        new(cell.X * Unit + Unit * 0.5f, cell.Y * Unit + Unit * 0.5f);

    public static Vector2I WorldToCell(Vector2 pos) =>
        new(Mathf.FloorToInt(pos.X / Unit), Mathf.FloorToInt(pos.Y / Unit));

    /// <summary>Центр области, занимаемой постройкой размера size с началом в origin.</summary>
    public static Vector2 AreaCenter(Vector2I origin, Vector2I size) =>
        CellCorner(origin) + new Vector2(size.X, size.Y) * Unit * 0.5f;
}
