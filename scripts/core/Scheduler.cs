using System;
using System.Collections.Generic;
using Godot;

/// <summary>Фазы кадра. Порядок фаз фиксирован, внутри фазы — по полю Order.</summary>
public enum Phase
{
    Input = 0,
    Simulate = 1,
    React = 2,
    Cleanup = 3,
}

/// <summary>
/// Свой планировщик вместо неявного обхода дерева нод: порядок систем задаётся явно,
/// кадр целиком гоняется из одного _PhysicsProcess у GameManager.
///
/// Здесь же поиск системы по типу. Отдельного контейнера под это заводить нельзя:
/// планировщик — единственное место, которое знает состав систем, и параллельный
/// список неизбежно разошёлся бы с ним при добавлении или удалении.
/// </summary>
public sealed class Scheduler
{
    private readonly List<GameSystem> _systems = new();

    /// <summary>
    /// Кэш поиска по типу. Ключ — как объявленный тип системы, так и любой базовый,
    /// по которому её однажды спросили: систему ищут и по конкретному классу,
    /// и по общему предку, а сканировать список на каждое обращение незачем.
    /// </summary>
    private readonly Dictionary<Type, GameSystem> _byType = new();

    private bool _dirty;

    /// <summary>Состав систем в порядке регистрации — по нему идёт вторая фаза инициализации.</summary>
    public IReadOnlyList<GameSystem> Systems => _systems;

    public void Add(GameSystem system)
    {
        _systems.Add(system);
        _dirty = true;

        var type = system.GetType();

        // Две системы одного типа делают поиск по типу бессмысленным: ответ становится
        // произвольным. Такое почти наверняка ошибка сборки сцены, поэтому говорим вслух
        if (_byType.TryGetValue(type, out var existing) && Alive.Is(existing))
        {
            GD.PushWarning($"[Scheduler] систем типа {type.Name} больше одной: " +
                           $"поиск по типу вернёт {existing.Name}");
            return;
        }

        _byType[type] = system;
    }

    public void Remove(GameSystem system)
    {
        _systems.Remove(system);
        Forget(system);
    }

    /// <summary>
    /// Система по типу. Заменяет рукописные свойства вида GameManager.Command:
    /// композиционный корень не должен разрастаться с каждой новой системой.
    /// Отсутствие системы — не ошибка сама по себе, ответом будет null.
    /// </summary>
    public T Get<T>() where T : GameSystem
    {
        if (_byType.TryGetValue(typeof(T), out var cached) && Alive.Is(cached))
            return (T)cached;

        foreach (var system in _systems)
        {
            if (system is not T typed)
                continue;

            _byType[typeof(T)] = typed;
            return typed;
        }

        return null;
    }

    public void RunFrame(double dt)
    {
        if (_dirty)
        {
            _systems.Sort((a, b) => a.Phase != b.Phase
                ? a.Phase.CompareTo(b.Phase)
                : a.StepOrder.CompareTo(b.StepOrder));
            _dirty = false;
        }

        for (int i = _systems.Count - 1; i >= 0; i--)
        {
            var system = _systems[i];
            if (Alive.Is(system))
                continue;

            _systems.RemoveAt(i);
            Forget(system);
        }

        foreach (var system in _systems)
            system.Step(dt);
    }

    /// <summary>
    /// Выкинуть систему из кэша поиска. Ключей у неё может быть несколько — свой тип
    /// и все базовые, по которым её спрашивали, — поэтому идём по значениям.
    /// </summary>
    private void Forget(GameSystem system)
    {
        List<Type> keys = null;

        foreach (var pair in _byType)
        {
            // Сравниваем по ссылке: у освобождённой обёртки движка перегруженные
            // операторы сравнения небезопасны — тот же приём применён в Index
            if (!ReferenceEquals(pair.Value, system))
                continue;

            keys ??= new List<Type>();
            keys.Add(pair.Key);
        }

        if (keys == null)
            return;

        foreach (var key in keys)
            _byType.Remove(key);
    }
}
