using Godot;

/// <summary>
/// Луч, тянущийся от инструмента к тому, над чем он работает: строительный луч, а впредь
/// и лазер. Узел лежит в сцене модели потомком <see cref="ModelTool"/> и потому знает, где
/// у луча начало; конец ему каждый кадр называет носитель.
///
/// ЧТО ЭТОТ УЗЕЛ ЗАДАЁТ, А ЧТО НЕТ. Он задаёт место начала и вид (<see cref="BeamStyle"/>).
/// Он не решает, идёт ли работа, и не знает ни цели, ни носителя: об этом ему сообщают
/// вызовы <see cref="Strike"/> и <see cref="Release"/>. То же разделение, что у вспышки
/// выстрела, — узел изображения не разыскивает события сам.
///
/// НАЧАЛО ЛУЧА ЕСТЬ ПОЛОЖЕНИЕ САМОГО УЗЛА. Отдельной ссылки на точку выхода нет намеренно:
/// художник ставит узел туда же, где стоит <see cref="ModelTool.Muzzle"/>, и поворот руки
/// уносит луч вместе с собой, поскольку узел ей потомок.
///
/// ПРЕДПРОСМОТР В РЕДАКТОРЕ. Поле <see cref="PreviewTarget"/> указывает на обычный
/// <see cref="Marker2D"/> той же сцены: в редакторе луч тянется к нему, и художник
/// растягивает его мышью, не запуская игру. Своей обработки мыши узел не заводит — маркер
/// таскается штатными средствами двумерного редактора. В игре поле не читается вовсе.
/// </summary>
[Tool]
[GlobalClass]
public partial class BeamVisual : Node2D
{
    /// <summary>Вид луча. Не назначен — узел не рисует ничего.</summary>
    [Export] public BeamStyle Style { get; set; }

    /// <summary>
    /// Куда тянуть луч В РЕДАКТОРЕ. Обычный узел той же сцены; на игру не влияет.
    /// </summary>
    [Export] public Node2D PreviewTarget { get; set; }

    /// <summary>Показывать ли предпросмотр. Только в редакторе — см. заголовок класса.</summary>
    [Export] public bool PreviewInEditor { get; set; } = true;

    /// <summary>Конец луча в мировых координатах.</summary>
    private Vector2 _target;

    /// <summary>Луч затребован в этом кадре. Снимается вызовом <see cref="Release"/>.</summary>
    private bool _held;

    /// <summary>Насколько луч разгорелся: от нуля до единицы. См. <see cref="BeamStyle.RiseTime"/>.</summary>
    private float _power;

    /// <summary>Отсчёт для пульсации и бега текстуры, секунд.</summary>
    private float _time;

    private Material _applied;

    /// <summary>Луч сейчас нарисован. Нужен ради одной последней перерисовки при гашении.</summary>
    private bool _lit;

    /// <summary>
    /// Тянуть луч к точке. Зовётся каждый кадр, пока работа идёт: точка движется вместе
    /// с целью, а её же сохранение снимает нужду в ссылке на саму цель — узел изображения
    /// не должен держать ссылок на сущности мира.
    /// </summary>
    public void Strike(Vector2 worldPoint)
    {
        _target = worldPoint;
        _held = true;
    }

    /// <summary>Погасить луч. Гаснет не мгновенно — см. <see cref="BeamStyle.FadeTime"/>.</summary>
    public void Release() => _held = false;

    public override void _Ready()
    {
        // Видимостью луч не управляется — пока работы нет, узел просто не отдаёт команд
        // рисования. Признак снимается лишь затем, чтобы луч, спрятанный в сцене при
        // подборе вида, не остался невидимым в игре. В редакторе не трогаем вовсе:
        // видимость есть сохраняемое свойство узла, и запись в неё попала бы в файл
        if (!Engine.IsEditorHint())
            Visible = true;
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            Preview();

        Sync();

        float target = _held && Style != null ? 1f : 0f;
        float span = _held ? Style?.RiseTime ?? 0f : Style?.FadeTime ?? 0f;

        _power = span > 0f
            ? Mathf.MoveToward(_power, target, (float)delta / span)
            : target;

        if (_power <= 0f)
        {
            // Погасший луч перерисовывается ровно один раз: холст держит последние
            // отданные команды, и без этого на экране осталась бы прошлая картинка
            if (!_lit)
                return;

            _lit = false;
            QueueRedraw();
            return;
        }

        _lit = true;
        _time += (float)delta;
        QueueRedraw();
    }

    /// <summary>
    /// Предпросмотр тянет луч к маркеру.
    ///
    /// ПОЧЕМУ МАЛО ОДНОГО <see cref="Engine.IsEditorHint"/>. Экземпляры моделей поднимаются
    /// и вне открытой сцены — в иконке панели строительства и в поле редактора контента, —
    /// и там луч был бы виден на всяком строителе без всякой работы. Поэтому предпросмотр
    /// разрешён только тому узлу, который принадлежит СЕЙЧАС ОТКРЫТОЙ сцене.
    /// </summary>
    private void Preview()
    {
        if (!PreviewInEditor || PreviewTarget == null || !Alive.Is(PreviewTarget))
        {
            _held = false;
            return;
        }

        var edited = GetTree()?.EditedSceneRoot;

        if (edited == null || (edited != this && !edited.IsAncestorOf(this)))
        {
            _held = false;
            return;
        }

        Strike(PreviewTarget.GlobalPosition);
    }

    /// <summary>
    /// Согласовать узел с видом. Вещество принадлежит ресурсу, а стоит на узле, поэтому
    /// подмену ресурса приходится переносить сюда; сверка по ссылке стоит дешевле записи.
    /// </summary>
    private void Sync()
    {
        var material = Style?.Material;

        if (_applied == material)
            return;

        _applied = material;
        Material = material;

        // Повтор нужен бегущей текстуре: без него участок за краем изображения тянется
        // краевым пикселем вместо следующего повтора рисунка
        TextureRepeat = Style?.Texture != null
            ? TextureRepeatEnum.Enabled
            : TextureRepeatEnum.ParentNode;
    }

    /// <summary>
    /// Тело луча и вспышки на концах. Рисуется в местных координатах узла, поэтому поворот
    /// руки учитывается сам собой, а цель переводится в них через <see cref="Node2D.ToLocal"/>.
    /// </summary>
    public override void _Draw()
    {
        if (Style == null || _power <= 0f)
            return;

        var to = ToLocal(_target);
        float length = to.Length();

        if (length < 1f)
            return;

        // Пульсация — свойство изображения, а не работы: толщина колеблется вокруг заданной,
        // и от разгорания зависит только непрозрачность
        float pulse = 1f + Style.PulseDepth * Mathf.Sin(_time * Style.PulseSpeed * Mathf.Tau);

        var color = Style.Color;
        var glow = color with { A = color.A * Style.GlowAlpha * _power };
        var core = color.Lerp(Colors.White, 0.55f) with { A = color.A * _power };

        if (Style.Texture != null)
            Body(to, length, glow with { A = color.A * _power });
        else if (Style.GlowWidth > 0f)
            ShapeDraw.Line(this, Vector2.Zero, to,
                ShapeStyle.Outline(glow, Style.GlowWidth * pulse, WidthMode.MinScreen));

        if (Style.CoreWidth > 0f)
            ShapeDraw.Line(this, Vector2.Zero, to,
                ShapeStyle.Outline(core, Style.CoreWidth * pulse, WidthMode.MinScreen));

        Cap(Vector2.Zero, Style.MuzzleRadius * pulse, core, glow);
        Cap(to, Style.ImpactRadius * pulse, core, glow);
    }

    /// <summary>
    /// Тело луча текстурой: прямоугольник во всю длину, повёрнутый вдоль неё. Рисунок
    /// повторяется по длине и бежит от среза к цели, поэтому высота текстуры приходится
    /// на толщину свечения, а ширина задаёт шаг повтора.
    /// </summary>
    private void Body(Vector2 to, float length, Color tint)
    {
        float width = Mathf.Max(Style.GlowWidth, 1f);
        var size = Style.Texture.GetSize();

        if (size.X <= 0f || size.Y <= 0f)
            return;

        // Сколько текселей текстуры приходится на пиксель мира: высота растянута на толщину,
        // и тот же множитель обязан действовать вдоль, иначе рисунок сомнётся
        float scale = size.Y / width;
        float offset = -_time * Style.TextureScroll * scale;

        DrawSetTransform(Vector2.Zero, to.Angle());
        DrawTextureRectRegion(Style.Texture,
            new Rect2(0f, -width * 0.5f, length, width),
            new Rect2(offset, 0f, length * scale, size.Y),
            tint);
        DrawSetTransform(Vector2.Zero);
    }

    /// <summary>Вспышка на конце луча: ядро внутри, свечение вокруг.</summary>
    private void Cap(Vector2 at, float radius, Color core, Color glow)
    {
        if (radius <= 0f)
            return;

        ShapeDraw.Circle(this, at, radius, ShapeStyle.Solid(glow));
        ShapeDraw.Circle(this, at, radius * 0.45f, ShapeStyle.Solid(core));
    }
}
