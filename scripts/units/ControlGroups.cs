using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Боевые группы: десять отрядов, привязанных к цифровым клавишам.
///
/// СОСТАВ КОПИРУЕТСЯ, А НЕ ССЫЛАЕТСЯ. Назначение складывает в слот копию выделения:
/// иначе группа осталась бы вторым именем того же списка и менялась бы при каждом щелчке
/// мышью. Группа обязана переживать смену выделения — в этом весь её смысл.
///
/// ЧЛЕНСТВО НЕ ИСКЛЮЧИТЕЛЬНО. Юнит может состоять в нескольких группах разом, и это
/// не ошибка: слот — сохранённый выбор игрока, а не свойство юнита. Вычищать пересечения
/// не нужно.
///
/// МЁРТВЫЕ УХОДЯТ ВЫМЕТАНИЕМ на каждом кадре, как и из выделения. Иначе группа копила бы
/// призраков, а её счётчик врал.
/// </summary>
public sealed class ControlGroups
{
    /// <summary>Слотов десять: клавиши 1…9 и 0 последней.</summary>
    public const int Count = 10;

    private readonly List<IOrderable>[] _slots = new List<IOrderable>[Count];

    /// <summary>Выбранный слот или -1, если выделение набрано вручную.</summary>
    public int Current { get; set; } = -1;

    public ControlGroups()
    {
        for (int slot = 0; slot < Count; slot++)
            _slots[slot] = new List<IOrderable>();
    }

    /// <summary>
    /// Подпись слота. Совпадает с клавишей по умолчанию, но привязку не задаёт: клавиши
    /// живут в <see cref="InputActions"/> и переназначаются, а подпись останется цифрой
    /// слота, пока настройки не научатся показывать назначенную клавишу.
    /// </summary>
    public static string Label(int slot) => slot == Count - 1 ? "0" : (slot + 1).ToString();

    public IReadOnlyList<IOrderable> Members(int slot) =>
        slot >= 0 && slot < Count ? _slots[slot] : Array.Empty<IOrderable>();

    public int Size(int slot) => slot >= 0 && slot < Count ? _slots[slot].Count : 0;

    /// <summary>
    /// Записать состав в слот. Пустой состав очищает группу — отдельного жеста на роспуск
    /// не нужно: снял выделение, повторил сочетание.
    /// </summary>
    public void Assign(int slot, IReadOnlyList<IOrderable> members)
    {
        if (slot < 0 || slot >= Count)
            return;

        var group = _slots[slot];
        group.Clear();

        foreach (var actor in members)
            if (actor != null && !group.Contains(actor))
                group.Add(actor);
    }

    /// <summary>Выбросить погибших. Зовётся раз в кадр, вместе с чисткой выделения.</summary>
    public void Sweep()
    {
        foreach (var group in _slots)
            group.RemoveAll(actor => !Alive.Is(actor as Node));
    }

    /// <summary>
    /// Заменить члена преемником во всех слотах. Каркас достраивается в готовую сущность,
    /// и группа обязана удержать преемника на месте ушедшего каркаса: иначе слот терял бы
    /// завод в миг готовности, хотя очередь производства уже переехала.
    ///
    /// Если преемник уже состоит в том же слоте, каркас просто снимается — дублировать
    /// одного исполнителя в группе нельзя.
    /// </summary>
    public void Succeed(IOrderable from, IOrderable to)
    {
        if (from == null || to == null || ReferenceEquals(from, to))
            return;

        foreach (var group in _slots)
        {
            for (int i = 0; i < group.Count; i++)
            {
                if (!ReferenceEquals(group[i], from))
                    continue;

                if (group.Contains(to))
                    group.RemoveAt(i);
                else
                    group[i] = to;

                break;
            }
        }
    }
}
