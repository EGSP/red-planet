using Godot;

/// <summary>
/// Баланс кадра: сколько экономика может дать и сколько у неё просят.
/// Отсюда берётся производительность базы — то самое число в процентах.
///
/// ЛОГИКА (повторяет экономику PA, зафиксирована здесь намеренно):
///
///   доступно  = запас + доход · dt          — сначала берём доход, недостачу добираем из запаса
///   спрос     = Σ заявок потребителей · dt   — сколько хотели бы потратить за этот тик
///   КПД(вид)  = clamp(доступно / спрос, 0, 1)
///   темп работы = min(КПД метала, КПД энергии) — работаем по худшему из двух
///
/// Заявка на КПД НЕ умножается: потребитель всегда просит свою полную мощность.
/// Иначе вышло бы уравнение с самим собой — чтобы узнать спрос, надо знать КПД,
/// а чтобы знать КПД, надо знать спрос.
///
/// Расход двух ресурсов ужимается по-разному, и это не небрежность:
///   МЕТАЛ    — по темпу работы: строим вдвое медленнее, вдвое медленнее и платим.
///   ЭНЕРГИЯ  — только по своему КПД: мощность оплачивается целиком, даже если стройка
///              еле ползёт из-за нехватки метала. Лишняя энергия просто сгорает.
/// Отсюда и правило игры: просадку по энергии лечат генераторами, а не тем,
/// что потребители сами себя подожмут.
///
/// Главное свойство модели: **цена не меняется, меняется скорость.** Не хватает — всё
/// замедляется равномерно, но ничего не встаёт и не отключается. При КПД 50% две стройки
/// идут ровно так же быстро, как одна при 100%.
///
/// Запас работает демпфером: пока в хранилище что-то есть, короткие всплески спроса
/// не роняют производительность. Пустое хранилище — просадка ровно во столько раз,
/// во сколько не хватает дохода.
///
/// Это ЧИСТЫЙ C#-объект под композиционным корнем: движок ему не нужен, состояние живёт
/// один кадр. Долговременное состояние — запас — лежит в проекции, как и положено.
/// </summary>
public readonly struct EconomyRates
{
    public EconomyRates(float metal, float energy)
    {
        Metal = metal;
        Energy = energy;
    }

    /// <summary>Какую долю запрошенного метала экономика реально даёт.</summary>
    public readonly float Metal;

    /// <summary>Какую долю запрошенной энергии экономика реально даёт.</summary>
    public readonly float Energy;

    /// <summary>
    /// Темп работы для того, кто тратит оба ресурса: по худшему из двух.
    /// Каждый актор применяет только то, что ему подходит — синтезатор метала
    /// нехваткой метала не ограничен, он его как раз производит.
    /// </summary>
    public float Work => Mathf.Min(Metal, Energy);
}

public sealed class EconomyLedger
{
    public float MetalIncome { get; private set; }

    public float EnergyIncome { get; private set; }

    /// <summary>Сколько запросили за секунду — до применения производительности.</summary>
    public float MetalDemand { get; private set; }

    public float EnergyDemand { get; private set; }

    public float MetalEfficiency { get; private set; } = 1f;

    public float EnergyEfficiency { get; private set; } = 1f;

    /// <summary>
    /// Производительность базы, 0..1 — то, что показывается игроку.
    /// Это худший из двух КПД: именно он определяет темп работы, а значит и то,
    /// как быстро на самом деле идут стройка и добыча.
    /// </summary>
    public float Efficiency { get; private set; } = 1f;

    /// <summary>Доли по каждому ресурсу — акторы берут отсюда то, что их касается.</summary>
    public EconomyRates Rates => new(MetalEfficiency, EnergyEfficiency);

    /// <summary>
    /// Фактический расход энергии за секунду. Энергию потребители тянут по полной,
    /// а ужимает её только нехватка самой энергии: при дефиците метала мощность всё
    /// равно оплачена, она просто сгорает. Восстановить производительность в этом
    /// случае можно лишь генераторами.
    /// </summary>
    public float EnergySpending => EnergyDemand * EnergyEfficiency;

    /// <summary>
    /// Фактический расход метала. А вот он идёт по темпу работы: строим вдвое медленнее —
    /// вдвое медленнее платим, полная цена постройки от этого не меняется.
    /// </summary>
    public float MetalSpending => MetalDemand * Efficiency;

    /// <summary>Чистый поток по заявкам: минус — спроса больше, чем экономика тянет.</summary>
    public float MetalNet => MetalIncome - MetalDemand;

    public float EnergyNet => EnergyIncome - EnergyDemand;

    /// <summary>Новый кадр — заявки собираются заново, старые не переносятся.</summary>
    public void BeginFrame()
    {
        MetalIncome = 0f;
        EnergyIncome = 0f;
        MetalDemand = 0f;
        EnergyDemand = 0f;
    }

    /// <summary>Заявить производство, единиц в секунду.</summary>
    public void AddIncome(ResourceKind kind, float rate)
    {
        if (rate <= 0f)
            return;

        if (kind == ResourceKind.Metal)
            MetalIncome += rate;
        else
            EnergyIncome += rate;
    }

    /// <summary>Заявить потребление, единиц в секунду.</summary>
    public void Request(ResourceKind kind, float rate)
    {
        if (rate <= 0f)
            return;

        if (kind == ResourceKind.Metal)
            MetalDemand += rate;
        else
            EnergyDemand += rate;
    }

    /// <summary>
    /// Свести баланс кадра. Зовётся ровно один раз между сбором заявок и работой:
    /// потребители обязаны узнать производительность ДО того, как что-то потратят,
    /// иначе получатся качели «отработали вполсилы → спрос упал → снова полная нагрузка».
    /// </summary>
    public void Resolve(double dt, StockpileProjection stockpile)
    {
        MetalEfficiency = Ratio(stockpile.Get(ResourceKind.Metal), MetalIncome, MetalDemand, dt);
        EnergyEfficiency = Ratio(stockpile.Get(ResourceKind.Energy), EnergyIncome, EnergyDemand, dt);

        Efficiency = Mathf.Min(MetalEfficiency, EnergyEfficiency);
    }

    private static float Ratio(float stored, float income, float demand, double dt)
    {
        if (demand <= 0f)
            return 1f;

        float available = stored + income * (float)dt;
        float needed = demand * (float)dt;

        return Mathf.Clamp(available / needed, 0f, 1f);
    }
}
