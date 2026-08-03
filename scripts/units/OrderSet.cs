using System.Collections.Generic;

/// <summary>
/// Набор видов приказов. Используется и как маска разрешения, и как перечень для панели.
///
/// РАЗРЕШЕНИЕ И ВОЗМОЖНОСТЬ РАЗДЕЛЕНЫ. Фактическая исполнимость выводится из снабжения
/// и класса сущности; явный список — из секции <c>[orders]</c> определения. К исполнению
/// принимается только пересечение. То, что сущность умеет, но в файле не разрешено,
/// попадает в мягкий набор и в панели помечается звёздочкой — в очередь такой приказ
/// не ставится.
/// </summary>
public readonly struct OrderSet
{
    private readonly int _mask;

    private OrderSet(int mask) => _mask = mask;

    /// <summary>Пустой набор: приказов нет.</summary>
    public static readonly OrderSet None = new(0);

    public OrderSet With(OrderKind kind) => new(_mask | Bit(kind));

    /// <summary>Добавить вид при истинном условии — так собирают набор из умений.</summary>
    public OrderSet With(OrderKind kind, bool when) => when ? With(kind) : this;

    public OrderSet Union(OrderSet other) => new(_mask | other._mask);

    public OrderSet Intersect(OrderSet other) => new(_mask & other._mask);

    public OrderSet Except(OrderSet other) => new(_mask & ~other._mask);

    public bool Allows(OrderKind kind) => (_mask & Bit(kind)) != 0;

    public bool Any => _mask != 0;

    /// <summary>Виды по порядку объявления перечисления — для интерфейса и отладки.</summary>
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
