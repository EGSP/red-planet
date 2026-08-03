using System.Collections.Generic;
using Godot;

/// <summary>
/// Набор состава волны: кто именно придёт при этом бюджете.
///
/// ПОЧЕМУ ЭТО ОТДЕЛЬНО ОТ <see cref="WaveSystem"/>, как и геометрия. Предпросмотр в редакторе
/// обязан показывать тот же состав, что придёт в игре, иначе подбирать по нему доли
/// и бюджеты бессмысленно. Зависимость здесь ровно одна — справочник видов, и он живёт
/// в автозагрузке содержимого, то есть доступен и без сессии.
///
/// ДВА ПРОХОДА, И ОНИ РАЗНЫЕ. Сначала выполняются целевые доли — те части бюджета, которые
/// волна отвела конкретным видам; затем остаток тратится свободно среди всего допустимого.
/// Порядок списков в файле при этом значим: он и есть порядок исполнения долей. Неявная
/// сортировка, скажем по величине доли, поставила бы результат в зависимость от чисел,
/// которые правят ради баланса, а не ради очерёдности.
/// </summary>
public sealed class WaveComposer
{
    private readonly List<UnitDefinition> _allowed = new();
    private readonly List<UnitDefinition> _pool = new();
    private readonly List<UnitDefinition> _composition = new();

    /// <summary>Набранный состав. Действителен до следующего вызова <see cref="Compose"/>.</summary>
    public IReadOnlyList<UnitDefinition> Composition => _composition;

    /// <summary>Сколько мощи израсходовано. Меньше бюджета — остаток было некому занять.</summary>
    public float Spent { get; private set; }

    /// <summary>Набрать состав в пределах бюджета.</summary>
    public void Compose(Catalog catalog, WaveDefinition wave, float budget,
        RandomNumberGenerator rng)
    {
        _composition.Clear();
        Spent = 0f;

        Allowed(catalog, wave);

        if (_allowed.Count == 0)
        {
            GD.PushWarning($"[WaveComposer] волна «{wave.Id}»: допустимых видов не осталось");
            return;
        }

        foreach (var list in wave.UnitLists)
        {
            if (list.TargetBudgetShare <= 0f)
                continue;

            Pool(list);
            Quota(_pool, budget * list.TargetBudgetShare, budget, rng);
        }

        Quota(_allowed, budget, budget, rng);
    }

    /// <summary>
    /// Потратить на виды из pool отведённую им квоту.
    ///
    /// Последний вид берётся и тогда, когда квота им перебирается: условие проверяется
    /// ДО добавления, а не после. Иначе «потратить на этих треть бюджета» почти никогда
    /// не выполнялось бы — доля кратна цене вида лишь по случайности, и набор
    /// останавливался бы, не дойдя до неё.
    ///
    /// Ограничение при этом одно и общее: сколько бы ни осталось в квоте, за пределы
    /// бюджета волны набор не выходит.
    /// </summary>
    private void Quota(List<UnitDefinition> pool, float quota, float budget,
        RandomNumberGenerator rng)
    {
        if (pool.Count == 0)
            return;

        float used = 0f;

        while (used < quota)
        {
            var definition = pool[rng.RandiRange(0, pool.Count - 1)];
            float power = definition.ArmyPower;

            if (Spent + power > budget)
            {
                // В остаток бюджета не помещается именно этот вид, но может поместиться
                // другой из того же списка — перебираем дешёвые, прежде чем сдаться
                if (!Cheapest(pool, budget - Spent, out definition))
                    break;

                power = definition.ArmyPower;
            }

            _composition.Add(definition);
            Spent += power;
            used += power;
        }
    }

    /// <summary>Самый дешёвый вид, помещающийся в остаток. Ложь — не помещается ни один.</summary>
    private static bool Cheapest(List<UnitDefinition> pool, float available,
        out UnitDefinition found)
    {
        found = null;

        foreach (var definition in pool)
            if (definition.ArmyPower <= available &&
                (found == null || definition.ArmyPower < found.ArmyPower))
                found = definition;

        return found != null;
    }

    /// <summary>
    /// Допустимые виды. Списки allow сужают: есть хоть один — допустимо только их
    /// объединение; нет ни одного — допустимы все виды противника. Списки deny
    /// вычитаются после. Списки limit на отбор не влияют вовсе, у них только доля.
    /// </summary>
    private void Allowed(Catalog catalog, WaveDefinition wave)
    {
        _allowed.Clear();
        bool narrowed = false;

        foreach (var list in wave.UnitLists)
        {
            if (list.Mode != UnitListMode.Allow)
                continue;

            narrowed = true;

            foreach (var definition in list.Units)
                if (!_allowed.Contains(definition))
                    _allowed.Add(definition);
        }

        if (!narrowed)
            foreach (var definition in catalog.Enemies)
                if (definition.ArmyPower > 0f)
                    _allowed.Add(definition);

        foreach (var list in wave.UnitLists)
        {
            if (list.Mode != UnitListMode.Deny)
                continue;

            foreach (var definition in list.Units)
                _allowed.Remove(definition);
        }
    }

    /// <summary>Виды списка, оставшиеся допустимыми после сужения и вычитания.</summary>
    private void Pool(WaveUnitList list)
    {
        _pool.Clear();

        foreach (var definition in list.Units)
            if (_allowed.Contains(definition))
                _pool.Add(definition);
    }

    /// <summary>Состав перечислением видов с количествами: «Стрелок ×6, Толстяк ×2».</summary>
    public string Describe()
    {
        if (_composition.Count == 0)
            return "пусто";

        var counts = new Dictionary<string, int>();
        var order = new List<string>();

        foreach (var definition in _composition)
        {
            string name = definition.DisplayName.Length > 0 ? definition.DisplayName : definition.Id;

            if (!counts.TryAdd(name, 1))
                counts[name]++;
            else
                order.Add(name);
        }

        var parts = new List<string>(order.Count);

        foreach (string name in order)
            parts.Add($"{name} ×{counts[name]}");

        return string.Join(", ", parts);
    }
}
