using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

/// <summary>Строка карты волн: одна волна на шкале террора.</summary>
public sealed class WaveGanttRow
{
    public string Id { get; init; }
    public string Title { get; init; }

    /// <summary>Вторая строка подписи: теги волны либо имя файла, если тегов нет.</summary>
    public string Subtitle { get; init; }

    public float From { get; init; }
    public float To { get; init; }

    /// <summary>Нижняя граница отсутствует: волна применима с самого начала партии.</summary>
    public bool OpenStart { get; init; }

    /// <summary>Верхняя граница отсутствует: волна применима до конца шкалы.</summary>
    public bool OpenEnded { get; init; }

    public string BudgetText { get; init; }

    /// <summary>Подходит ли волна к показателю террора, выставленному ползунком.</summary>
    public bool Applicable { get; init; }

    /// <summary>Черновик не проходит проверки содержимого и записан быть не может.</summary>
    public bool Faulted { get; init; }

    public bool Dirty { get; init; }
    public Color Accent { get; init; }
}

/// <summary>
/// Полосы применимости волн на общей шкале террора.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ Control. Раскладка контейнерами не позволяет совместить подписи строк
/// с общей осью сверху: каждая строка получала бы собственный масштаб. Здесь ось и полосы
/// рисуются в одной системе координат.
/// </summary>
[Tool]
public partial class WaveGanttChart : Control
{
    /// <summary>Ширина колонки подписей слева от шкалы.</summary>
    private const float LabelWidth = 200f;

    private const float HeaderHeight = 26f;
    private const float RowHeight = 34f;

    /// <summary>Отступ полосы от краёв строки по вертикали.</summary>
    private const float BarInset = 6f;

    private readonly List<WaveGanttRow> _rows = new();
    private float _max = 1f;
    private bool _log;
    private float _probe;
    private string _selectedId;
    private int _hoverRow = -1;

    /// <summary>Щелчок по строке: выбрать волну.</summary>
    public event Action<string> RowSelected;

    /// <summary>Двойной щелчок: открыть файл волны в системном редакторе.</summary>
    public event Action<string> RowActivated;

    public void SetRows(
        List<WaveGanttRow> rows, float max, bool log, float probe, string selectedId)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _max = Mathf.Max(max, 0.001f);
        _log = log;
        _probe = probe;
        _selectedId = selectedId;
        CustomMinimumSize = new Vector2(560, HeaderHeight + _rows.Count * RowHeight + 12f);
        QueueRedraw();
    }

    public void SetSelected(string id)
    {
        _selectedId = id;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            int row = RowAt(motion.Position);
            if (row != _hoverRow)
            {
                _hoverRow = row;
                QueueRedraw();
            }

            return;
        }

        if (@event is not InputEventMouseButton mouse
            || mouse.ButtonIndex != MouseButton.Left
            || !mouse.Pressed)
        {
            return;
        }

        int index = RowAt(mouse.Position);
        if (index < 0)
            return;

        AcceptEvent();
        string id = _rows[index].Id;
        if (mouse.DoubleClick)
            EditorControls.Run("open wave file", () => RowActivated?.Invoke(id));
        else
            EditorControls.Run("select wave", () => RowSelected?.Invoke(id));
    }

    private int RowAt(Vector2 position)
    {
        if (position.Y < HeaderHeight)
            return -1;

        int index = (int)((position.Y - HeaderHeight) / RowHeight);
        return index >= 0 && index < _rows.Count ? index : -1;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        if (_rows.Count == 0)
        {
            DrawString(font, new Vector2(16f, HeaderHeight + 16f),
                "No wave matches the search", HorizontalAlignment.Left, -1, 13,
                new Color(1f, 1f, 1f, 0.6f));
            return;
        }

        float right = Size.X - 12f;
        float width = Mathf.Max(right - LabelWidth, 40f);

        DrawAxis(font, width, right);

        for (int i = 0; i < _rows.Count; i++)
            DrawRow(font, _rows[i], i, width);

        float probeX = PositionOf(_probe, width);
        DrawLine(new Vector2(probeX, HeaderHeight - 6f),
            new Vector2(probeX, HeaderHeight + _rows.Count * RowHeight),
            new Color(1f, 0.75f, 0.35f, 0.9f), 2f);
    }

    private void DrawRow(Font font, WaveGanttRow row, int index, float width)
    {
        float top = HeaderHeight + index * RowHeight;
        bool selected = row.Id == _selectedId;

        if (selected)
            DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(0.3f, 0.45f, 0.7f, 0.28f));
        else if (index == _hoverRow)
            DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(1f, 1f, 1f, 0.05f));
        else if (index % 2 == 1)
            DrawRect(new Rect2(0f, top, Size.X, RowHeight), new Color(1f, 1f, 1f, 0.03f));

        // Неприменимая волна приглушается: так виден участок шкалы, на котором не подходит
        // ни одна волна.
        float alpha = row.Applicable ? 1f : 0.32f;
        string title = row.Dirty ? row.Title + " *" : row.Title;
        DrawString(font, new Vector2(10f, top + 15f), title,
            HorizontalAlignment.Left, LabelWidth - 18f, 13, new Color(0.95f, 0.97f, 1f, alpha));
        if (!string.IsNullOrEmpty(row.Subtitle))
            DrawString(font, new Vector2(10f, top + 28f), row.Subtitle,
                HorizontalAlignment.Left, LabelWidth - 18f, 11,
                new Color(0.75f, 0.82f, 0.95f, alpha * 0.8f));

        float x0 = PositionOf(row.From, width);
        float x1 = PositionOf(row.To, width);
        var bar = new Rect2(x0, top + BarInset, Mathf.Max(x1 - x0, 3f), RowHeight - BarInset * 2f);
        var fill = row.Accent;
        fill.A = alpha * 0.85f;
        DrawRect(bar, fill, true);
        DrawRect(bar,
            row.Faulted ? new Color(1f, 0.45f, 0.35f, alpha) : new Color(1f, 1f, 1f, alpha * 0.35f),
            false, row.Faulted ? 2f : 1f);

        // Открытый конец диапазона обозначен клином: полоса доходит до края шкалы,
        // и без этого знака её нельзя отличить от границы, совпавшей с максимумом.
        if (row.OpenEnded)
            DrawOpenEnd(bar, fill);
        if (row.OpenStart)
            DrawString(font, new Vector2(LabelWidth + 4f, top + 12f), "◂",
                HorizontalAlignment.Left, -1, 12, new Color(1f, 1f, 1f, alpha * 0.6f));

        // Бюджет пишется внутри полосы, а для узкой полосы — сразу за ней.
        if (bar.Size.X > 70f)
            DrawString(font, new Vector2(bar.Position.X + 8f, top + RowHeight * 0.5f + 5f),
                row.BudgetText, HorizontalAlignment.Left, bar.Size.X - 16f, 12,
                new Color(0.08f, 0.09f, 0.12f, alpha));
        else
            DrawString(font,
                new Vector2(bar.Position.X + bar.Size.X + 6f, top + RowHeight * 0.5f + 5f),
                row.BudgetText, HorizontalAlignment.Left, -1, 12,
                new Color(0.9f, 0.95f, 1f, alpha));
    }

    private void DrawOpenEnd(Rect2 bar, Color fill)
    {
        float tip = bar.Position.X + bar.Size.X;
        var points = new[]
        {
            new Vector2(tip, bar.Position.Y),
            new Vector2(tip + 8f, bar.Position.Y + bar.Size.Y * 0.5f),
            new Vector2(tip, bar.Position.Y + bar.Size.Y),
        };
        DrawColoredPolygon(points, fill);
    }

    private void DrawAxis(Font font, float width, float right)
    {
        DrawLine(new Vector2(LabelWidth, HeaderHeight - 4f),
            new Vector2(right, HeaderHeight - 4f), new Color(1f, 1f, 1f, 0.25f));

        foreach (float value in Ticks())
        {
            float x = PositionOf(value, width);
            DrawLine(new Vector2(x, HeaderHeight - 9f), new Vector2(x, HeaderHeight - 4f),
                new Color(1f, 1f, 1f, 0.3f));
            DrawString(font, new Vector2(x + 2f, HeaderHeight - 12f),
                value.ToString("0.##", CultureInfo.InvariantCulture),
                HorizontalAlignment.Left, -1, 11, new Color(0.8f, 0.86f, 1f, 0.75f));
        }

        DrawString(font, new Vector2(10f, HeaderHeight - 12f), "wave / terror",
            HorizontalAlignment.Left, LabelWidth - 18f, 11, new Color(0.8f, 0.86f, 1f, 0.75f));
    }

    /// <summary>Отметки шкалы: степени десяти в логарифмическом режиме, иначе равные доли.</summary>
    private IEnumerable<float> Ticks()
    {
        if (_log)
        {
            for (float value = 1f; value <= _max; value *= 10f)
                yield return value;
            yield break;
        }

        const int count = 6;
        for (int i = 0; i <= count; i++)
            yield return _max * i / count;
    }

    /// <summary>Положение показателя террора на шкале, в пикселях от левого края.</summary>
    private float PositionOf(float value, float width)
    {
        float clamped = Mathf.Clamp(value, 0f, _max);
        float t = _log
            ? Mathf.Log(1f + clamped) / Mathf.Log(1f + _max)
            : clamped / _max;
        return LabelWidth + t * width;
    }
}
