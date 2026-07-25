using System.Collections.Generic;
using Godot;

/// <summary>
/// Узел работы — то, к чему подключаются исполнители своим инструментом.
/// Исполнитель только сообщает «подключился с такой-то мощностью»; ресурсы двигает сам узел,
/// исходя из суммарного потока. Один запрос за тик вместо обхода всех исполнителей.
///
/// Каркас (Blueprint) тянет ресурсы из хранилища, месторождение (OreDeposit) — отдаёт в хранилище.
/// </summary>
public abstract partial class WorkNode : Node2D
{
    /// <summary>Локальный реестр подключений: id исполнителя -> его мощность.</summary>
    private readonly Dictionary<int, float> _connections = new();

    public int Id { get; set; }

    /// <summary>Суммарная мощность подключённых, пересчитывается дельтой.</summary>
    public float TotalPower { get; private set; }

    public int WorkerCount => _connections.Count;

    /// <summary>Нужна ли ещё работа — по этому признаку боты ищут себе занятие.</summary>
    public abstract bool NeedsWork { get; }

    public void AttachWorker(int workerId, float power)
    {
        if (_connections.TryGetValue(workerId, out var previous))
            TotalPower -= previous;

        _connections[workerId] = power;
        TotalPower += power;
    }

    public void DetachWorker(int workerId)
    {
        if (_connections.Remove(workerId, out var power))
            TotalPower -= power;

        if (TotalPower < 0.0001f)
            TotalPower = 0f;
    }

    /// <summary>Шаг работы узла: сам решает, сколько ресурсов запросить или отдать.</summary>
    public abstract void Work(double dt);

    /// <summary>
    /// Узел уходит из игры: сам отпускает исполнителей, чтобы они не держали ссылку
    /// на удаляемую ноду. Иначе юнит на следующем кадре обратится к мёртвому объекту.
    /// </summary>
    protected void ReleaseWorkers()
    {
        foreach (var workerId in new List<int>(_connections.Keys))
            GameManager.I.Entities.Get<Unit>(workerId)?.OnTargetLost(this);

        _connections.Clear();
        TotalPower = 0f;
    }
}
