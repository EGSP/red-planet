using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Экран отчёта партии: графики наблюдаемых величин за всю её длительность.
///
/// Открывается из меню паузы и из меню исхода — то есть тогда, когда симуляция стоит.
/// Поэтому графики строятся один раз при открытии, а не каждый кадр: за время показа
/// ряды не пополняются, и перестроение было бы работой впустую.
///
/// ВЫБОРА КАНАЛОВ НЕТ НАМЕРЕННО. Раскладка объявлена в <see cref="MetricChannels.Plots"/>
/// и постоянна: каждый график отвечает на один вопрос, а составление графика из произвольных
/// рядов требует от игрока знать, какие величины сопоставимы между собой. Ось времени общая
/// у всех графиков — время партии без пауз.
///
/// СТЕК ПРИМЕНЁН ТОЛЬКО ТАМ, ГДЕ СУММА ИМЕЕТ СМЫСЛ. Слагаемые террора складываются в сам
/// показатель, и накопление показывает вклад каждого. Проценты производительности, запас
/// и потоки в стек не сводятся: сумма таких величин не соответствует ничему в игре.
/// </summary>
public partial class MetricsScreen : CanvasLayer
{
    // Затемнение непрозрачно: сквозь полупрозрачный слой просвечивал HUD, и его подписи
    // накладывались на графики
    private static readonly Color Shade = new(0.04f, 0.05f, 0.07f);
    private static readonly Color GridColor = new(1f, 1f, 1f, 0.08f);
    private static readonly Color CaptionColor = new(0.72f, 0.75f, 0.8f);
    private static readonly Color OverlayColor = new(1f, 0.9f, 0.75f);

    /// <summary>
    /// Цвета рядов по порядку внутри графика. Их шесть, а больше шести каналов в графике
    /// нет: график с большим числом линий нечитаем независимо от подбора цветов.
    /// </summary>
    private static readonly Color[] Palette =
    {
        new(1f, 0.62f, 0.45f),
        new(0.45f, 0.78f, 1f),
        new(0.55f, 0.9f, 0.6f),
        new(0.9f, 0.75f, 0.4f),
        new(0.8f, 0.6f, 0.95f),
        new(0.95f, 0.5f, 0.6f),
    };

    /// <summary>
    /// К скольким точкам сводится ряд перед отрисовкой. Часовая партия при секундном шаге
    /// даёт три с половиной тысячи замеров на график шириной в несколько сотен пикселей,
    /// и рисовать их все значило бы класть по десятку отрезков в один столбец экрана.
    /// </summary>
    private const int PlotResolution = 480;

    private readonly List<PlotCard> _cards = new();

    private Control _frame;
    private Label _summary;
    private Label _report;

    /// <summary>Показан ли экран сейчас.</summary>
    public bool IsOpen => _frame is { Visible: true };

    public override void _Ready()
    {
        // Выше меню исхода: экран открывается и оттуда тоже
        Layer = 30;
        Build();
    }

    /// <summary>Открыть экран и построить графики по накопленным рядам.</summary>
    public void Open()
    {
        if (_frame == null)
            return;

        Refresh();
        _frame.Visible = true;
    }

    public void Close()
    {
        if (_frame != null)
            _frame.Visible = false;
    }

    /// <summary>
    /// Escape закрывает экран и не идёт дальше. Порядок разбора обеспечивается местом узла
    /// в сцене: этот экран стоит последним в ветке наблюдателей, а необработанный ввод
    /// движок разносит в обратном порядке обхода дерева, поэтому меню паузы получит
    /// нажатие только тогда, когда экран закрыт.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || !@event.IsActionPressed(InputActions.GameCancel))
            return;

        GetViewport().SetInputAsHandled();
        Close();
    }

    private void Refresh()
    {
        var metrics = GameManager.I?.Metrics;

        if (metrics == null)
        {
            _summary.Text = "Ряды недоступны: сессия не собрана";
            return;
        }

        float step = metrics.Step;
        int count = metrics.Count;
        float seconds = count * step;

        _summary.Text = $"Длительность партии без пауз: {Elapsed(seconds)}   •   " +
                        $"замеров: {count}   •   шаг: {step:0.##} с";

        string path = GameManager.I.System<MetricsSystem>()?.ReportPath;
        _report.Text = string.IsNullOrEmpty(path) ? "Отчёт в файл не пишется" : $"Отчёт: {path}";

        foreach (var card in _cards)
            card.Refresh(metrics, seconds);
    }

    private void Build()
    {
        _frame = new UiFrame { Visible = false };
        AddChild(_frame);

        var shade = new ColorRect { Color = Shade };
        _frame.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var margin = new MarginContainer();
        _frame.AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 24);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        column.AddChild(BuildHeader());

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(grid);

        foreach (var plot in MetricChannels.Plots)
        {
            var card = new PlotCard(plot);
            grid.AddChild(card.Root);
            _cards.Add(card);
        }
    }

    private Control BuildHeader()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);

        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 2);
        row.AddChild(titles);

        var title = new Label { Text = "Отчёт партии" };
        title.AddThemeFontSizeOverride("font_size", 26);
        titles.AddChild(title);

        _summary = new Label { Text = "" };
        _summary.AddThemeFontSizeOverride("font_size", 13);
        _summary.AddThemeColorOverride("font_color", CaptionColor);
        titles.AddChild(_summary);

        _report = new Label { Text = "" };
        _report.AddThemeFontSizeOverride("font_size", 11);
        _report.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.62f));
        titles.AddChild(_report);

        var close = new Button
        {
            Text = "Закрыть",
            CustomMinimumSize = new Vector2(140, 38),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        close.Pressed += Close;
        row.AddChild(close);

        return row;
    }

    private static string Elapsed(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// Один график с заголовком, полем рисования и легендой. Легенда состоит из обычных
    /// подписей, а не из рисованного текста: шрифт и его размер тогда берутся из темы,
    /// как у всего прочего интерфейса.
    /// </summary>
    private sealed class PlotCard
    {
        private readonly MetricPlot _plot;
        private readonly PlotView _view;
        private readonly Label _peak;

        public readonly Control Root;

        public PlotCard(MetricPlot plot)
        {
            _plot = plot;

            var panel = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(360, 220),
            };

            var margin = new MarginContainer();
            panel.AddChild(margin);

            foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
                margin.AddThemeConstantOverride(side, 8);

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            margin.AddChild(column);

            var head = new HBoxContainer();
            column.AddChild(head);

            var title = new Label { Text = plot.Title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            title.AddThemeFontSizeOverride("font_size", 14);
            head.AddChild(title);

            _peak = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Right };
            _peak.AddThemeFontSizeOverride("font_size", 11);
            _peak.AddThemeColorOverride("font_color", CaptionColor);
            head.AddChild(_peak);

            _view = new PlotView
            {
                CustomMinimumSize = new Vector2(0, 150),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            column.AddChild(_view);

            column.AddChild(BuildLegend(plot));

            Root = panel;
        }

        public void Refresh(TimeSeriesProjection metrics, float seconds)
        {
            var series = new List<float[]>(_plot.Channels.Length);

            foreach (string id in _plot.Channels)
                series.Add(Resample(metrics.All(id)));

            float[] overlay = string.IsNullOrEmpty(_plot.Overlay)
                ? null
                : Resample(metrics.All(_plot.Overlay));

            float peak = _view.Show(series, overlay, _plot.Mode == MetricPlotMode.Stack);

            var format = MetricChannels.Find(_plot.Channels[0])?.Format ?? MetricFormat.Number;
            _peak.Text = $"макс {Format(peak, format)}   ·   {Elapsed(seconds)}";
        }

        private static Control BuildLegend(MetricPlot plot)
        {
            var legend = new HBoxContainer();
            legend.AddThemeConstantOverride("separation", 10);

            for (int i = 0; i < plot.Channels.Length; i++)
            {
                var channel = MetricChannels.Find(plot.Channels[i]);

                var item = new Label { Text = channel?.Caption ?? plot.Channels[i] };
                item.AddThemeFontSizeOverride("font_size", 10);
                item.AddThemeColorOverride("font_color", Palette[i % Palette.Length]);
                legend.AddChild(item);
            }

            if (string.IsNullOrEmpty(plot.Overlay))
                return legend;

            var extra = new Label { Text = MetricChannels.Find(plot.Overlay)?.Caption ?? plot.Overlay };
            extra.AddThemeFontSizeOverride("font_size", 10);
            extra.AddThemeColorOverride("font_color", OverlayColor);
            legend.AddChild(extra);

            return legend;
        }

        private static string Format(float value, MetricFormat format) => format switch
        {
            MetricFormat.Percent => $"{value * 100f:0}%",
            MetricFormat.Count => $"{value:0}",
            MetricFormat.Rate => $"{value:0.#}/с",
            _ => $"{value:0.#}",
        };

        /// <summary>
        /// Свести ряд к числу точек, соразмерному ширине графика. Значения усредняются,
        /// а не прореживаются: прореживание пропустило бы всплеск, попавший между взятыми
        /// точками, и график показал бы партию спокойнее, чем она была.
        /// </summary>
        private static float[] Resample(ReadOnlySpan<float> source)
        {
            if (source.Length == 0)
                return Array.Empty<float>();

            if (source.Length <= PlotResolution)
                return source.ToArray();

            var result = new float[PlotResolution];

            for (int i = 0; i < PlotResolution; i++)
            {
                int from = (int)((long)i * source.Length / PlotResolution);
                int to = (int)((long)(i + 1) * source.Length / PlotResolution);

                if (to <= from)
                    to = from + 1;

                float sum = 0f;

                for (int j = from; j < to; j++)
                    sum += source[j];

                result[i] = sum / (to - from);
            }

            return result;
        }
    }

    /// <summary>
    /// Поле рисования одного графика. Общий масштаб по вертикали един для всех рядов графика:
    /// каналы в нём подобраны сопоставимыми, и отдельная шкала на каждый ряд превратила бы
    /// сравнение линий в обман.
    /// </summary>
    private sealed partial class PlotView : Control
    {
        private readonly List<float[]> _series = new();
        private float[] _overlay;
        private bool _stack;
        private float _peak;

        /// <summary>Показать ряды и вернуть их вершину — она подписывается над графиком.</summary>
        public float Show(List<float[]> series, float[] overlay, bool stack)
        {
            _series.Clear();
            _series.AddRange(series);
            _overlay = overlay;
            _stack = stack;
            _peak = Peak();

            QueueRedraw();
            return _peak;
        }

        public override void _Draw()
        {
            var size = Size;

            if (size.X <= 2f || size.Y <= 2f)
                return;

            // Сетка рисуется всегда, в том числе по пустым рядам: пустое поле без разметки
            // читалось бы как поломка экрана, а не как отсутствие замеров
            for (int i = 1; i < 4; i++)
            {
                float y = size.Y * i / 4f;
                DrawLine(new Vector2(0f, y), new Vector2(size.X, y), GridColor);
            }

            if (_peak <= 0f)
                return;

            if (_stack)
                DrawStack(size);
            else
                DrawLines(size);

            if (_overlay is { Length: > 1 })
                DrawSeries(_overlay, size, OverlayColor, 2f);
        }

        private void DrawLines(Vector2 size)
        {
            for (int i = 0; i < _series.Count; i++)
                DrawSeries(_series[i], size, Palette[i % Palette.Length], 1.5f);
        }

        /// <summary>
        /// Накопление снизу вверх: каждый следующий ряд ложится поверх суммы предыдущих,
        /// и высота всей стопки равна сумме слагаемых. Полоса рисуется одним многоугольником —
        /// верхний край слева направо, нижний обратно.
        /// </summary>
        private void DrawStack(Vector2 size)
        {
            int length = Length();

            if (length < 2)
                return;

            var lower = new float[length];
            var polygon = new Vector2[length * 2];

            for (int s = 0; s < _series.Count; s++)
            {
                var series = _series[s];

                for (int i = 0; i < length; i++)
                {
                    float value = i < series.Length ? series[i] : 0f;
                    float upper = lower[i] + Mathf.Max(0f, value);

                    float x = size.X * i / (length - 1);
                    polygon[i] = new Vector2(x, Y(upper, size.Y));
                    polygon[polygon.Length - 1 - i] = new Vector2(x, Y(lower[i], size.Y));

                    lower[i] = upper;
                }

                var color = Palette[s % Palette.Length];
                DrawColoredPolygon(polygon, new Color(color, 0.55f));
            }
        }

        private void DrawSeries(float[] series, Vector2 size, Color color, float width)
        {
            if (series.Length < 2)
                return;

            var points = new Vector2[series.Length];

            for (int i = 0; i < series.Length; i++)
                points[i] = new Vector2(size.X * i / (series.Length - 1), Y(series[i], size.Y));

            DrawPolyline(points, color, width, true);
        }

        /// <summary>
        /// Значение в высоту поля. Сверху и снизу оставлен отступ в толщину линии: без него
        /// ряд, лежащий на вершине или на нуле, рисовался бы ровно по краю поля и срезался
        /// наполовину, а постоянная величина именно так и выглядит.
        /// </summary>
        private float Y(float value, float height)
        {
            const float pad = 2f;
            float span = Mathf.Max(1f, height - pad * 2f);

            return pad + span * (1f - Mathf.Clamp(value / _peak, 0f, 1f));
        }

        private int Length()
        {
            int length = 0;

            foreach (var series in _series)
                length = Mathf.Max(length, series.Length);

            return length;
        }

        /// <summary>
        /// Вершина графика. У стека это наибольшая сумма слагаемых, у линий — наибольшее
        /// значение среди всех рядов. Кривая поверх стека в вершину входит тоже: иначе она
        /// уходила бы за верхний край поля.
        /// </summary>
        private float Peak()
        {
            float peak = 0f;

            if (_stack)
            {
                int length = Length();

                for (int i = 0; i < length; i++)
                {
                    float sum = 0f;

                    foreach (var series in _series)
                        if (i < series.Length)
                            sum += Mathf.Max(0f, series[i]);

                    peak = Mathf.Max(peak, sum);
                }
            }
            else
            {
                foreach (var series in _series)
                    foreach (float value in series)
                        peak = Mathf.Max(peak, value);
            }

            if (_overlay != null)
                foreach (float value in _overlay)
                    peak = Mathf.Max(peak, value);

            return peak;
        }
    }
}
