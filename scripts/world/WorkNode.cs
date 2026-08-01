using System.Collections.Generic;
using Godot;

/// <summary>
/// Узел работы — то, к чему подключаются исполнители своим инструментом.
/// Исполнитель только сообщает «подключился с такой-то мощностью»; ресурсы двигает сам узел,
/// исходя из суммарного потока. Один запрос за тик вместо обхода всех исполнителей.
///
/// Сейчас узел работы в игре один — каркас (Blueprint): он тянет метал из хранилища
/// по суммарной мощности подключённых строителей. Абстракция шире одного наследника
/// намеренно: месторождение было вторым, пока метал добывали вручную, и та же форма
/// понадобится любому занятию, где несколько исполнителей складывают мощность в одну работу.
/// </summary>
public abstract partial class WorkNode : Node2D, IEconomyActor
{
    /// <summary>
    /// Локальный реестр подключений: id исполнителя -> его мощность и расход энергии.
    /// Расход хранится вместе с мощностью, потому что у разных инструментов он разный,
    /// а узлу нужна только сумма.
    /// </summary>
    private readonly Dictionary<int, (float Power, float Energy)> _connections = new();

    public int Id { get; set; }

    /// <summary>Суммарная мощность подключённых, пересчитывается дельтой.</summary>
    public float TotalPower { get; private set; }

    /// <summary>Суммарный расход энергии подключённых инструментов, единиц в секунду.</summary>
    public float TotalEnergy { get; private set; }

    public int WorkerCount => _connections.Count;

    /// <summary>Нужна ли ещё работа — по этому признаку боты ищут себе занятие.</summary>
    public abstract bool NeedsWork { get; }

    public void AttachWorker(int workerId, float power, float energy)
    {
        if (_connections.TryGetValue(workerId, out var previous))
        {
            TotalPower -= previous.Power;
            TotalEnergy -= previous.Energy;
        }

        _connections[workerId] = (power, energy);
        TotalPower += power;
        TotalEnergy += energy;
    }

    public void DetachWorker(int workerId)
    {
        if (_connections.Remove(workerId, out var previous))
        {
            TotalPower -= previous.Power;
            TotalEnergy -= previous.Energy;
        }

        if (TotalPower < 0.0001f)
            TotalPower = 0f;

        if (TotalEnergy < 0.0001f)
            TotalEnergy = 0f;
    }

    /// <summary>Первый проход кадра: объявить свои потоки, единиц в секунду.</summary>
    public abstract void Declare(EconomyLedger ledger);

    /// <summary>Второй проход: отработать тик с долями, которые дала экономика.</summary>
    public abstract void Run(double dt, EconomyRates rates);

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
        TotalEnergy = 0f;
    }
}
