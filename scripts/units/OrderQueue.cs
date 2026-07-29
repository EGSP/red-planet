using System.Collections.Generic;
using Godot;

/// <summary>
/// Очередь приказов сущности. Обычный C#-объект, которым владеет нода.
///
/// ФИЛЬТР ЖИВЁТ ЗДЕСЬ, и обойти его нельзя: очередь знает своего хозяина и спрашивает
/// у него набор допустимых приказов на каждой постановке. Поэтому раздающей системе
/// не нужно разбираться, кому она отдаёт приказ, — достаточно попробовать.
///
/// Ставим всё или ничего: если хоть один приказ цепочки недопустим, очередь не меняется
/// вовсе. Иначе «дойти и починить» превратилось бы в «дойти», и юнит молча встал бы
/// посреди карты вместо того, чтобы не принять приказ.
/// </summary>
public sealed class OrderQueue
{
    private readonly IOrderable _owner;
    private readonly List<Order> _items = new();

    public OrderQueue(IOrderable owner) => _owner = owner;

    public IReadOnlyList<Order> Items => _items;

    public Order Current => _items.Count > 0 ? _items[0] : null;

    public bool Idle => _items.Count == 0;

    public int Count => _items.Count;

    public bool Allows(OrderKind kind) => _owner.AllowedOrders.Allows(kind);

    /// <summary>Заменить очередь целиком. Вернула false — приказ этой сущности не положен.</summary>
    public bool TrySet(params Order[] orders)
    {
        if (!Acceptable(orders))
            return false;

        _items.Clear();
        _items.AddRange(orders);
        return true;
    }

    /// <summary>Дописать в хвост, не трогая текущую работу.</summary>
    public bool TryEnqueue(params Order[] orders)
    {
        if (!Acceptable(orders))
            return false;

        _items.AddRange(orders);
        return true;
    }

    public void Clear() => _items.Clear();

    public void DropCurrent()
    {
        if (_items.Count > 0)
            _items.RemoveAt(0);
    }

    /// <summary>
    /// Снять с головы всё, что больше не выполнить: цель пала, каркас достроен,
    /// месторождение выработано. Цикл, а не одна проверка: невыполнимой может оказаться
    /// и следующая, и та, что за ней.
    /// </summary>
    public void DropInvalid()
    {
        while (_items.Count > 0 && !_items[0].IsValid())
            _items.RemoveAt(0);
    }

    /// <summary>Сущность уходит из игры — вычищаем все приказы на неё, а не только текущий.</summary>
    public void DropAllFor(Node2D target) =>
        _items.RemoveAll(order => order.Target == target || order.Entity == target);

    private bool Acceptable(Order[] orders)
    {
        if (orders == null || orders.Length == 0)
            return false;

        var allowed = _owner.AllowedOrders;

        foreach (var order in orders)
            if (order == null || !allowed.Allows(order.Kind))
                return false;

        return true;
    }
}

/// <summary>
/// Всё, чему можно отдать приказ. Юнит здесь — понятие широкое: это и боты, и коммандер,
/// и постройки, и враги. Разница между ними не в том, есть ли у них очередь, а в том,
/// что в их наборе: у турели один приказ, у месторождения ни одного.
///
/// РАЗДЕЛЕНИЕ ТРУДА. Приказы раздают мозговые системы (BotAiSystem, EnemyAiSystem,
/// AssemblerSystem, CommandSystem по воле игрока), а исполняет их сама сущность —
/// OrderSystem только зовёт. Так политика «чем заняться» отделена от механики «как сделать»,
/// и вражеский бот отличается от союзного своим мозгом, а не своим кодом движения.
/// </summary>
public interface IOrderable
{
    int EntityId { get; }

    Faction Faction { get; }

    Vector2 GlobalPosition { get; }

    /// <summary>Имя для интерфейса.</summary>
    string DisplayName { get; }

    /// <summary>Что этой сущности вообще можно приказать. Пустой набор — ничего.</summary>
    OrderSet AllowedOrders { get; }

    OrderQueue Orders { get; }

    /// <summary>Отработать текущий приказ за кадр.</summary>
    void RunOrder(Order order, double dt);

    /// <summary>Приказов нет: отпустить работу и вести себя как обычно.</summary>
    void OnIdle(double dt);
}
