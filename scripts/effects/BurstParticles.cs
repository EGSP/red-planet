using System.Collections.Generic;
using Godot;

/// <summary>
/// Корень одноразового эффекта: вспышки выстрела, попадания, разлёта обломков. Держит
/// несколько узлов частиц и проигрывает их разом по вызову <see cref="Play"/>.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ КОРЕНЬ, А НЕ ОДИН УЗЕЛ ЧАСТИЦ. Вспышка почти всегда составная: ядро
/// живёт краткий миг и светится, пламя тянется дольше и гаснет в дым. Одним веществом
/// такое не выразить — у них разные сроки, размеры и кривые. Каждая часть остаётся своим
/// <see cref="GpuParticles2D"/> с полным разбором в инспекторе, а корень отвечает лишь
/// за то, чтобы они начинались одновременно.
///
/// ПОЧЕМУ НЕ САБ-ЭММИТЕР. Саб-эммитер решает другую задачу: частица порождает частицы —
/// искры, сыплющиеся со следа, дым от каждого обломка. Здесь же обе части выходят из одной
/// точки в один миг и друг от друга не зависят, поэтому связь через саб-эммитер лишь
/// подчинила бы срок пламени сроку ядра.
///
/// СОБСТВЕННОЙ ЛОГИКИ НЕТ. Узел не знает ни владельца, ни повода: когда проиграть,
/// решает <see cref="EffectSystem"/>.
/// </summary>
[Tool, GlobalClass]
public partial class BurstParticles : Node2D
{
    /// <summary>
    /// Проигрывать эффект по кругу В РЕДАКТОРЕ. На игру не влияет никак: повтор идёт
    /// только при <see cref="Engine.IsEditorHint"/>, а в запущенной игре узел этого поля
    /// не читает вовсе.
    ///
    /// ЗАЧЕМ. Одноразовый эффект в редакторе показывается ровно один раз — кнопкой
    /// перезапуска либо ручным включением выдачи, — и подбирать по такому предпросмотру
    /// цвет, срок и разлёт невозможно: каждая правка требует нового щелчка. Повтор
    /// превращает подбор в наблюдение за идущим эффектом.
    ///
    /// Признак сохраняется в сцену, как всякое поле <c>[Export]</c>, и это безвредно:
    /// оставленный включённым, в игре он ничего не делает.
    /// </summary>
    [Export] public bool PreviewLoop { get; set; }

    /// <summary>Через сколько секунд повторять предпросмотр. Только в редакторе.</summary>
    [Export(PropertyHint.Range, "0.1,5,0.1")] public float PreviewInterval { get; set; } = 1f;

    private GpuParticles2D[] _emitters;
    private ScorchStamp[] _stamps;
    private float _preview;

    /// <summary>
    /// Части эффекта. Собираются один раз при первом обращении: состав сцены во время
    /// игры не меняется.
    /// </summary>
    public GpuParticles2D[] Emitters => _emitters ??= Collect<GpuParticles2D>();

    /// <summary>
    /// Объявленные в эффекте отметины копоти. Сам узел их не ставит: собрать их — работа
    /// сцены, а поставить — <see cref="EffectSystem"/>, поскольку он один знает про слой
    /// отпечатков. Пустой набор означает эффект, следа не оставляющий.
    /// </summary>
    public ScorchStamp[] Stamps => _stamps ??= Collect<ScorchStamp>();

    /// <summary>
    /// В игре выдача выключается принудительно.
    ///
    /// ЗАЧЕМ. Художник включает выдачу прямо в сцене, чтобы увидеть эффект, и признак
    /// этот сохраняется в файл. Без сброса эффект отыграл бы сам собой при всяком поднятии
    /// сцены — в том числе в иконке панели строительства и в поле редактора контента,
    /// где никакого выстрела не было. Проигрывает эффект только тот, кто знает повод,
    /// то есть <see cref="EffectSystem"/>.
    /// </summary>
    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        foreach (var emitter in Emitters)
            if (Alive.Is(emitter))
                emitter.Emitting = false;

        SetProcess(false);
    }

    /// <summary>Повтор предпросмотра. В игре шаг снят в <see cref="_Ready"/>.</summary>
    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint() || !PreviewLoop)
            return;

        _preview -= (float)delta;

        if (_preview > 0f)
            return;

        _preview = Mathf.Max(PreviewInterval, 0.1f);
        Play();
    }

    /// <summary>
    /// Проиграть эффект с начала.
    ///
    /// Именно <c>Restart</c>, а не включение выдачи: части объявлены одноразовыми
    /// (<c>one_shot</c>), и повторный выстрел обязан начинать вспышку заново, не дожидаясь,
    /// пока догорит прежняя. Уже выпущенные частицы при этом сбрасываются — для вспышки
    /// это верно, поскольку она короче промежутка между выстрелами у любого ствола.
    /// </summary>
    public void Play()
    {
        foreach (var emitter in Emitters)
            if (Alive.Is(emitter))
                emitter.Restart();
    }

    private T[] Collect<T>() where T : Node
    {
        var found = new List<T>();
        Walk(this, found);
        return found.ToArray();
    }

    private static void Walk<T>(Node node, List<T> found) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                found.Add(match);

            Walk(child, found);
        }
    }
}
