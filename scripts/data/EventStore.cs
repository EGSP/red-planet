using System;
using System.Collections.Generic;

/// <summary>
/// Негенерик-лицо потока. Нужен ровно для одного: сложить разнотипные потоки
/// в один словарь внутри EventStore — обобщённые типы напрямую туда не положишь.
/// Наружу им не пользуются.
/// </summary>
public interface IEventStream
{
    bool IsTransient { get; }
    void Clear();
}

/// <summary>
/// Поток документов одного типа — append-only список.
///
/// ЗАЧЕМ ДВА СПОСОБА ЧИТАТЬ. У потока две разные роли, и путать их не надо:
/// 1. Appended — подписка. Так работают проекции: обновляются дельтой в тот же миг,
///    когда документ дописан. Ничего не хранят про историю, поэтому чистка потока их не ломает.
/// 2. Records — список за текущий кадр. Для систем, которым удобнее разобрать всё пачкой
///    (например, заспавнить по одному эффекту на каждое событие кадра).
///
/// Дозапись только через Add, и только сам поток проставляет SequenceId —
/// иначе нумерация разъедется и курсоры потребителей перестанут работать.
/// </summary>
public sealed class EventStream<T> : IEventStream where T : IEventRecord
{
    private readonly List<T> _records = new();
    private int _lastSequenceId;

    /// <summary>
    /// Срабатывает сразу при дозаписи документа. Основной способ для проекций:
    /// подписался один раз при старте — дальше состояние поддерживается само.
    /// </summary>
    public event Action<T> Appended;

    /// <summary>Документы, накопившиеся в потоке. У транзиентных — только за текущий кадр.</summary>
    public IReadOnlyList<T> Records => _records;

    /// <summary>
    /// Чистится ли поток в конце кадра. Считается один раз на тип по атрибуту TransientEvent —
    /// на каждый тип потока существует ровно один экземпляр, так что отражение тут не в тягость.
    /// </summary>
    public bool IsTransient { get; } =
        Attribute.IsDefined(typeof(T), typeof(TransientEventAttribute));

    /// <summary>Дописать документ: присвоить номер, сохранить и оповестить подписчиков.</summary>
    public void Add(T record)
    {
        record.SequenceId = ++_lastSequenceId;
        _records.Add(record);
        Appended?.Invoke(record);
    }

    /// <summary>
    /// Выбросить накопленное. Нумерация НЕ сбрасывается: SequenceId остаётся сквозным,
    /// чтобы курсоры потребителей не спутали новую запись со старой.
    /// </summary>
    public void Clear() => _records.Clear();
}

/// <summary>
/// Журнал всех документов игры: потоки, разложенные по типу документа.
///
/// ЗАЧЕМ. Единственная шина, через которую системы говорят друг с другом,
/// и единственный источник, из которого проекции набирают своё состояние.
///
/// КАК ПРИМЕНЯТЬ.
/// - Опубликовать факт:            store.Append(new ResourceSpent { ... });
/// - Подписаться (в проекции):     store.Stream&lt;ResourceSpent&gt;().Appended += OnSpent;
/// - Разобрать пачкой за кадр:     store.Stream&lt;OreDepleted&gt;().Records
///
/// Индексируется самим типом документа, поэтому регистрировать потоки заранее не нужно:
/// первый, кто спросил или дописал, тот и создал поток.
///
/// СЕЙЧАС ЭТО ШИНА В ПРЕДЕЛАХ КАДРА. Все документы проекта помечены TransientEvent,
/// то есть журнал вычищается каждый кадр и историю не хранит. Долговременный лог понадобится
/// для сохранений — тогда часть потоков перестанет быть транзиентной, а к ним добавится
/// усечение по снапшоту (см. docs/system-design.md).
/// </summary>
public sealed class EventStore
{
    private readonly Dictionary<Type, IEventStream> _streams = new();

    /// <summary>Поток документов этого типа, создаётся при первом обращении.</summary>
    public EventStream<T> Stream<T>() where T : IEventRecord
    {
        if (!_streams.TryGetValue(typeof(T), out var stream))
        {
            stream = new EventStream<T>();
            _streams[typeof(T)] = stream;
        }
        return (EventStream<T>)stream;
    }

    /// <summary>
    /// Зафиксировать свершившийся факт. Имя именно Append, а не Publish:
    /// журнал — append-only лог, документ дописывается в конец и больше не меняется.
    /// </summary>
    public void Append<T>(T record) where T : IEventRecord => Stream<T>().Add(record);

    /// <summary>
    /// Конец кадра: выбросить всё, что помечено TransientEvent.
    /// Вызывает планировщик один раз за кадр — руками звать не нужно.
    ///
    /// Проекции от этого не страдают: они инкрементальные и из истории не пересчитываются.
    /// Это и есть причина, по которой чистка вообще безопасна.
    /// </summary>
    public void ClearTransient()
    {
        foreach (var stream in _streams.Values)
            if (stream.IsTransient)
                stream.Clear();
    }
}
