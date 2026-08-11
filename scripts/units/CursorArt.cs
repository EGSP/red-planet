/// <summary>
/// Вид курсора: что произойдёт по нажатию прямо сейчас.
///
/// НЕ СОВПАДАЕТ С <see cref="OrderKind"/> НАМЕРЕННО. Приказы, которые для игрока выглядят
/// одинаково, делят один курсор: атака по цели и приказ идти с боем — одно и то же
/// перекрестье, потому что разница между ними в том, есть ли под указателем враг, а это
/// игрок и так видит. Обратное тоже верно: <see cref="Arrow"/> не отвечает ни одному
/// приказу и означает, что нажатие приказа не отдаст.
/// </summary>
public enum CursorKind
{
    /// <summary>Обычная стрелка: выделение, интерфейс, приказа не будет.</summary>
    Arrow,

    Move,
    Attack,
    Repair,
    Assist,
    Build,
    Patrol,
}

/// <summary>
/// Картинки курсоров. Единственное место, где вид курсора связан с файлом на диске.
///
/// Набор в <c>assets/sprites/ui/commands</c> шире того, что используется: заготовлены
/// реклейм, остановка и повтор. Они появятся здесь вместе с приказами, которым отвечают, —
/// заводить вид курсора раньше самого приказа незачем.
/// </summary>
public static class CursorArt
{
    private const string Commands = "res://assets/sprites/ui/commands/";

    public static string PathOf(CursorKind kind) => kind switch
    {
        CursorKind.Move => Commands + "icons_command_move.png",
        CursorKind.Attack => Commands + "icons_command_attack.png",
        CursorKind.Repair => Commands + "icons_command_repair.png",
        CursorKind.Assist => Commands + "icons_command_assist.png",
        CursorKind.Build => Commands + "icons_command_use.png",
        CursorKind.Patrol => Commands + "icons_command_patrol.png",
        _ => "res://assets/sprites/ui/cursor.png",
    };

    /// <summary>
    /// Курсор, отвечающий виду приказа. Стройка и сопровождение читаются по своим
    /// картинкам, снос своей не имеет: он выдаётся сразу и цели под указателем не требует.
    /// </summary>
    public static CursorKind Of(OrderKind kind) => kind switch
    {
        OrderKind.Move => CursorKind.Move,
        OrderKind.Attack or OrderKind.AttackMove or OrderKind.AttackArea => CursorKind.Attack,
        OrderKind.Repair => CursorKind.Repair,
        OrderKind.Follow => CursorKind.Assist,
        OrderKind.Build => CursorKind.Build,
        OrderKind.Patrol => CursorKind.Patrol,
        _ => CursorKind.Arrow,
    };
}
