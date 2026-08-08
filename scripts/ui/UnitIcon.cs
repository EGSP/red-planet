using Godot;

/// <summary>
/// Изображение юнита в интерфейсе: тот же силуэт, что и на карте, вписанный в отведённый
/// квадрат и повёрнутый носом влево.
///
/// ПОЧЕМУ НОСОМ ВЛЕВО. Иконка стоит слева от названия, и машина, обращённая к подписи,
/// читается как одно целое с нею. Обращённая от подписи выглядела бы уезжающей за край
/// ячейки. В мире нос направлен по +X, поэтому изображение развёрнуто на половину оборота.
///
/// РАЗМЕР НЕ ЗАВИСИТ ОТ РАЗМЕРА МАШИНЫ. Силуэт вписывается в квадрат целиком, каким бы
/// ни был <see cref="UnitDefinition.RadiusPx"/>: ячейки панели одинаковы, и крупная машина,
/// нарисованная крупнее, вылезала бы за края, а мелкая терялась бы точкой посередине.
/// </summary>
public partial class UnitIcon : Control
{
    /// <summary>Доля половины стороны, которую занимает силуэт. Остаток — поля вокруг.</summary>
    private const float Fill = 0.92f;

    private UnitDefinition _definition;

    public UnitDefinition Definition
    {
        get => _definition;
        set
        {
            _definition = value;
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        // Ввод целиком достаётся кнопке, внутри которой стоит иконка
        MouseFilter = MouseFilterEnum.Ignore;

        // Силуэт вписан в текущий размер, а размер назначает раскладка уже после создания
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        if (_definition == null)
            return;

        float half = Mathf.Min(Size.X, Size.Y) * 0.5f;
        if (half <= 0f)
            return;

        // Радиус корпуса меньше видимого размера: части силуэта, броня и ствол выходят
        // за него, поэтому вписывается не радиус, а полное удаление крайней точки
        float extent = UnitSilhouette.Extent(_definition, _definition.RadiusPx);
        if (extent <= 0f)
            return;

        float radius = _definition.RadiusPx * (half * Fill / extent);

        UnitSilhouette.Draw(this, _definition, radius, toolLocal: 0f,
            origin: Size * 0.5f, angle: Mathf.Pi);
    }
}
