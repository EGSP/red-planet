using Godot;

/// <summary>
/// Подложка под постройкой: площадка, отделяющая корпус от поверхности.
///
/// ПОЧЕМУ НЕ В НАКОПИТЕЛЬНОМ СЛОЕ СЛЕДОВ. Подложка привязана к живой сущности и обязана
/// исчезать вместе с ней, а из накопительного буфера ничего не стирается. Поэтому она
/// рисуется самой постройкой, а след разрушенного здания — уже другое изображение, и вот
/// он останется навсегда.
///
/// ПОЧЕМУ ИЗОБРАЖЕНИЕ РАСТЯГИВАЕТСЯ, А НЕ УКЛАДЫВАЕТСЯ КРАТНО. Постройки имеют разные и не
/// кратные друг другу размеры, поэтому кратная укладка потребовала бы либо набора
/// изображений под каждый размер, либо видимых обрезков по краю. Растяжение искажает рисунок
/// панелей, но искажение это равномерное и на глаз читается как разный масштаб площадок,
/// а не как дефект.
/// </summary>
public static class BuildingSkirt
{
    private const string TexturePath =
        "res://assets/sprites/terrain/common/metal_decal_06_diffuse.png";

    /// <summary>Насколько площадка выступает за корпус, пикселей.</summary>
    private const float MarginPx = Const.Unit * 0.3f;

    /// <summary>Непрозрачность: площадка есть подложка, а не самостоятельная деталь.</summary>
    private const float Opacity = 0.7f;

    private static Texture2D _texture;
    private static bool _missing;

    /// <summary>
    /// Нарисовать площадку под корпусом. Прямоугольник задаётся в тех же координатах, что и
    /// корпус, поэтому поворот, выставленный вызывающим, действует и на неё.
    /// </summary>
    public static void Draw(CanvasItem canvas, Rect2 body)
    {
        if (_missing)
            return;

        if (_texture == null)
        {
            _texture = GD.Load<Texture2D>(TexturePath);
            _missing = _texture == null;

            if (_missing)
                return;
        }

        canvas.DrawTextureRect(_texture, body.Grow(MarginPx), false,
            new Color(1f, 1f, 1f, Opacity));
    }
}
