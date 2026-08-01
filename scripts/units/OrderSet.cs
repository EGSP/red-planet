using System.Collections.Generic;

/// <summary>
/// Набор приказов, которые сущности вообще можно отдать.
///
/// ЗАЧЕМ. Он работает сразу в двух ролях: как справка — что этому юниту доступно, — и как
/// фильтр: приказ не из набора не поставится, кто бы его ни отдавал. Поэтому системе,
/// раздающей приказы, не нужно знать, кому она их раздаёт: копателю не уйдёт приказ
/// атаковать, а месторождению — вообще никакой. Пустой набор и означает «команд не имеет».
///
/// ВЫВОДИТСЯ ИЗ УМЕНИЙ, А НЕ ЗАДАЁТСЯ СПИСКОМ. Набор собирается из тех же полей справочника,
/// по которым сущность работает: нет бура — нет приказа копать, нет ствола — нет атаки,
/// нулевая скорость — некуда идти. Так набор не может разъехаться с тем, что юнит умеет
/// на самом деле, а настраивать вторую копию правды в .toml не приходится.
/// </summary>
public readonly struct OrderSet
{
    private readonly int _mask;

    private OrderSet(int mask) => _mask = mask;

    /// <summary>Приказов не имеет: месторождение, склад, стена.</summary>
    public static readonly OrderSet None = new(0);

    public OrderSet With(OrderKind kind) => new(_mask | Bit(kind));

    /// <summary>Добавить вид, если умение есть — так набор и собирают из справочника.</summary>
    public OrderSet With(OrderKind kind, bool when) => when ? With(kind) : this;

    public bool Allows(OrderKind kind) => (_mask & Bit(kind)) != 0;

    public bool Any => _mask != 0;

    /// <summary>Виды по порядку — для интерфейса и отладки.</summary>
    public IEnumerable<OrderKind> Kinds
    {
        get
        {
            foreach (OrderKind kind in System.Enum.GetValues<OrderKind>())
                if (Allows(kind))
                    yield return kind;
        }
    }

    private static int Bit(OrderKind kind) => 1 << (int)kind;
}
