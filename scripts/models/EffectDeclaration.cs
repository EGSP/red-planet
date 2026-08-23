using Godot;

/// <summary>
/// Общая часть узлов, объявляющих эффект в сцене модели: ссылка на сцену, множитель размера
/// и предпросмотр в редакторе. Сами поводы у наследников разные — гибель
/// (<see cref="DeathEffect"/>), попадание снаряда (<see cref="ImpactEffect"/>), — а вот
/// объявление у них устроено одинаково, и повторять его в каждом означало бы, что правка
/// предпросмотра идёт дважды.
///
/// ССЫЛКА, А НЕ ЭКЗЕМПЛЯР. Модель освобождается вместе с сущностью, а эффект обязан её
/// пережить либо сыграть там, где сущности нет вовсе; к тому же сцена эффекта держит
/// несколько узлов частиц, и копия их в каждой из сотен машин стоила бы памяти при том,
/// что до повода она не нужна. Поэтому узел объявляет только ссылку и размер, а поднимает
/// и проигрывает сцену <see cref="EffectSystem"/> из общего набора готовых экземпляров.
///
/// СОБСТВЕННОЙ ЛОГИКИ НЕТ — как и у <see cref="ModelTool"/>, который не разыскивает своё
/// определение оружия. Узел не знает ни повода, ни слоя эффектов, ни мира вокруг.
/// </summary>
[Tool]
public abstract partial class EffectDeclaration : Node2D
{
    private PackedScene _effect;
    private float _size = 1f;
    private bool _preview = true;

    /// <summary>
    /// Сцена эффекта с корнем <see cref="BurstParticles"/>. Не назначена — играется общий
    /// эффект этого повода, объявленный у <see cref="EffectSystem"/>.
    /// </summary>
    [Export]
    public PackedScene Effect
    {
        get => _effect;
        set { _effect = value; Rebuild(); }
    }

    /// <summary>
    /// Множитель размера эффекта. Сцена одна на многие виды, а машины и снаряды различаются
    /// величиной втрое и более, поэтому размер задаётся здесь, где эффект виден рядом с тем,
    /// чему он принадлежит. Множитель распространяется и на отметину копоти: она объявлена
    /// внутри сцены эффекта и растёт вместе с ним.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,8,0.05,or_greater")]
    public float Size
    {
        get => _size;
        set { _size = value; Rebuild(); }
    }

    /// <summary>
    /// Показывать ли эффект в редакторе. На игру не влияет: предпросмотр поднимается
    /// только при <see cref="Engine.IsEditorHint"/>.
    ///
    /// ЗАЧЕМ. Размер подбирается единственным способом — на глаз рядом с тем, чему эффект
    /// принадлежит. Без предпросмотра множитель пришлось бы угадывать, запускать партию
    /// и смотреть, что вышло.
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

    /// <summary>Цвет отметки места в редакторе. Наследник различает себя им же.</summary>
    protected virtual Color Mark => new(1f, 0.45f, 0.2f, 0.7f);

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
        var mark = Mark;

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
