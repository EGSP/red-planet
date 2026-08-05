/// <summary>
/// Что показывать поверх мира. Панель пишет, отрисовщики читают.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ ПАНЕЛИ. Отрисовщик живёт в слоях мира, панель — в дереве интерфейса,
/// и связывать их ссылкой значило бы, что отрисовка перестаёт работать, стоит панели
/// не оказаться в сцене. Набор признаков разрывает эту зависимость: отрисовщику довольно
/// прочитать поле, а кто его выставил — не его дело.
///
/// Состояние статическое и сессию переживает намеренно: включённая отладка не должна
/// гаснуть от перезапуска партии, иначе разбор повторяющегося случая начинается
/// с расстановки галочек заново.
/// </summary>
public static class DebugFlags
{
    // ── навигация ─────────────────────────────────────────────────────────────────

    /// <summary>Непроходимые ячейки растра.</summary>
    public static bool NavBlocked;

    /// <summary>Клиренс градиентом: чем светлее, тем дальше до ближайшего препятствия.</summary>
    public static bool NavClearance;

    /// <summary>Связные области цветом. Разные цвета — пути между ними нет.</summary>
    public static bool NavComponents;

    /// <summary>Прямоугольники строений и обязательные зазоры вокруг них.</summary>
    public static bool Footprints;

    // ── пути ──────────────────────────────────────────────────────────────────────

    /// <summary>Ломаные выделенных.</summary>
    public static bool Paths;

    /// <summary>Пути всех подвижных, а не только выделенных.</summary>
    public static bool PathsAll;

    /// <summary>Узлы, раскрытые последним поиском. Заполнение стоит памяти — только по спросу.</summary>
    public static bool PathsExpanded;

    // ── boids ─────────────────────────────────────────────────────────────────────

    /// <summary>Векторы сил. Длина пропорциональна величине.</summary>
    public static bool BoidForces;

    /// <summary>Радиусы: физический корпус и радиус чутья.</summary>
    public static bool BoidRadii;

    /// <summary>Линии к соседям, которых сущность учитывает.</summary>
    public static bool BoidNeighbours;

    /// <summary>Ячейки раскладки соседей.</summary>
    public static bool BoidCells;

    // ── замеры ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Измерять время шага систем. Признак читает планировщик; при снятом признаке
    /// вызов систем идёт без всякой обёртки, а накопленные ряды сбрасываются.
    /// </summary>
    public static bool Profile;

    /// <summary>Рисовать ли хоть что-нибудь из навигационного растра.</summary>
    public static bool AnyNav => NavBlocked || NavClearance || NavComponents || Footprints;

    public static bool AnyPath => Paths || PathsAll || PathsExpanded;

    public static bool AnyBoid => BoidForces || BoidRadii || BoidNeighbours || BoidCells;
}
