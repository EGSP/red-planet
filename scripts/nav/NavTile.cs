/// <summary>
/// Неизменяемый тайл навигационного растра: непроходимость и ограниченный клиренс.
/// Публикуется только целиком; пересчитанный тайл заменяет прежнюю ссылку в снимке.
/// </summary>
public sealed class NavTile
{
    /// <summary>Ячеек по стороне тайла. Тайл есть единица пересчёта растра и узел графа регионов.</summary>
    public const int Size = 32;

    public readonly int TileX;
    public readonly int TileY;
    public readonly bool[] Blocked;
    public readonly int[] Distance;

    public NavTile(int tileX, int tileY, bool[] blocked, int[] distance)
    {
        TileX = tileX;
        TileY = tileY;
        Blocked = blocked;
        Distance = distance;
    }
}
