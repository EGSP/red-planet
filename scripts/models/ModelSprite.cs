using Godot;

/// <summary>
/// Одна нарисованная картинка запечённой модели: то, что в сцене задано узлом
/// <see cref="Sprite2D"/> либо <see cref="ModelShade"/>.
///
/// ЗАЧЕМ ОНА ЕСТЬ. Узел спрайта в игре ничего не решает: он держит текстуру, место
/// и порядок наложения, а меняются эти величины только при правке сцены. Поэтому в
/// экземпляре машины ему отвечает не узел, а объект отрисовки сервера
/// (см. <see cref="UnitModel.Realize"/>), созданный однажды и с тех пор не трогаемый.
/// Здесь хранится всё, что нужно для его создания.
///
/// КООРДИНАТЫ МЕСТНЫЕ ОТНОСИТЕЛЬНО ЯКОРЯ — корня модели либо той части-инструмента,
/// внутри которой спрайт лежал. Вложенность сцены при запекании перемножается: спрайт,
/// лежавший под тремя узлами подряд, получает одно преобразование и одного владельца.
/// </summary>
public readonly struct ModelSprite
{
    /// <summary>Изображение. У слоя затенения — уже испечённое из альфы источника.</summary>
    public readonly Texture2D Texture;

    /// <summary>
    /// Вещество вида. Ресурс общий на все экземпляры и НЕ размножается: величины, свои
    /// у каждой машины, — цвет команды и доля прочности — идут параметрами экземпляра
    /// шейдера, см. <see cref="InstanceParam"/>.
    /// </summary>
    public readonly Material Material;

    /// <summary>Преобразование относительно якоря: место, поворот, масштаб.</summary>
    public readonly Transform2D Local;

    /// <summary>
    /// Прямоугольник вывода в осях самого спрайта. Учтены центровка, сдвиг рисунка
    /// и отражение: отражённая картинка задаётся отрицательным размером.
    /// </summary>
    public readonly Rect2 Rect;

    /// <summary>Цвет: произведение <c>modulate</c> и <c>self_modulate</c> узла.</summary>
    public readonly Color Modulate;


    /// <summary>
    /// Способ увеличения текстуры. Хранится потому, что у корпуса стоит ближайший сосед
    /// ради резкости пиксельного рисунка, а у размытой тени — линейный.
    /// </summary>
    public readonly CanvasItem.TextureFilterEnum Filter;

    /// <summary>
    /// Отход слоя затенения в МИРОВЫХ осях. Ноль означает, что картинка стоит на месте
    /// и после создания не трогается вовсе.
    ///
    /// ЗАЧЕМ ОН ПЕРЕЖИЛ ЗАПЕКАНИЕ. Источник света на карте один и задан мировым углом,
    /// поэтому тень повёрнутой машины уходит в ту же сторону, что и тень неповёрнутой.
    /// Отсюда следует, что место тени зависит от угла корпуса и вычислить его заранее
    /// нельзя; вычисляется оно при развороте — см. <see cref="UnitModel.Align"/>.
    /// </summary>
    public readonly Vector2 Shift;

    /// <summary>
    /// Слой ли это затенения. Каркас строительства их не показывает: тень принадлежит
    /// стоящему корпусу — см. <see cref="UnitModel.DropShades"/>.
    /// </summary>
    public readonly bool Shade;

    public ModelSprite(Texture2D texture, Material material, Transform2D local, Rect2 rect,
        Color modulate, CanvasItem.TextureFilterEnum filter, Vector2 shift, bool shade)
    {
        Texture = texture;
        Material = material;
        Local = local;
        Rect = rect;
        Modulate = modulate;
        Filter = filter;
        Shift = shift;
        Shade = shade;
    }

    /// <summary>
    /// Чем картинка отличима от соседки для объединения вызовов отрисовки. По этому ключу
    /// раскладка и выдаёт уровень <c>z</c> — см. <see cref="MaterialOrderBalancer"/>.
    ///
    /// СОБСТВЕННОГО НОМЕРА УРОВНЯ КАРТИНКА НЕ ХРАНИТ. Раскладка пересобирается, когда
    /// испечён новый вид, и запомненный номер устарел бы; ключ же не меняется никогда.
    /// </summary>
    public MaterialOrderBalancer.Key Key => new(Material, Texture, Filter);

    /// <summary>Место картинки при заданном мировом угле якоря — с учётом отхода тени.</summary>
    public Transform2D Placed(float worldRotation) => Shift == Vector2.Zero
        ? Local
        : new Transform2D(Local.Rotation, Local.Scale, Local.Skew,
            Local.Origin + Shift.Rotated(-worldRotation));
}
