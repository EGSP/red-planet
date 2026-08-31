using System.Collections.Generic;
using Godot;

/// <summary>
/// Очереди приказов поверх мира: цепочка от исполнителя ко всем целям по порядку и рамка
/// выделения, пока её тянут. Две стратегии: только выделенные или все свои — переключается
/// клавишей C, как CapsLock в PA.
///
/// Сама метка выделения здесь не рисуется: она лежит ПОД сущностью и потому принадлежит
/// другому слою — см. <see cref="SelectionOverlay"/>.
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

        if (command.State == CommandState.Banding)
            DrawBand(command.Band);

        // Указываемое прямо сейчас: круг области либо линия рисования с местами исполнителей.
        // Приказ ещё не отдан, поэтому рисуется он не очередью, а этим предпоказом
        if (command.State == CommandState.Sweeping)
            DrawArea(command.AreaCenter, command.AreaRadius,
                command.Aimed ?? OrderKind.Attack,
                preview: true, ready: command.AreaReady);

        // Линия показывается только там, где она что-то означает, — то есть при строе.
        // Единственного исполнителя рисование просто ведёт за указателем, мест вдоль линии
        // у него не возникает, и след жеста говорил бы ему о несуществующем распределении
        if (command.State == CommandState.Drawing && command.MoverCount > 1)
            DrawStroke(command.Path, command.Spots(command.MoverCount));
    }

    /// <summary>
    /// Вся очередь целиком, а не только текущий приказ: игрок должен видеть, что юнит
    /// сделает, — дойти, потом копать, — а не гадать по одной ближайшей стрелке.
    /// </summary>
    private void DrawChain(IOrderable actor)
    {
        // Список общий на весь отряд, а место в нём у каждого своё, да ещё и ветки
        // сцепляются: спрашиваем у очереди готовый остаток, иначе исполнителю приписался бы
        // шаг, который он уже прошёл, либо потерялось бы продолжение в чужой ветке
        int total = actor.Orders.Count;

        if (total == 0)
            return;

        var font = ThemeDB.FallbackFont;
        var from = ToLocal(actor.GlobalPosition);
        int step = -1;
        Order first = null;

        foreach (var order in actor.Orders.Remaining)
        {
            step++;
            first ??= order;

            var to = ToLocal(order.Point);
            var kind = Order.Viz(order.Kind);

            // Текущий шаг ярче остальных: очередь читается сверху вниз даже на пёстрой карте
            float alpha = step == 0 ? 0.75f : 0.4f;
            var line = DrawTheme.Line(kind, alpha, step == 0 ? 2.5f : 1.5f,
                step == 0 ? WidthMode.Screen : WidthMode.MinScreen);

            ShapeDraw.Line(this, from, to, line);
            DrawMark(to, order.Kind, new Color(DrawTheme.Hue(kind), alpha + 0.15f));

            // У приказа по области значка мало: важен не центр круга, а сам круг —
            // именно он говорит, где исполнитель будет искать себе цель или бегать
            if (order.Radius > 0f)
                DrawArea(order.Point, order.Radius, order.Kind, preview: false);

            // Номер шага нужен только там, где шагов больше одного
            if (total > 1)
                DrawString(font, to + new Vector2(9f, -9f), $"{step + 1}",
                    HorizontalAlignment.Left, -1, 11, new Color(DrawTheme.Hue(kind), 0.9f));

            // Ожидание отставших подписывается отдельно: юнит стоит на месте, и без
            // объяснения это выглядит как застрявший приказ
            if (step == 0)
            {
                string label = order.Waiting(actor.EntityId)
                    ? $"{Order.Name(order.Kind)} — ждём {order.Awaited}"
                    : Order.Name(order.Kind);

                DrawString(font, to + new Vector2(9f, 18f), label,
                    HorizontalAlignment.Left, -1, 11, new Color(DrawTheme.Hue(kind), 0.8f));
            }

            from = to;
        }

        // Замыкать круг нужно на первый ПРЕДСТОЯЩИЙ шаг маршрута. Обычно это тот, который
        // исполняется сейчас, но пока юнит доделывает приказ перед маршрутом, круг замыкается
        // на его начало: возврат идёт к первому патрулю, а не к тому, чем юнит занят
        DrawRingBack(actor, from,
            actor.Orders.Repeats(first) ? first : actor.Orders.RingHead);
    }

    /// <summary>
    /// Пройденная часть кольца патруля: от конца маршрута через его начало обратно к тому
    /// шагу, на котором исполнитель стоит сейчас.
    ///
    /// ПРОЙДЕННОЕ РИСУЕТСЯ, ПОТОМУ ЧТО ОНО ВЕРНЁТСЯ. Остаток очереди отвечает на вопрос,
    /// что юниту ещё делать, а игроку при патруле нужен ответ на другой — где он ходит.
    /// Без замкнутого круга маршрут на глазах укорачивался бы с каждым пройденным шагом,
    /// хотя не меняется вовсе.
    ///
    /// Приглушённее остатка и без номеров: это уже сделанное, и спорить за внимание
    /// с предстоящим оно не должно.
    /// </summary>
    private void DrawRingBack(IOrderable actor, Vector2 from, Order stop)
    {
        if (!actor.Orders.Ringed)
            return;

        foreach (var order in actor.Orders.Ring)
        {
            // Дошли до того, что уже нарисовано остатком: круг замкнулся
            if (ReferenceEquals(order, stop))
                break;

            var to = ToLocal(order.Point);
            var kind = Order.Viz(order.Kind);

            ShapeDraw.Line(this, from, to,
                DrawTheme.Line(kind, 0.22f, 1.5f, WidthMode.MinScreen));
            DrawMark(to, order.Kind, new Color(DrawTheme.Hue(kind), 0.35f));

            if (order.Radius > 0f)
                DrawArea(order.Point, order.Radius, order.Kind, preview: false);

            from = to;
        }

        // Последнее звено — к тому шагу, на котором исполнитель стоит сейчас: без него
        // круг остался бы разорванным ровно там, где юнит находится
        if (stop != null)
            ShapeDraw.Line(this, from, ToLocal(stop.Point),
                DrawTheme.Line(Order.Viz(stop.Kind), 0.22f, 1.5f, WidthMode.MinScreen));
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

            // Патруль — обход по кругу, и значок у него кольцо: маршрут читается по нему
            // даже там, где круга области нет
            case OrderKind.Patrol:
                ShapeDraw.Ring(this, at, size * 0.45f, size * 0.8f, stroke, 16);
                break;

            case OrderKind.Delete:
                ShapeDraw.Line(this, at + new Vector2(-size, -size), at + new Vector2(size, size), stroke);
                ShapeDraw.Line(this, at + new Vector2(-size, size), at + new Vector2(size, -size), stroke);
                ShapeDraw.Circle(this, at, size * 0.85f, stroke, 16);
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

    /// <summary>
    /// Круг области — указываемой или уже стоящей в очереди. Цвет берётся от вида приказа,
    /// а различаются два случая только насыщенностью: указываемое ярче, потому что игрок
    /// правит его прямо сейчас.
    /// </summary>
    private void DrawArea(Vector2 center, float radius, OrderKind kind, bool preview,
        bool ready = true)
    {
        if (radius <= 0f)
            return;

        var hue = DrawTheme.Hue(Order.Viz(kind));

        // Недостаточно растянутый круг показан бледно и без заливки: по отпусканию он станет
        // приказом по точке, и обещать областью то, чего не будет, нельзя
        float alpha = preview ? (ready ? 0.8f : 0.25f) : 0.45f;
        float fill = preview ? (ready ? 0.1f : 0f) : 0.05f;

        ShapeDraw.Circle(this, ToLocal(center), radius,
            ShapeStyle.Filled(new Color(hue, fill), new Color(hue, alpha),
                1.5f, WidthMode.Screen), 48);
    }

    /// <summary>
    /// Линия рисования и места, которые займут исполнители. Места показываются по ходу
    /// жеста, а не после него: игрок ведёт указателем и должен видеть будущий строй,
    /// пока ещё может его поправить.
    /// </summary>
    private void DrawStroke(IReadOnlyList<Vector2> path, IReadOnlyList<Vector2> spots)
    {
        if (path == null || path.Count == 0)
            return;

        var hue = DrawTheme.Hue(VizKind.OrderMove);

        if (path.Count >= 2)
        {
            var points = new Vector2[path.Count];

            for (int i = 0; i < path.Count; i++)
                points[i] = ToLocal(path[i]);

            ShapeDraw.Polyline(this, points,
                ShapeStyle.Outline(new Color(hue, 0.75f), 2f, WidthMode.Screen));
        }

        if (spots == null)
            return;

        foreach (var spot in spots)
            ShapeDraw.Circle(this, ToLocal(spot), 4f,
                ShapeStyle.Filled(new Color(hue, 0.3f), new Color(hue, 0.9f), 2f,
                    WidthMode.Screen), 12);
    }
}
