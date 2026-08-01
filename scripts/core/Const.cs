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

    // ── Точки метала ──────────────────────────────────────────────────────────────
    //
    // Расставляются один раз при создании мира и больше не меняются: точка вечна,
    // и восстанавливать в ней нечего. Все точки одинаковы — богатство числом не задаётся.
    // Разницу между ними создаёт только положение: до дальней дольше идти и труднее
    // её прикрыть, и этого достаточно, чтобы выбор «куда расширяться» имел цену.

    /// <summary>Сколько точек метала на карте.</summary>
    public const int MetalSpotCount = 14;

    /// <summary>Дальше этого радиуса точек нет: у самой границы мира их не разместить.</summary>
    public const int MetalSpotFieldRadius = 15;

    /// <summary>Ближе этого радиуса точек нет — иначе они встанут прямо в базу.</summary>
    public const int MetalSpotBaseClearance = 4;

    /// <summary>Минимальное расстояние между точками, клеток: иначе они собираются в кучу.</summary>
    public const int MetalSpotSpacing = 3;

    // Сколько точек гарантированно лежит рядом со стартом. Без гарантии случайная раскладка
    // иногда оставляет начало вовсе без метала, и партия проиграна ещё до первого решения
    public const int MetalSpotStartCount = 3;
    public const int MetalSpotStartRadius = 8;

    /// <summary>На каком расстоянии бот держится от коммандера, когда идёт следом.</summary>
    public static float FollowDistancePx => Unit * 3f;

    // Запас без единой постройки. Дальше потолок поднимают сами постройки (MetalStorage
    // и EnergyStorage в справочнике) — запас нужен как демпфер, иначе любой всплеск
    // спроса мгновенно роняет производительность базы
    public const float BaseMetalCapacity = 150f;
    public const float BaseEnergyCapacity = 150f;

    // Враги приходят из-за поля точек: дальние точки лежат на линии их подхода,
    // и экстрактор на краю карты обороняется тяжелее, чем экстрактор у базы
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
    public static float EnemySpawnRadiusPx => MetalSpotFieldRadius * EnemyRingFactor * Unit;

    /// <summary>
    /// Точка высадки — центр стартовой клетки, вокруг которой встаёт база.
    ///
    /// От неё, а не от текущего центра базы, отсчитывается удалённость построек для террора.
    /// Центр базы смещается по мере роста, и тогда дальняя постройка со временем сама собой
    /// становилась бы ближней — то есть расширение удешевляло бы уже сделанное расширение.
    /// Точка высадки неподвижна, поэтому удалённость означает ровно то, что должна.
    /// </summary>
    public static Vector2 LandingPoint => CellCenter(Vector2I.Zero);

    public static Vector2 CellCorner(Vector2I cell) => new(cell.X * Unit, cell.Y * Unit);

    public static Vector2 CellCenter(Vector2I cell) =>
        new(cell.X * Unit + Unit * 0.5f, cell.Y * Unit + Unit * 0.5f);

    public static Vector2I WorldToCell(Vector2 pos) =>
        new(Mathf.FloorToInt(pos.X / Unit), Mathf.FloorToInt(pos.Y / Unit));

    /// <summary>Центр области, занимаемой постройкой размера size с началом в origin.</summary>
    public static Vector2 AreaCenter(Vector2I origin, Vector2I size) =>
        CellCorner(origin) + new Vector2(size.X, size.Y) * Unit * 0.5f;
}
