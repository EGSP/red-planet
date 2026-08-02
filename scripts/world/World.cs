using Godot;

/// <summary>
/// Действующие настройки мира: размер поля, раскладка точек метала, кольцо появления
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

    /// <summary>Клеток застройки от центра до края.</summary>
    public static int RadiusCells => Settings.RadiusCells;

    /// <summary>Клеток застройки по стороне мира.</summary>
    public static int Cells => RadiusCells * 2 + 1;

    /// <summary>Сторона мира в пикселях.</summary>
    public static int SizePx => Cells * Const.Unit;

    /// <summary>Ячеек навигации по стороне мира.</summary>
    public static int NavWidth => SizePx / Const.NavCell;

    /// <summary>Левый верхний угол мира в пикселях.</summary>
    public static Vector2 Min => new(-RadiusCells * Const.Unit, -RadiusCells * Const.Unit);

    public static Rect2 Bounds => new(Min, new Vector2(SizePx, SizePx));

    /// <summary>Радиус окружности появления противника в пикселях.</summary>
    public static float SpawnRadiusPx => Settings.SpawnRadiusPx;
}
