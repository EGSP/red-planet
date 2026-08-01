using System.Collections.Generic;
using Godot;

/// <summary>
/// Создание мира в той его части, которая касается метала: один раз расставляет точки
/// и на этом заканчивает работу. Респавна нет и быть не может — точка вечна.
///
/// ПОЧЕМУ ЭТО СИСТЕМА, А НЕ ВЫЗОВ ИЗ СЕССИИ. Расставлять точки можно только тогда, когда
/// стартовая база уже стоит: иначе точки займут её клетки. Стартовую раздачу делает Session
/// в своём _Ready, а он приходит позже _Ready систем, поэтому единственный момент, когда мир
/// заведомо собран, — первый кадр симуляции. Тем же способом это делалось и раньше.
///
/// Все точки одинаковы. Богатства, коэффициентов и видов у них нет: разницу создаёт только
/// положение — до дальней точки дольше идти и труднее её прикрыть. Пока карта одна на всех
/// и симметрична, этого хватает, чтобы вопрос «куда расширяться» имел цену.
/// </summary>
public partial class MetalSpotSystem : GameSystem
{
    /// <summary>Сколько раз пробуем поставить одну точку, прежде чем сдаться.</summary>
    private const int Attempts = 60;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<Vector2I> _placed = new();

    private bool _seeded;

    protected override void OnRegister() => _rng.Randomize();

    public override void Step(double dt)
    {
        if (_seeded || GM.Playground == null)
            return;

        Generate();
        _seeded = true;
    }

    /// <summary>
    /// Сначала ближние точки, потом все остальные. Порядок именно такой, потому что
    /// ближние — гарантия, а не пожелание: раскладка, оставившая начало без метала,
    /// проиграна до первого решения игрока.
    /// </summary>
    private void Generate()
    {
        for (int i = 0; i < Const.MetalSpotStartCount; i++)
            TryPlace(Const.MetalSpotBaseClearance, Const.MetalSpotStartRadius);

        while (_placed.Count < Const.MetalSpotCount)
            if (!TryPlace(Const.MetalSpotBaseClearance, Const.MetalSpotFieldRadius))
                break;

        if (_placed.Count < Const.MetalSpotCount)
            GD.PushWarning($"[Точки метала] размещено {_placed.Count} из {Const.MetalSpotCount}. " +
                           "Поле тесное для заданного числа точек и расстояния между ними");
    }

    private bool TryPlace(int minRadius, int maxRadius)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var cell = RandomCell(minRadius, maxRadius);

            if (!Suitable(cell))
                continue;

            GM.Spawn.SpawnMetalSpot(cell);
            _placed.Add(cell);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Случайная клетка кольца. Радиус берётся через корень намеренно: площадь кольца растёт
    /// вместе с радиусом, и равномерный выбор радиуса сгущал бы точки к центру — как раз туда,
    /// где они нужны меньше всего.
    /// </summary>
    private Vector2I RandomCell(int minRadius, int maxRadius)
    {
        float angle = _rng.RandfRange(0f, Mathf.Tau);
        float squared = _rng.RandfRange(minRadius * minRadius, maxRadius * maxRadius);
        float radius = Mathf.Sqrt(squared);

        return new Vector2I(
            Mathf.RoundToInt(Mathf.Cos(angle) * radius),
            Mathf.RoundToInt(Mathf.Sin(angle) * radius));
    }

    /// <summary>Клетка в границах, свободна, ещё не помечена и не липнет к соседней точке.</summary>
    private bool Suitable(Vector2I cell)
    {
        if (!GM.Grid.InBounds(cell) || GM.Grid.IsOccupied(cell) || GM.Grid.HasMetal(cell))
            return false;

        int spacing = Const.MetalSpotSpacing * Const.MetalSpotSpacing;

        foreach (var placed in _placed)
            if ((placed - cell).LengthSquared() < spacing)
                return false;

        return true;
    }
}
