using Godot;

/// <summary>
/// Полоса предстоящих событий у верхнего края, вплотную над полосой ресурсов.
///
/// ПОЧЕМУ НАД ЭКОНОМИКОЙ, А НЕ В УГЛУ. Срок ближайшей волны и состояние экономики читаются
/// вместе: игрок смотрит на запас металла ровно затем, чтобы решить, успеет ли он что-либо
/// построить до волны. Правый верхний угол занят <see cref="TerrorBar"/>, и полоса событий
/// там оказалась бы рядом со сведениями другого рода — с разбором давления по слагаемым,
/// то есть с настройкой, а не с решением.
///
/// ВЫСОТА СВЕДЕНА К ОДНОЙ СТРОКЕ. Заголовка над дорожкой нет, а срок ближайшей волны стоит
/// в той же строке справа от неё: два блока у верхнего края и без того отнимают заметную
/// часть экрана, и полоса событий не должна прибавлять к этому третью строку.
///
/// Отдельный слой, как и прочие постоянные блоки: показ не зависит ни от выделения,
/// ни от того, чем игрок занят.
///
/// Сама шкала со всеми отметками нарисована в <see cref="EventTrack"/>: здесь только
/// размещение и срок словами.
/// </summary>
public partial class EventBar : CanvasLayer
{
    private static readonly Color WaveColor = new(1f, 0.62f, 0.45f);

    private const int TrackHeight = 18;

    /// <summary>Место под срок справа от дорожки, пикселей.</summary>
    private const int CountdownWidth = 52;

    /// <summary>
    /// Сколько прошедшего времени остаётся на шкале, секунд.
    ///
    /// Величина небольшая по замыслу: прошлое здесь нужно лишь затем, чтобы отметка волны
    /// не исчезала в тот же миг, когда волна вышла на карту, и игрок успевал связать
    /// увиденное на карте с отметкой на полосе.
    /// </summary>
    [Export] public float PastSeconds = 10f;

    /// <summary>
    /// Насколько далеко вперёд смотрит шкала, секунд.
    ///
    /// Задаётся отдельно от <see cref="WaveSettings.ChillInterval"/> намеренно, хотя
    /// подбирается по нему: полоса показывает не только волны, и связывать её длину
    /// с одним источником событий значило бы перенастраивать её при каждой правке отдыха.
    /// </summary>
    [Export] public float FutureSeconds = 120f;

    /// <summary>
    /// Террор, при котором ромб волны имеет наименьший размер.
    ///
    /// Границы размера заданы здесь, а не выведены из содержимого, потому что вывести их
    /// не из чего: у волн задана только нижняя граница применимости, верхняя у всех
    /// оставлена безразличной. Наибольший показатель партии есть величина настройки,
    /// а не свойство отдельной волны.
    /// </summary>
    [Export] public float TerrorAtSmallest;

    /// <summary>Террор, при котором ромб волны имеет наибольший размер.</summary>
    [Export] public float TerrorAtLargest = 500f;

    private EventTrack _track;
    private Label _countdown;
    private PanelContainer _panel;

    /// <summary>Нижний край полосы: по нему выравнивается <see cref="ResourceBar"/>.</summary>
    public float BottomEdge => HudLayout.BottomOf(_panel);

    public override void _Ready()
    {
        var frame = new UiFrame();
        AddChild(frame);

        // Раскладка та же, что у полосы ресурсов: якорь на контейнере нулевого размера
        // не работает, прижимать надо цепочкой контейнеров от каркаса
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        frame.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_top", HudLayout.TopMargin);
        column.AddChild(margin);

        var center = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        margin.AddChild(center);

        _panel = new PanelContainer();
        center.AddChild(_panel);

        // Ширина задана всей строке, а дорожка растягивается по остатку: так блок совпадает
        // по ширине с полосой ресурсов под ним независимо от места, занятого сроком
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(HudLayout.CenterWidth, 0) };
        row.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(row);

        _track = new EventTrack
        {
            CustomMinimumSize = new Vector2(0, TrackHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddChild(_track);

        // Срок стоит справа от дорожки, а не подписью под ней: подпись прибавила бы строку,
        // тогда как справа остаётся место в той же строке
        _countdown = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(CountdownWidth, 0),
        };
        _countdown.AddThemeFontSizeOverride("font_size", 11);
        _countdown.AddThemeColorOverride("font_color", WaveColor);
        row.AddChild(_countdown);
    }

    public override void _Process(double delta)
    {
        var waves = GameManager.I?.System<WaveSystem>();

        if (waves == null)
        {
            _countdown.Text = "";
            return;
        }

        // Настройки читаются каждый кадр, а не только при создании: их правят ползунком
        // в инспекторе при запущенной игре, и полоса должна отвечать сразу
        _track.PastSeconds = PastSeconds;
        _track.FutureSeconds = FutureSeconds;
        _track.TerrorAtSmallest = TerrorAtSmallest;
        _track.TerrorAtLargest = TerrorAtLargest;

        int left = Mathf.CeilToInt(waves.TimeLeft);
        _countdown.Text = $"{left / 60}:{left % 60:00}";
    }
}
