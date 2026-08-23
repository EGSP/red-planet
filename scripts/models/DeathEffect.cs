using Godot;

/// <summary>
/// Объявление взрыва в сцене модели: «эта машина при гибели взрывается такой сценой,
/// в этом месте корпуса и с такой задержкой».
///
/// ССЫЛКА, А НЕ ЭКЗЕМПЛЯР, И ПРИЧИН ТОМУ ДВЕ. Первая: модель освобождается вместе
/// с сущностью, а взрыв обязан её пережить — экземпляр, лежащий в дереве погибшего,
/// исчез бы ровно в тот миг, когда его следует показать. Вторая: сцена взрыва держит
/// несколько узлов частиц, и копия их в каждой из сотен машин стоила бы памяти и времени
/// поднятия при том, что до гибели она не нужна вовсе. Поэтому узел объявляет лишь место
/// и ссылку, а поднимает и проигрывает сцену <see cref="EffectSystem"/> из общего набора
/// готовых экземпляров.
///
/// НЕСКОЛЬКО УЗЛОВ НА МОДЕЛЬ ДОПУСТИМЫ И ОСМЫСЛЕННЫ. Крупная постройка, разлетающаяся
/// одной вспышкой в геометрическом центре, выглядит меньше себя самой; несколько узлов
/// с разными задержками дают серию взрывов по корпусу.
///
/// СОБСТВЕННОЙ ЛОГИКИ НЕТ — как и у <see cref="ModelTool"/>, который не разыскивает своё
/// определение оружия. Узел не знает ни о гибели, ни о слое эффектов; повод и место
/// в мире известны только системе.
///
/// ЕСЛИ УЗЛА В МОДЕЛИ НЕТ, вид всё равно взрывается: <see cref="EffectSystem"/> играет
/// общий взрыв из своего поля <c>Explosion</c> в точке гибели. Объявление здесь есть
/// переопределение общего, а не единственный способ его получить.
/// </summary>
[Tool, GlobalClass]
public partial class DeathEffect : Node2D
{
    private PackedScene _effect;
    private float _size = 1f;
    private bool _preview = true;

    /// <summary>
    /// Сцена взрыва с корнем <see cref="BurstParticles"/>. Не назначена — играется общий
    /// взрыв, объявленный у <see cref="EffectSystem"/>.
    /// </summary>
    [Export]
    public PackedScene Effect
    {
        get => _effect;
        set { _effect = value; Rebuild(); }
    }

    /// <summary>
    /// Множитель размера эффекта. Сцена взрыва одна на многие виды, а машины различаются
    /// величиной втрое и более, поэтому размер задаётся здесь, где корпус виден рядом
    /// с эффектом. Множитель распространяется и на отметину копоти: она объявлена внутри
    /// сцены взрыва и растёт вместе с ним.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,8,0.05,or_greater")]
    public float Size
    {
        get => _size;
        set { _size = value; Rebuild(); }
    }

    /// <summary>
    /// Через сколько секунд после гибели играть. Нужна серии взрывов у крупной постройки:
    /// одновременные вспышки в разных точках корпуса читаются как одна большая, а
    /// разнесённые на доли секунды — как разрушение.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.05,or_greater")] public float Delay { get; set; }

    /// <summary>
    /// Показывать ли эффект в редакторе. На игру не влияет: предпросмотр поднимается
    /// только при <see cref="Engine.IsEditorHint"/>.
    ///
    /// ЗАЧЕМ. Размер взрыва подбирается единственным способом — на глаз рядом с корпусом,
    /// которому он принадлежит. Без предпросмотра множитель размера пришлось бы угадывать,
    /// запускать партию, терять машину и смотреть, что вышло.
    ///
    /// Поднятый в редакторе экземпляр владельца не получает, поэтому в файл сцены модели
    /// не попадает и на сохранение не влияет.
    /// </summary>
    [Export]
    public bool ShowPreview
    {
        get => _preview;
        set { _preview = value; Rebuild(); }
    }

    private Node2D _instance;
    private bool _queued;

    public override void _Ready() => Rebuild();

    /// <summary>
    /// Предпросмотр снимается на время сохранения сцены. Владельца у него нет, то есть
    /// в файл он не попал бы и так, но редактор во время сохранения блокирует дерево,
    /// и менять его состав в этот миг нельзя.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationEditorPreSave)
            Drop();
        else if (what == NotificationEditorPostSave)
            Rebuild();
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint() || _instance != null)
            return;

        // Эффект не назначен либо предпросмотр снят — остаётся отметить само место,
        // иначе узел в редакторе неотличим от пустого места
        var mark = new Color(1f, 0.45f, 0.2f, 0.7f);

        DrawArc(Vector2.Zero, 8f * Size, 0f, Mathf.Tau, 24, mark, 1.5f);
        DrawLine(new Vector2(-5f, 0f), new Vector2(5f, 0f), mark, 1.5f);
        DrawLine(new Vector2(0f, -5f), new Vector2(0f, 5f), mark, 1.5f);
    }

    public override void _ExitTree() => Drop();

    /// <summary>
    /// Заказать пересборку предпросмотра.
    ///
    /// ПОЧЕМУ ОТЛОЖЕННО. Пересборка добавляет узел в дерево, а зовётся она в том числе
    /// из правки поля в инспекторе и из уведомления об окончании сохранения — в оба этих
    /// мига редактор дерево держит занятым, и синхронный <c>AddChild</c> отвергается
    /// с «Parent node is busy setting up children». Отложенный вызов приходит тогда, когда
    /// дерево свободно.
    ///
    /// Признак заказа снимает повтор: правка нескольких полей подряд означает одну
    /// пересборку, а не по одной на каждое поле.
    /// </summary>
    private void Rebuild()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree() || _queued)
            return;

        _queued = true;
        Callable.From(Apply).CallDeferred();
    }

    /// <summary>
    /// Собственно пересборка. Повтор проигрывания задаёт сама сцена эффекта признаком
    /// <see cref="BurstParticles.PreviewLoop"/> — здесь достаточно поднять её и придать
    /// заданный размер.
    /// </summary>
    private void Apply()
    {
        _queued = false;

        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        Drop();
        QueueRedraw();

        if (!ShowPreview || Effect == null)
            return;

        if (Effect.Instantiate() is not Node2D instance)
            return;

        instance.Scale = Vector2.One * Size;
        _instance = instance;

        // Owner не назначается намеренно: предпросмотр принадлежит редактору,
        // а не сцене модели
        AddChild(instance);
    }

    private void Drop()
    {
        if (Alive.Is(_instance))
            _instance.QueueFree();

        _instance = null;
    }
}
