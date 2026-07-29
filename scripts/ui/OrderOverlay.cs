using Godot;

/// <summary>
/// Очереди приказов выделенных юнитов поверх мира: кольцо вокруг выделенного, цепочка
/// от него ко всем целям по порядку и рамка выделения, пока её тянут.
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
    private static readonly Color Ring = new(0.6f, 1f, 0.75f);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var command = GameManager.I?.Command;
        if (command == null)
            return;

        foreach (var actor in command.Selected)
        {
            if (!Alive.Is(actor as Node))
                continue;

            DrawRing(actor);
            DrawChain(actor);
        }

        if (command.Banding)
            DrawBand(command.Band);
    }

    private void DrawRing(IOrderable actor)
    {
        float radius = ((actor as IDamageable)?.HitRadius ?? Const.Unit * 0.4f) + 6f;
        DrawArc(ToLocal(actor.GlobalPosition), radius, 0f, Mathf.Tau, 32, new Color(Ring, 0.9f), 2f);
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
            var tint = Order.Tint(order.Kind);

            // Текущий шаг ярче остальных: очередь читается сверху вниз даже на пёстрой карте
            float alpha = i == 0 ? 0.75f : 0.4f;

            DrawLine(from, to, new Color(tint, alpha), i == 0 ? 2.5f : 1.5f);
            DrawMark(to, order.Kind, new Color(tint, alpha + 0.15f));

            // Номер шага нужен только там, где шагов больше одного
            if (orders.Count > 1)
                DrawString(font, to + new Vector2(9f, -9f), $"{i + 1}",
                    HorizontalAlignment.Left, -1, 11, new Color(tint, 0.9f));

            if (i == 0)
                DrawString(font, to + new Vector2(9f, 18f), Order.Name(order.Kind),
                    HorizontalAlignment.Left, -1, 11, new Color(tint, 0.8f));

            from = to;
        }
    }

    /// <summary>Значок вида приказа: форма важнее цвета, цвет на карте легко потерять.</summary>
    private void DrawMark(Vector2 at, OrderKind kind, Color tint)
    {
        const float size = 7f;

        switch (kind)
        {
            case OrderKind.Move:
                DrawArc(at, size * 0.6f, 0f, Mathf.Tau, 16, tint, 2f);
                break;

            case OrderKind.Attack:
                DrawLine(at + new Vector2(-size, -size), at + new Vector2(size, size), tint, 2f);
                DrawLine(at + new Vector2(-size, size), at + new Vector2(size, -size), tint, 2f);
                break;

            case OrderKind.Repair:
                DrawLine(at + new Vector2(-size, 0f), at + new Vector2(size, 0f), tint, 2f);
                DrawLine(at + new Vector2(0f, -size), at + new Vector2(0f, size), tint, 2f);
                break;

            case OrderKind.Build:
                DrawRect(new Rect2(at - Vector2.One * size, Vector2.One * size * 2f), tint,
                    false, 2f);
                break;

            case OrderKind.Mine:
                DrawPolyline(new[]
                {
                    at + new Vector2(0f, -size), at + new Vector2(size, 0f),
                    at + new Vector2(0f, size), at + new Vector2(-size, 0f),
                    at + new Vector2(0f, -size),
                }, tint, 2f);
                break;

            default:
                DrawArc(at, size, 0f, Mathf.Tau, 20, tint, 1.5f);
                break;
        }
    }

    private void DrawBand(Rect2 band)
    {
        var local = new Rect2(ToLocal(band.Position), band.Size);

        DrawRect(local, new Color(Ring, 0.08f));
        DrawRect(local, new Color(Ring, 0.7f), false, 1.5f);
    }
}
