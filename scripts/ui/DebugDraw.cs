using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Именованные каналы отладочной графики. Строковый канал задаётся отдельно;
/// значение перечисления совпадает с именем строки (<see cref="ToChannelName"/>).
/// </summary>
public enum DebugChannel
{
    Default,
    Nav,
    Path,
    Boids,
    Combat,
}

/// <summary>
/// Срок жизни отладочной команды: один кадр, интервал в секундах либо до явной очистки.
/// </summary>
public readonly struct DebugLife
{
    public enum Kind
    {
        Frame,
        Duration,
        Forever,
    }

    public readonly Kind Mode;
    public readonly float Seconds;

    private DebugLife(Kind mode, float seconds)
    {
        Mode = mode;
        Seconds = seconds;
    }

    /// <summary>Показать до первого фактического <c>_Draw</c>, удалить на следующем <c>_Process</c>.</summary>
    public static DebugLife Frame { get; } = new(Kind.Frame, 0f);

    /// <summary>Хранить до <see cref="DebugDraw.Clear()"/> или очистки канала.</summary>
    public static DebugLife Forever { get; } = new(Kind.Forever, 0f);

    /// <summary>Хранить заданное число секунд игрового времени (<see cref="Node._Process"/>).</summary>
    public static DebugLife For(float seconds) =>
        new(Kind.Duration, Mathf.Max(seconds, 0f));
}

/// <summary>
/// Сессионный слой произвольной отладочной графики поверх мира.
///
/// Не autoload и не переживает площадку: нода создаётся вместе с прочими эффектами
/// в <see cref="CommandSystem"/> и уничтожается вместе с деревом сессии.
/// Точка доступа — <see cref="Current"/>; при выходе из дерева ссылка обнуляется.
///
/// Команды копят геометрию и стиль, а в <c>_Draw</c> вызывают <see cref="ShapeDraw"/>.
/// Доменные оверлеи (навигация, пути, boids) сюда не входят.
/// </summary>
public partial class DebugDraw : Node2D
{
    /// <summary>
    /// Активный экземпляр текущей сессии либо <c>null</c>, если площадка ещё не собрана
    /// или уже разобрана. Вызывающий код обязан учитывать отсутствие ноды.
    /// </summary>
    public static DebugDraw Current { get; private set; }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, bool> _channelEnabled = new(StringComparer.Ordinal);
    private Vector2[] _localScratch;

    public override void _EnterTree()
    {
        Current = this;
    }

    public override void _ExitTree()
    {
        if (Current == this)
            Current = null;

        _entries.Clear();
        _channelEnabled.Clear();
        _localScratch = null;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool removed = false;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];

            switch (entry.Life)
            {
                case DebugLife.Kind.Frame:
                    // Удалять только после фактического _Draw; иначе команда пропадёт
                    // в кадре, где Process опередил отрисовку.
                    if (entry.Drawn)
                    {
                        _entries.RemoveAt(i);
                        removed = true;
                    }
                    break;

                case DebugLife.Kind.Duration:
                    entry.Remaining -= dt;
                    if (entry.Remaining <= 0f)
                    {
                        _entries.RemoveAt(i);
                        removed = true;
                    }
                    break;

                // Forever: геометрия не меняется — QueueRedraw не нужен.
            }
        }

        // Перерисовать только при удалении, иначе последняя фигура останется на холсте.
        if (removed)
            QueueRedraw();
    }

    public override void _Draw()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (IsChannelEnabled(entry.Channel))
                DrawEntry(entry);

            // Помечаем даже при выключенном канале: иначе Frame на нём никогда не снимется.
            entry.Drawn = true;
        }
    }

    // ── каналы ────────────────────────────────────────────────────────────────────

    public static string ToChannelName(DebugChannel channel) => channel.ToString();

    public bool IsChannelEnabled(string channel)
    {
        channel = NormalizeChannel(channel);
        return !_channelEnabled.TryGetValue(channel, out bool enabled) || enabled;
    }

    public bool IsChannelEnabled(DebugChannel channel) =>
        IsChannelEnabled(ToChannelName(channel));

    public void SetChannelEnabled(string channel, bool enabled)
    {
        _channelEnabled[NormalizeChannel(channel)] = enabled;
        QueueRedraw();
    }

    public void SetChannelEnabled(DebugChannel channel, bool enabled) =>
        SetChannelEnabled(ToChannelName(channel), enabled);

    /// <summary>Удалить все команды.</summary>
    public void Clear()
    {
        if (_entries.Count == 0)
            return;

        _entries.Clear();
        QueueRedraw();
    }

    /// <summary>Удалить команды указанного канала.</summary>
    public void Clear(string channel)
    {
        channel = NormalizeChannel(channel);
        int removed = _entries.RemoveAll(entry => entry.Channel == channel);

        if (removed > 0)
            QueueRedraw();
    }

    public void Clear(DebugChannel channel) => Clear(ToChannelName(channel));

    // ── команды ───────────────────────────────────────────────────────────────────

    public void Line(
        Vector2 from,
        Vector2 to,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default)
    {
        var entry = Create(ShapeKind.Line, channel, style, life);
        entry.A = from;
        entry.B = to;
        Push(entry);
    }

    public void Line(
        Vector2 from,
        Vector2 to,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default) =>
        Line(from, to, style, ToChannelName(channel), life);

    public void Polyline(
        Vector2[] points,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default,
        bool closed = false)
    {
        var entry = Create(ShapeKind.Polyline, channel, style, life);
        entry.Points = CopyPoints(points);
        entry.Closed = closed;
        Push(entry);
    }

    public void Polyline(
        Vector2[] points,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default,
        bool closed = false) =>
        Polyline(points, style, ToChannelName(channel), life, closed);

    public void Arrow(
        Vector2 from,
        Vector2 to,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default,
        float headLength = 0f)
    {
        var entry = Create(ShapeKind.Arrow, channel, style, life);
        entry.A = from;
        entry.B = to;
        entry.R0 = headLength;
        Push(entry);
    }

    public void Arrow(
        Vector2 from,
        Vector2 to,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default,
        float headLength = 0f) =>
        Arrow(from, to, style, ToChannelName(channel), life, headLength);

    public void Circle(
        Vector2 center,
        float radius,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default,
        int pointCount = 0)
    {
        var entry = Create(ShapeKind.Circle, channel, style, life);
        entry.A = center;
        entry.R0 = radius;
        entry.PointCount = pointCount;
        Push(entry);
    }

    public void Circle(
        Vector2 center,
        float radius,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default,
        int pointCount = 0) =>
        Circle(center, radius, style, ToChannelName(channel), life, pointCount);

    public void Ring(
        Vector2 center,
        float innerRadius,
        float outerRadius,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default,
        int pointCount = 0)
    {
        var entry = Create(ShapeKind.Ring, channel, style, life);
        entry.A = center;
        entry.R0 = innerRadius;
        entry.R1 = outerRadius;
        entry.PointCount = pointCount;
        Push(entry);
    }

    public void Ring(
        Vector2 center,
        float innerRadius,
        float outerRadius,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default,
        int pointCount = 0) =>
        Ring(center, innerRadius, outerRadius, style, ToChannelName(channel), life, pointCount);

    public void Rect(
        Rect2 rect,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default)
    {
        var entry = Create(ShapeKind.Rect, channel, style, life);
        entry.Rect = rect;
        Push(entry);
    }

    public void Rect(
        Rect2 rect,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default) =>
        Rect(rect, style, ToChannelName(channel), life);

    public void Obb(
        in Obb area,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default)
    {
        var entry = Create(ShapeKind.Obb, channel, style, life);
        entry.Area = area;
        Push(entry);
    }

    public void Obb(
        in Obb area,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default) =>
        Obb(area, style, ToChannelName(channel), life);

    public void Polygon(
        Vector2[] points,
        in ShapeStyle style,
        string channel = null,
        DebugLife life = default,
        bool closed = true)
    {
        var entry = Create(ShapeKind.Polygon, channel, style, life);
        entry.Points = CopyPoints(points);
        entry.Closed = closed;
        Push(entry);
    }

    public void Polygon(
        Vector2[] points,
        in ShapeStyle style,
        DebugChannel channel,
        DebugLife life = default,
        bool closed = true) =>
        Polygon(points, style, ToChannelName(channel), life, closed);

    // ── внутренности ──────────────────────────────────────────────────────────────

    private Entry Create(ShapeKind shape, string channel, in ShapeStyle style, DebugLife life)
    {
        // default(DebugLife) совпадает с Frame: Mode = 0, Seconds = 0.
        return new Entry
        {
            Shape = shape,
            Channel = NormalizeChannel(channel),
            Style = style,
            Life = life.Mode,
            Remaining = life.Seconds,
        };
    }

    private void Push(Entry entry)
    {
        _entries.Add(entry);
        QueueRedraw();
    }

    private void DrawEntry(Entry entry)
    {
        switch (entry.Shape)
        {
            case ShapeKind.Line:
                ShapeDraw.Line(this, ToLocal(entry.A), ToLocal(entry.B), entry.Style);
                break;

            case ShapeKind.Polyline:
                ShapeDraw.Polyline(this, ToLocalPoints(entry.Points), entry.Style, entry.Closed);
                break;

            case ShapeKind.Arrow:
                ShapeDraw.Arrow(this, ToLocal(entry.A), ToLocal(entry.B), entry.Style, entry.R0);
                break;

            case ShapeKind.Circle:
                ShapeDraw.Circle(this, ToLocal(entry.A), entry.R0, entry.Style, entry.PointCount);
                break;

            case ShapeKind.Ring:
                ShapeDraw.Ring(this, ToLocal(entry.A), entry.R0, entry.R1, entry.Style, entry.PointCount);
                break;

            case ShapeKind.Rect:
                ShapeDraw.Rect(this, new Rect2(ToLocal(entry.Rect.Position), entry.Rect.Size), entry.Style);
                break;

            case ShapeKind.Obb:
                // ShapeDraw.Obb сам переводит мировые углы в локальные координаты Node2D.
                ShapeDraw.Obb(this, entry.Area, entry.Style);
                break;

            case ShapeKind.Polygon:
                ShapeDraw.Polygon(this, ToLocalPoints(entry.Points), entry.Style, entry.Closed);
                break;
        }
    }

    private Vector2[] ToLocalPoints(Vector2[] world)
    {
        if (world == null || world.Length == 0)
            return world;

        // Общий буфер: ShapeDraw читает точки синхронно внутри одного вызова.
        if (_localScratch == null || _localScratch.Length != world.Length)
            _localScratch = new Vector2[world.Length];

        for (int i = 0; i < world.Length; i++)
            _localScratch[i] = ToLocal(world[i]);

        return _localScratch;
    }

    private static Vector2[] CopyPoints(Vector2[] points)
    {
        if (points == null || points.Length == 0)
            return points;

        var copy = new Vector2[points.Length];
        points.CopyTo(copy, 0);
        return copy;
    }

    private static string NormalizeChannel(string channel) =>
        string.IsNullOrEmpty(channel) ? nameof(DebugChannel.Default) : channel;

    private enum ShapeKind
    {
        Line,
        Polyline,
        Arrow,
        Circle,
        Ring,
        Rect,
        Obb,
        Polygon,
    }

    private sealed class Entry
    {
        public ShapeKind Shape;
        public string Channel;
        public ShapeStyle Style;
        public DebugLife.Kind Life;
        public float Remaining;
        public bool Drawn;
        public Vector2 A;
        public Vector2 B;
        public Vector2[] Points;
        public float R0;
        public float R1;
        public Rect2 Rect;
        public Obb Area;
        public bool Closed;
        public int PointCount;
    }
}
