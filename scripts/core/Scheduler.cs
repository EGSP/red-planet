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
/// </summary>
public sealed class Scheduler
{
    private readonly List<GameSystem> _systems = new();
    private bool _dirty;

    public void Add(GameSystem system)
    {
        _systems.Add(system);
        _dirty = true;
    }

    public void Remove(GameSystem system) => _systems.Remove(system);

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
            if (!GodotObject.IsInstanceValid(system))
            {
                _systems.RemoveAt(i);
                continue;
            }
        }

        foreach (var system in _systems)
            system.Step(dt);
    }
}
