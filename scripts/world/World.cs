using Godot;

/// <summary>
/// Действующие настройки мира: размер поля застройки, кольца руды, окружность появления
/// противника. Единственная точка, откуда их читают все, кому нужны границы.
///
/// ПОЧЕМУ СТАТИЧЕСКАЯ ССЫЛКА, А НЕ ПОЛЕ НА КАЖДОЙ СИСТЕМЕ. Границы мира спрашивают
/// растр навигации, постановка построек, камера и раскладка ориентиров — то есть код,
/// который к менеджеру сессии отношения не имеет и получать ресурс по цепочке
/// зависимостей был бы вынужден ради одного числа. Раньше те же величины лежали
/// константами в <see cref="Const"/> и были такими же глобальными; изменилось лишь то,
/// что теперь их можно править, а не пересобирать проект.
///
/// КАРТА В ПРОЦЕССЕ ОДНА. Свойство ставится при сборке сессии и при открытии
/// предпросмотра в редакторе. Двух миров одновременно не бывает: сессия в приложении
/// одна, а редактор игру не запускает.
/// </summary>
public static class World
{
    private static WorldSettings _settings;

    /// <summary>
    /// Действующие настройки. Не назначены — берутся умолчания класса, поэтому обращение
    /// к границам мира не роняет ни игру, ни редактор, даже если сцена собрана не до конца.
    /// </summary>
    public static WorldSettings Settings
    {
        get => _settings ??= new WorldSettings();
        set => _settings = value;
    }

    /// <summary>Радиус поля застройки от центра в клетках карты.</summary>
    public static int Radius => Settings.Radius;

    /// <summary>Клеток застройки по стороне мира.</summary>
    public static int Cells => Radius * 2 + 1;

    /// <summary>Сторона поля застройки в пикселях.</summary>
    public static int SizePx => Cells * Const.Unit;

    /// <summary>Ячеек навигации по стороне поля застройки.</summary>
    public static int NavWidth => SizePx / Const.NavCell;

    /// <summary>Левый верхний угол поля застройки в пикселях.</summary>
    public static Vector2 Min => new(-Radius * Const.Unit, -Radius * Const.Unit);

    /// <summary>Прямоугольник поля застройки: постановка, руда, растр навигации.</summary>
    public static Rect2 Bounds => new(Min, new Vector2(SizePx, SizePx));

    /// <summary>
    /// Прямоугольник арены движения: покрывает окружность появления противника.
    /// Растр навигации сюда не растягивается.
    /// </summary>
    public static Rect2 ArenaBounds => Settings.ArenaBounds;

    /// <summary>Радиус окружности появления противника в пикселях.</summary>
    public static float SpawnRadiusPx => Settings.SpawnRadiusPx;
}
