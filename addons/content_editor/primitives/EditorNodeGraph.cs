using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Небольшое представление ориентированного графа для редакторских панелей.
/// Поддерживает сетку, масштабирование, панорамирование и активацию узла двойным щелчком.
/// </summary>
[Tool]
public partial class EditorNodeGraph : Control
{
    private const float NodeWidth = 190f;
    private const float NodeHeight = 58f;

    private ContentGraphData _graph;
    private readonly Dictionary<string, Rect2> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _levels = new(StringComparer.Ordinal);
    private Vector2 _pan = new(24, 24);
    private float _zoom = 1f;
    private bool _panning;
    private Vector2 _panStart;
    private Vector2 _mouseStart;

    public event Action<string> NodeActivated;

    public EditorNodeGraph()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public void SetGraph(ContentGraphData graph)
    {
        _graph = graph;
        BuildLayout();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.075f, 0.085f, 0.105f));
        DrawGrid();
        if (_graph == null)
            return;

        DrawSetTransform(_pan, 0f, Vector2.One * _zoom);
        for (int i = 0; i < _graph.Edges.Count; i++)
            DrawEdgeLine(_graph.Edges[i], i);
        // Подписи рисуются отдельным проходом после всех линий: иначе линия следующего
        // ребра могла пройти поверх текста предыдущего.
        for (int i = 0; i < _graph.Edges.Count; i++)
            DrawEdgeLabel(_graph.Edges[i], i);
        foreach (var node in _graph.Nodes)
            DrawNode(node);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse)
        {
            if (mouse.ButtonIndex == MouseButton.Middle)
            {
                _panning = mouse.Pressed;
                _panStart = _pan;
                _mouseStart = mouse.Position;
                AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAt(mouse.Position, 1.1f);
                AcceptEvent();
            }
            else if (mouse.Pressed && mouse.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAt(mouse.Position, 1f / 1.1f);
                AcceptEvent();
            }
            else if (mouse.Pressed
                     && mouse.ButtonIndex == MouseButton.Left
                     && mouse.DoubleClick)
            {
                string key = Hit(mouse.Position);
                if (key != null)
                    NodeActivated?.Invoke(key);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _panning)
        {
            _pan = _panStart + motion.Position - _mouseStart;
            QueueRedraw();
            AcceptEvent();
        }
    }

    private void BuildLayout()
    {
        _rects.Clear();
        _levels.Clear();
        if (_graph == null)
            return;

        // Топологические уровни помещают общую зависимость правее всех её источников.
        // Например, vars, на который ссылаются и юнит, и его base, не оказывается
        // в одной колонке с base.
        var incoming = _graph.Nodes.ToDictionary(
            node => node.Key,
            _ => 0,
            StringComparer.Ordinal);
        foreach (var edge in _graph.Edges)
        {
            if (incoming.ContainsKey(edge.To))
                incoming[edge.To]++;
        }

        var queue = new Queue<string>(
            incoming.Where(pair => pair.Value == 0)
                .OrderBy(pair => pair.Key == _graph.RootKey ? 0 : 1)
                .Select(pair => pair.Key));
        foreach (string key in queue)
            _levels[key] = key == _graph.RootKey ? 0 : 1;

        while (queue.Count > 0)
        {
            string from = queue.Dequeue();
            foreach (var edge in _graph.Edges.Where(candidate => candidate.From == from))
            {
                _levels[edge.To] = Mathf.Max(
                    _levels.GetValueOrDefault(edge.To),
                    _levels[from] + 1);
                incoming[edge.To]--;
                if (incoming[edge.To] == 0)
                    queue.Enqueue(edge.To);
            }
        }

        foreach (var node in _graph.Nodes)
        {
            if (!_levels.ContainsKey(node.Key))
                _levels[node.Key] = node.Key == _graph.RootKey ? 0 : 1;
        }

        var rowByKey = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var group in _graph.Nodes
                     .GroupBy(node => _levels[node.Key])
                     .OrderBy(group => group.Key))
        {
            int row = 0;
            var ordered = group
                .OrderBy(node => ParentBarycenter(node.Key, rowByKey))
                .ThenBy(node => node.Kind)
                .ThenBy(node => node.Title);
            foreach (var node in ordered)
            {
                _rects[node.Key] = new Rect2(
                    group.Key * 250f,
                    row * 82f,
                    NodeWidth,
                    NodeHeight);
                rowByKey[node.Key] = row;
                row++;
            }
        }
    }

    private float ParentBarycenter(string key, IReadOnlyDictionary<string, float> rows)
    {
        var parentRows = _graph.Edges
            .Where(edge => edge.To == key && rows.ContainsKey(edge.From))
            .Select(edge => rows[edge.From])
            .ToList();
        return parentRows.Count == 0 ? float.MaxValue : parentRows.Average();
    }

    private void DrawGrid()
    {
        float step = 24f * _zoom;
        if (step < 8f)
            return;

        var color = new Color(1f, 1f, 1f, 0.045f);
        float startX = _pan.X % step;
        float startY = _pan.Y % step;
        for (float x = startX; x < Size.X; x += step)
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), color);
        for (float y = startY; y < Size.Y; y += step)
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), color);
    }

    private void DrawEdgeLine(ContentGraphEdge edge, int index)
    {
        Vector2[] route = EdgeRoute(edge, index);
        if (route.Length < 2)
            return;

        var color = new Color(0.68f, 0.73f, 0.82f, 0.8f);
        DrawPolyline(route, color, 2f, true);

        Vector2 end = route[^1];
        Vector2 direction = (end - route[^2]).Normalized();
        Vector2 side = direction.Orthogonal() * 5f;
        DrawColoredPolygon(new[]
        {
            end,
            end - direction * 11f + side,
            end - direction * 11f - side,
        }, color);
    }

    private void DrawEdgeLabel(ContentGraphEdge edge, int index)
    {
        Vector2[] route = EdgeRoute(edge, index);
        if (route.Length < 2)
            return;

        Vector2 middle = LabelPoint(route, out bool horizontal);
        Vector2 textSize = ThemeDB.FallbackFont.GetStringSize(
            edge.Label, HorizontalAlignment.Left, -1, 11);
        middle += horizontal
            ? new Vector2(0f, -textSize.Y - 8f)
            : new Vector2(textSize.X * 0.5f + 10f, 0f);
        var background = new Rect2(
            middle - textSize * 0.5f - new Vector2(4f, 2f),
            textSize + new Vector2(8f, 4f));
        DrawRect(background, new Color(0.07f, 0.08f, 0.1f, 0.92f));
        DrawString(
            ThemeDB.FallbackFont,
            middle + new Vector2(-textSize.X * 0.5f, textSize.Y * 0.3f),
            edge.Label,
            HorizontalAlignment.Left,
            -1,
            11,
            new Color(0.82f, 0.85f, 0.9f));
    }

    private Vector2[] EdgeRoute(ContentGraphEdge edge, int index)
    {
        if (!_rects.TryGetValue(edge.From, out var from)
            || !_rects.TryGetValue(edge.To, out var to))
        {
            return Array.Empty<Vector2>();
        }

        Vector2 start = new(from.End.X, from.GetCenter().Y);
        Vector2 end = new(to.Position.X, to.GetCenter().Y);
        int fromLevel = _levels.GetValueOrDefault(edge.From);
        int toLevel = _levels.GetValueOrDefault(edge.To);

        if (toLevel > fromLevel + 1)
        {
            // Длинное ребро выводится в свободный коридор над узлами, а не проходит
            // сквозь промежуточные колонки.
            float corridor = -28f - index * 13f;
            return new[]
            {
                start,
                start + new Vector2(18f, 0f),
                new Vector2(start.X + 18f, corridor),
                new Vector2(end.X - 18f, corridor),
                new Vector2(end.X - 18f, end.Y),
                end,
            };
        }

        // Соседние уровни соединяются в промежутке между колонками. Небольшое
        // смещение создаёт отдельные каналы для нескольких рёбер.
        float lane = (start.X + end.X) * 0.5f + ((index % 5) - 2) * 5f;
        return new[]
        {
            start,
            new Vector2(lane, start.Y),
            new Vector2(lane, end.Y),
            end,
        };
    }

    private static Vector2 LabelPoint(
        IReadOnlyList<Vector2> route, out bool horizontal)
    {
        float longest = -1f;
        Vector2 point = Vector2.Zero;
        horizontal = false;
        for (int i = 1; i < route.Count; i++)
        {
            Vector2 delta = route[i] - route[i - 1];
            float length = delta.LengthSquared();
            if (length <= longest)
                continue;

            longest = length;
            point = (route[i] + route[i - 1]) * 0.5f;
            horizontal = Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y);
        }

        return point;
    }

    private void DrawNode(ContentGraphNode node)
    {
        if (!_rects.TryGetValue(node.Key, out var rect))
            return;

        Color color = NodeColor(node.Kind);
        DrawRect(rect, color.Darkened(0.58f), true);
        DrawRect(rect, color, false, 2f);
        DrawString(
            ThemeDB.FallbackFont,
            rect.Position + new Vector2(10, 21),
            node.Title,
            HorizontalAlignment.Left,
            NodeWidth - 20,
            13,
            Colors.White);
        DrawString(
            ThemeDB.FallbackFont,
            rect.Position + new Vector2(10, 43),
            node.Detail,
            HorizontalAlignment.Left,
            NodeWidth - 20,
            11,
            new Color(1f, 1f, 1f, 0.68f));
    }

    private string Hit(Vector2 screen)
    {
        Vector2 local = (screen - _pan) / _zoom;
        foreach (var pair in _rects)
        {
            if (pair.Value.HasPoint(local))
                return pair.Key;
        }
        return null;
    }

    private void ZoomAt(Vector2 point, float factor)
    {
        float next = Mathf.Clamp(_zoom * factor, 0.45f, 1.8f);
        Vector2 world = (point - _pan) / _zoom;
        _zoom = next;
        _pan = point - world * _zoom;
        QueueRedraw();
    }

    private static Color NodeColor(ContentGraphNodeKind kind) => kind switch
    {
        ContentGraphNodeKind.Unit => new Color(0.3f, 0.68f, 1f),
        ContentGraphNodeKind.Building => new Color(1f, 0.64f, 0.24f),
        ContentGraphNodeKind.Base => new Color(0.63f, 0.66f, 0.72f),
        ContentGraphNodeKind.Vars => new Color(0.78f, 0.42f, 1f),
        ContentGraphNodeKind.Weapon => new Color(1f, 0.34f, 0.36f),
        ContentGraphNodeKind.WorkTool => new Color(0.35f, 0.86f, 0.55f),
        _ => Colors.White,
    };
}
