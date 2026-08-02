using Godot;

/// <summary>
/// Очереди приказов поверх мира: кольцо вокруг выделенного, цепочка от него ко всем
/// целям по порядку и рамка выделения, пока её тянут. Две стратегии: только выделенные
/// или все свои — переключается клавишей C, как CapsLock в PA.
///
/// ЗАЧЕМ ОТДЕЛЬНОЙ НОДОЙ, а не рисованием у самих юнитов. Во-первых, цепочка должна лежать
/// поверх всего, а не тонуть под постройками — у своей ноды слой её собственный. Во-вторых,
/// приказ — это связь между двумя сущностями, и рисовать её у одной из них значит завести
/// у неё знание о том, чего она не касается.
///
/// Точка приказа берётся не из записанных координат, а из положения цели прямо сейчас:
/// враг за это время убежал, и путь обязан тянуться за ним, иначе стрелка врёт.
/// </summary>
public partial class OrderOverlay : Node2D
{
    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var command = GameManager.I?.Command;
        if (command == null)
            return;

        // Кольца — только у выделенных: это метка выбора, а не приказа
        foreach (var actor in command.Selected)
        {
            if (Alive.Is(actor as Node))
                DrawRing(actor);
        }

        if (command.ShowAllOrders)
        {
            foreach (var actor in GameManager.I.Index.All<IOrderable>())
            {
                if (!Alive.Is(actor as Node))
                    continue;

                if (actor.Faction != Faction.Player || !actor.AllowedOrders.Any)
                    continue;

                DrawChain(actor);
            }
        }
        else
        {
            foreach (var actor in command.Selected)
            {
                if (Alive.Is(actor as Node))
                    DrawChain(actor);
            }
        }

        if (command.Banding)
            DrawBand(command.Band);
    }

    private void DrawRing(IOrderable actor)
    {
        float radius = ((actor as IDamageable)?.HitRadius ?? Const.Unit * 0.4f) + 6f;
        float band = 3f;

        ShapeDraw.Ring(this, ToLocal(actor.GlobalPosition), radius - band * 0.5f, radius + band * 0.5f,
            DrawTheme.Radius(VizKind.Selection), 32);
    }

    /// <summary>
    /// Вся очередь целиком, а не только текущий приказ: игрок должен видеть, что юнит
    /// сделает, — дойти, потом копать, — а не гадать по одной ближайшей стрелке.
    /// </summary>
    private void DrawChain(IOrderable actor)
    {
        var orders = actor.Orders.Items;
        if (orders.Count == 0)
            return;

        var font = ThemeDB.FallbackFont;
        var from = ToLocal(actor.GlobalPosition);

        for (int i = 0; i < orders.Count; i++)
        {
            var order = orders[i];
            var to = ToLocal(order.Point);
            var kind = Order.Viz(order.Kind);

            // Текущий шаг ярче остальных: очередь читается сверху вниз даже на пёстрой карте
            float alpha = i == 0 ? 0.75f : 0.4f;
            var line = DrawTheme.Line(kind, alpha, i == 0 ? 2.5f : 1.5f,
                i == 0 ? WidthMode.Screen : WidthMode.MinScreen);

            ShapeDraw.Line(this, from, to, line);
            DrawMark(to, order.Kind, new Color(DrawTheme.Hue(kind), alpha + 0.15f));

            // Номер шага нужен только там, где шагов больше одного
            if (orders.Count > 1)
                DrawString(font, to + new Vector2(9f, -9f), $"{i + 1}",
                    HorizontalAlignment.Left, -1, 11, new Color(DrawTheme.Hue(kind), 0.9f));

            if (i == 0)
                DrawString(font, to + new Vector2(9f, 18f), Order.Name(order.Kind),
                    HorizontalAlignment.Left, -1, 11, new Color(DrawTheme.Hue(kind), 0.8f));

            from = to;
        }
    }

    /// <summary>Значок вида приказа: форма важнее цвета, цвет на карте легко потерять.</summary>
    private void DrawMark(Vector2 at, OrderKind kind, Color tint)
    {
        const float size = 7f;
        var stroke = ShapeStyle.Outline(tint, 2f, WidthMode.Screen);

        switch (kind)
        {
            case OrderKind.Move:
                ShapeDraw.Circle(this, at, size * 0.6f,
                    ShapeStyle.Filled(new Color(tint, 0.25f), tint, 2f, WidthMode.Screen), 16);
                break;

            case OrderKind.Attack:
                ShapeDraw.Line(this, at + new Vector2(-size, -size), at + new Vector2(size, size), stroke);
                ShapeDraw.Line(this, at + new Vector2(-size, size), at + new Vector2(size, -size), stroke);
                break;

            case OrderKind.Repair:
                ShapeDraw.Line(this, at + new Vector2(-size, 0f), at + new Vector2(size, 0f), stroke);
                ShapeDraw.Line(this, at + new Vector2(0f, -size), at + new Vector2(0f, size), stroke);
                break;

            case OrderKind.Build:
                ShapeDraw.Rect(this, new Rect2(at - Vector2.One * size, Vector2.One * size * 2f), stroke);
                break;

            default:
                ShapeDraw.Circle(this, at, size,
                    ShapeStyle.Filled(new Color(tint, 0.2f), tint, 1.5f, WidthMode.MinScreen), 20);
                break;
        }
    }

    private void DrawBand(Rect2 band)
    {
        var local = new Rect2(ToLocal(band.Position), band.Size);
        ShapeDraw.Rect(this, local, DrawTheme.Radius(VizKind.Band));
    }
}
