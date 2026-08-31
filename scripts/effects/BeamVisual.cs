using System.Collections.Generic;
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
/// САМ УЗЕЛ НИЧЕГО НЕ РИСУЕТ. Прежде он выводил полосу и вспышки в собственном <c>_Draw</c>,
/// и на каждый работающий инструмент приходился свой список команд отрисовки. Теперь узел
/// лишь ведёт своё состояние — точку конца и разгорание, — а изображение всех лучей мира
/// собирает <see cref="BeamSystem"/> в множественные сетки. Отсюда и учёт: узел вносит себя
/// в перечень при входе в дерево и снимает при выходе.
///
/// НАЧАЛО ЛУЧА ЕСТЬ ПОЛОЖЕНИЕ САМОГО УЗЛА. Отдельной ссылки на точку выхода нет намеренно:
/// художник ставит узел туда же, где стоит <see cref="ModelTool.Muzzle"/>, и поворот руки
/// уносит луч вместе с собой, поскольку узел ей потомок.
///
/// ПРЕДПРОСМОТР В РЕДАКТОРЕ. Поле <see cref="PreviewTarget"/> указывает на обычный
/// <see cref="Marker2D"/> той же сцены: в редакторе луч тянется к нему, и художник
/// растягивает его мышью, не запуская игру. Рисуется предпросмотр тем же
/// <see cref="BeamPainter"/>, что и лучи в игре, поэтому показывает он ровно то, что будет
/// видно в партии, — вплоть до шейдеров и бега текстуры.
/// </summary>
[Tool]
[GlobalClass]
public partial class BeamVisual : Node2D
{
    /// <summary>
    /// Все лучи, находящиеся в дереве игры. Перечень ведётся самими узлами, а читает его
    /// <see cref="BeamSystem"/>: лучи не являются сущностями мира и в <c>Index</c> не входят,
    /// а разыскивать их обходом дерева каждый кадр значило бы обходить все модели разом.
    ///
    /// Узлы редактора сюда не попадают: там ни сессии, ни системы нет.
    /// </summary>
    internal static readonly List<BeamVisual> Live = new();

    /// <summary>Вид луча. Не назначен — луч не рисуется.</summary>
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

    /// <summary>Отрисовщик предпросмотра. Только в редакторе; в игре лучи рисует система.</summary>
    private BeamPainter _preview;

    /// <summary>Вид, под который собран предпросмотр. Сменился — отрисовщик пересобирается.</summary>
    private BeamStyle _previewed;

    /// <summary>Начало луча в мировых координатах — положение самого узла.</summary>
    public Vector2 From => GlobalPosition;

    /// <summary>Конец луча в мировых координатах.</summary>
    public Vector2 To => _target;

    /// <summary>Насколько луч разгорелся. Ноль означает, что рисовать нечего.</summary>
    public float Power => _power;

    /// <summary>Собственный отсчёт луча: по нему идут пульсация и бег рисунка.</summary>
    public float Time => _time;

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

    /// <summary>
    /// Продвинуть разгорание и собственный отсчёт. В игре зовётся системой лучей: порядок
    /// кадра задаёт планировщик, а не обход дерева нод.
    /// </summary>
    public void Advance(double delta)
    {
        float target = _held && Style != null ? 1f : 0f;
        float span = _held ? Style?.RiseTime ?? 0f : Style?.FadeTime ?? 0f;

        _power = span > 0f
            ? Mathf.MoveToward(_power, target, (float)delta / span)
            : target;

        // Отсчёт идёт только у горящего луча: у погасшего он всё равно ни на что не влияет,
        // а без остановки набегал бы за партию до величин, теряющих точность
        if (_power > 0f)
            _time += (float)delta;
    }

    public override void _EnterTree()
    {
        if (!Engine.IsEditorHint())
            Live.Add(this);
    }

    public override void _ExitTree()
    {
        Live.Remove(this);
        DropPreview();
    }

    public override void _Ready()
    {
        // Видимостью луч не управляется: пока работы нет, система просто не отдаёт его сеткам
        // ни одного экземпляра. Признак снимается лишь затем, чтобы луч, спрятанный в сцене
        // при подборе вида, не остался невидимым в игре. В редакторе не трогаем вовсе:
        // видимость есть сохраняемое свойство узла, и запись в неё попала бы в файл
        if (!Engine.IsEditorHint())
            Visible = true;
        else
            SetProcess(true);
    }

    /// <summary>
    /// Кадр предпросмотра. В игре обработка не ведётся вовсе: и отсчёт, и отрисовку
    /// закрывает <see cref="BeamSystem"/>.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            SetProcess(false);
            return;
        }

        Preview();
        Advance(delta);

        if (!ReferenceEquals(_previewed, Style))
        {
            DropPreview();
            _previewed = Style;
        }

        if (Style == null)
            return;

        _preview ??= new BeamPainter(this, Style);
        _preview.Add(From, To, _power, _time);
        _preview.Flush();
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

    private void DropPreview()
    {
        _preview?.Discard();
        _preview = null;
    }
}
