using Godot;

/// <summary>
/// Изображение сущности в интерфейсе: та же модель, что и на карте, вписанная в отведённый
/// квадрат и повёрнутая носом влево.
///
/// ПОЧЕМУ ЭКЗЕМПЛЯР МОДЕЛИ, А НЕ ОТДЕЛЬНЫЙ РИСУНОК. Игрок опознаёт машину по очертанию,
/// и расхождение между панелью и полем означало бы, что заказывается одно, а выезжает другое.
/// Пока изображение задавалось ключом <c>sprite</c>, иконка рисовала его сама; со сцены
/// модели изображение стало деревом узлов, и повторить его командами отрисовки нельзя.
/// Поэтому иконка поднимает ту же сцену дочерним узлом и лишь вписывает её в свой квадрат.
///
/// ЦЕНА. Экземпляр сцены дороже отрисовки одной текстуры, но ячеек в панели десяток, и они
/// не пересоздаются каждый кадр: экземпляр живёт, пока у иконки не сменилось определение.
///
/// ПОЧЕМУ НОСОМ ВЛЕВО. Иконка стоит слева от названия, и машина, обращённая к подписи,
/// читается как одно целое с нею. Обращённая от подписи выглядела бы уезжающей за край
/// ячейки. В мире нос направлен по +X, поэтому изображение развёрнуто на половину оборота.
///
/// РАЗМЕР НЕ ЗАВИСИТ ОТ РАЗМЕРА МАШИНЫ. Изображение вписывается в квадрат целиком, каким бы
/// ни был габарит сущности: ячейки панели одинаковы, и крупная машина, нарисованная крупнее,
/// вылезала бы за края, а мелкая терялась бы точкой посередине.
/// </summary>
public partial class UnitIcon : Control
{
    /// <summary>Доля половины стороны, которую занимает изображение. Остаток — поля вокруг.</summary>
    private const float Fill = 0.92f;

    private UnitDefinition _definition;
    private UnitModel _model;

    public UnitDefinition Definition
    {
        get => _definition;
        set
        {
            _definition = value;
            SyncModel();
            QueueRedraw();
        }
    }

    public override void _Ready()
    {
        // Ввод целиком достаётся кнопке, внутри которой стоит иконка
        MouseFilter = MouseFilterEnum.Ignore;

        // Изображение вписано в текущий размер, а размер назначает раскладка уже после
        // создания
        Resized += QueueRedraw;

        SyncModel();
    }

    public override void _Draw()
    {
        if (_definition == null)
            return;

        float half = Mathf.Min(Size.X, Size.Y) * 0.5f;

        if (half <= 0f)
            return;

        if (Alive.Is(_model))
        {
            PlaceModel(half);
            return;
        }

        // Запасной круг для видов, у которых модели ещё нет
        UnitVisual.Draw(this, _definition, half * Fill, Size * 0.5f, Mathf.Pi);
    }

    /// <summary>
    /// Поставить экземпляр модели в середину ячейки и вписать его в неё. Вписывается удаление
    /// самой дальней точки, а не габарит по осям: изображение повёрнуто, и по осям оно заняло
    /// бы не тот прямоугольник, что был измерен.
    /// </summary>
    private void PlaceModel(float half)
    {
        float extent = _model.Extent();

        if (extent <= 0f)
            return;

        _model.Position = Size * 0.5f;
        _model.Rotation = Mathf.Pi;
        _model.Scale = Vector2.One * (half * Fill / extent);
    }

    /// <summary>
    /// Согласовать экземпляр сцены с определением. Экземпляр пересоздаётся только при смене
    /// пути к сцене: ячейка панели переназначается при каждой перестройке списка, и поднимать
    /// сцену заново на то же определение значило бы делать это на ровном месте.
    /// </summary>
    private void SyncModel()
    {
        if (!IsInsideTree())
            return;

        string wanted = _definition?.Model ?? "";

        if (Alive.Is(_model) && _model.Source == wanted)
            return;

        if (Alive.Is(_model))
            _model.QueueFree();

        _model = null;

        if (string.IsNullOrEmpty(wanted))
            return;

        var model = UnitModel.Realize(ModelBake.For(_definition));

        // Вне мира уровни раскладки не значат ничего: холст общий с интерфейсом,
        // и картинки машины легли бы поверх окружающих панелей
        model?.Flatten();

        if (model == null)
            return;

        // Подсказки принадлежат правке сцены, а не показу изображения
        model.ShowGizmo = false;

        foreach (var tool in model.Tools)
            tool.ShowGizmo = false;

        AddChild(model);
        model.ApplyTeamColor(TeamPalette.Player);
        model.SetToolFacing(0f);

        _model = model;
    }
}
