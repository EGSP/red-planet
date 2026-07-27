using System;
using System.Collections.Generic;

/// <summary>
/// Проекция — производное состояние, собранное из потока документов.
/// В терминах 1С это регистр накопления, в терминах CQRS — read model.
///
/// ЗАЧЕМ. Документы отвечают на вопрос «что случилось», проекция — на вопрос
/// «сколько сейчас». Пробегать журнал каждый раз, когда UI хочет число, немыслимо,
/// поэтому проекция держит готовый ответ.
///
/// ГЛАВНОЕ ПРАВИЛО: обновляться ДЕЛЬТОЙ в момент публикации документа,
/// а не пересчётом из истории. Два следствия, оба важные:
/// 1. Журнал можно чистить и усекать — проекция самодостаточна и ничего не потеряет.
/// 2. Стоимость обновления не растёт вместе с числом сущностей.
/// Именно на этом обжёгся прошлый прототип: там регистр суммировал всех живых врагов
/// заново на каждый спавн и смерть.
///
/// КАК ПРИМЕНЯТЬ. Унаследоваться, в Subscribe подписаться на нужные потоки и править
/// свои поля дельтой, а после правки позвать NotifyChanged.
/// </summary>
public abstract class Projection
{
    /// <summary>
    /// Состояние изменилось. Отсюда обновляется представление: HUD слушает это событие
    /// вместо того, чтобы опрашивать проекцию каждый кадр.
    /// </summary>
    public event Action Changed;

    protected void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Подписаться на потоки документов. Зовётся один раз при старте игры
    /// (ProjectionStore.SubscribeAll), поэтому тяжёлую работу тут делать можно,
    /// а вот отписываться потом никто не будет — проекции живут всю партию.
    /// </summary>
    public abstract void Subscribe(EventStore events);
}

/// <summary>
/// Все проекции игры, разложенные по типу.
///
/// ЗАЧЕМ. Единая точка, где видно, какое производное состояние вообще есть в игре,
/// и единственный способ до него добраться: Projections.Get&lt;StockpileProjection&gt;().
///
/// Устроен так же, как EventStore, только хранит не потоки, а проекции — отсюда
/// одинаковый суффикс Store у обоих: типизированный контейнер с поиском по ключу.
///
/// ПОРЯДОК ДОБАВЛЕНИЯ ЗНАЧИМ. Если одна проекция читает другую, зависимость должна быть
/// добавлена раньше. Держим это явным списком в GameManager, а не полагаемся на удачу.
/// </summary>
public sealed class ProjectionStore
{
    private readonly Dictionary<Type, Projection> _projections = new();

    /// <summary>Зарегистрировать проекцию. Повторная регистрация типа — ошибка, а не тихая замена.</summary>
    public void Add<T>(T projection) where T : Projection
    {
        if (_projections.ContainsKey(typeof(T)))
            throw new InvalidOperationException($"Проекция {typeof(T).Name} уже зарегистрирована");

        _projections[typeof(T)] = projection;
    }

    /// <summary>Достать проекцию по типу. Вернёт null, если её забыли зарегистрировать.</summary>
    public T Get<T>() where T : Projection =>
        _projections.TryGetValue(typeof(T), out var projection) ? (T)projection : null;

    /// <summary>
    /// Подключить все проекции к журналу. Отдельным шагом после регистрации,
    /// чтобы проекции могли ссылаться друг на друга: сначала собрали состав, потом подписали.
    /// </summary>
    public void SubscribeAll(EventStore events)
    {
        foreach (var projection in _projections.Values)
            projection.Subscribe(events);
    }
}
