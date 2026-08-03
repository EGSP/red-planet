using Godot;

/// <summary>
/// Настройка мира: размер поля застройки, кольца руды и окружность появления противника.
///
/// ПОЧЕМУ ЭТО ПЕРЕЕХАЛО ИЗ <see cref="Const"/> В РЕСУРС. Разделение прежнее — по способу
/// настройки. Величины, которые подбираются глазом на карте, должны правиться там, где
/// карта видна, то есть в инспекторе рядом с предпросмотром. Числа же, от которых зависит
/// размер массивов растра навигации, остаются производными от <see cref="Radius"/>:
/// <see cref="NavGrid.Width"/> пересобирает массивы при смене размера, но пути, посчитанные
/// до того, остались бы в старых координатах. Между партиями и в редакторе правится свободно.
///
/// СИД ЗАДАЁТСЯ ЯВНО. Ноль означает «взять случайный», любое другое значение делает
/// раскладку воспроизводимой — и та же раскладка показывается предпросмотром в редакторе.
///
/// ЕДИНИЦА РАДИУСОВ И РАССТОЯНИЙ — клетка карты (<see cref="Const.Unit"/> пикселей),
/// если у свойства не сказано иное.
/// </summary>
[Tool]
[GlobalClass]
public partial class WorldSettings : Resource
{
    // ── Поле ──────────────────────────────────────────────────────────────────────

    [ExportGroup("Поле")]

    /// <summary>
    /// Радиус поля застройки от центра в клетках карты. Мир — квадрат со стороной
    /// в удвоенное значение плюс одна клетка.
    /// </summary>
    [Export(PropertyHint.Range, "8,64,1")] public int Radius = 20;

    /// <summary>Пустая зона у базы в клетках: ближе руду не кладём.</summary>
    [Export(PropertyHint.Range, "0,32,1")] public int BaseClearance = 4;

    /// <summary>Сид раскладки. Ноль — случайный на каждую партию.</summary>
    [Export] public ulong Seed;

    // ── Руда ──────────────────────────────────────────────────────────────────────

    [ExportGroup("Руда")]

    /// <summary>
    /// Кольца от центра наружу. Внутренний край следующего равен внешнему краю предыдущего.
    /// </summary>
    [Export] public WorldRingDefinition[] Rings;

    /// <summary>Наименьшее расстояние между рудниками в клетках карты.</summary>
    [Export(PropertyHint.Range, "1,16,1")] public int SpotSpacing = 3;

    /// <summary>Сколько раз пробуем поставить одну точку, прежде чем сдаться.</summary>
    [Export(PropertyHint.Range, "1,200,1")] public int PlacementAttempts = 60;

    // ── Появление противника ──────────────────────────────────────────────────────

    [ExportGroup("Появление противника")]

    /// <summary>
    /// Радиус окружности появления врагов в клетках карты. Должен быть не меньше
    /// <see cref="Radius"/>, иначе появление окажется внутри поля застройки.
    /// </summary>
    [Export(PropertyHint.Range, "8,128,1")] public int EnemySpawnRadius = 26;

    /// <summary>
    /// Доля <see cref="EnemySpawnRadius"/>, на которой лежит ближняя дуга формы волны.
    /// Единица — дуга на самой окружности появления; меньше единицы — ближе к базе.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01")] public float WaveStart = 1f;

    /// <summary>Радиус окружности появления противника в пикселях.</summary>
    public float SpawnRadiusPx => Mathf.Max(EnemySpawnRadius, 1) * Const.Unit;

    public float BaseClearancePx => Mathf.Max(BaseClearance, 0) * Const.Unit;

    public float SpotSpacingPx => Mathf.Max(SpotSpacing, 1) * Const.Unit;

    /// <summary>Суммарная толщина всех колец в клетках.</summary>
    public int FieldRadius
    {
        get
        {
            if (Rings == null || Rings.Length == 0)
                return 0;

            int sum = 0;

            foreach (var ring in Rings)
            {
                if (ring != null)
                    sum += Mathf.Max(ring.Radius, 0);
            }

            return sum;
        }
    }

    /// <summary>
    /// Внутренний и внешний радиус кольца в клетках. Кольца без определения пропускаются
    /// как нулевой вклад в сумму.
    /// </summary>
    public void RingBounds(int index, out float inner, out float outer)
    {
        inner = 0f;
        outer = 0f;

        if (Rings == null || index < 0 || index >= Rings.Length)
            return;

        for (int i = 0; i < index; i++)
        {
            var previous = Rings[i];

            if (previous != null)
                inner += Mathf.Max(previous.Radius, 0);
        }

        var ring = Rings[index];
        float thickness = ring != null ? Mathf.Max(ring.Radius, 0) : 0f;
        outer = inner + thickness;
    }

    /// <summary>
    /// Множитель террора по расстоянию от точки высадки в клетках. До первого кольца
    /// и внутри него — множитель первого; за последним — множитель последнего.
    /// </summary>
    public float TerrorMultiplierAt(float distance)
    {
        if (Rings == null || Rings.Length == 0)
            return 1f;

        float outer = 0f;
        float last = 1f;

        foreach (var ring in Rings)
        {
            if (ring == null)
                continue;

            outer += Mathf.Max(ring.Radius, 0);
            last = ring.TerrorMultiplier;

            if (distance <= outer)
                return last;
        }

        return last;
    }

    /// <summary>
    /// Квадрат, покрывающий окружность появления с запасом на корпус. Движение зажимается
    /// по нему, а не по полю застройки.
    /// </summary>
    public Rect2 ArenaBounds
    {
        get
        {
            float extent = SpawnRadiusPx + Const.Unit;
            return new Rect2(-extent, -extent, extent * 2f, extent * 2f);
        }
    }
}
