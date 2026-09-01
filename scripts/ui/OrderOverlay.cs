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
/// ЧЕМ ЭТО БЫЛО РАНЬШЕ. Наложение рисовало себя нодой в собственном <c>_Draw</c>: каждый
/// отрезок цепочки был отдельной командой отрисовки, окружность области строилась разбиением
/// на полсотни отрезков, а название приказа выводилось текстом, где своей команды стоил
/// каждый символ. Здесь наложение только объявляет фигуры, а рисует их общая множественная
/// сетка мира (<see cref="ShapeMesh"/>): весь показ приказов обходится в один вызов на форму.
///
/// ПОЧЕМУ СИСТЕМОЙ, А НЕ НОДОЙ. Фигуры обязаны объявляться до того, как сетки запишут буферы,
/// и порядок этот задаёт планировщик — см. <see cref="ShapeMeshSystem"/>. Своей ноды
/// у наложения при этом не осталось вовсе: рисовать ему больше нечем и незачем.
///
/// ПОЧЕМУ НЕ РИСУЮТ САМИ ЮНИТЫ. Приказ есть связь между двумя сущностями, и поручать её
/// одной из них значит завести у неё знание о том, чего она не касается. Кроме того, слой
/// показа общий на все очереди (<see cref="Layer"/>), а нода сущности лежит в своём.
///
/// Точка приказа берётся не из записанных координат, а из положения цели прямо сейчас:
/// враг за это время убежал, и путь обязан тянуться за ним, иначе стрелка врёт.
/// </summary>
public partial class OrderOverlay : GameSystem
{
    /// <summary>
    /// Слой мира, в котором лежит показ приказов.
    ///
    /// ПОД ЮНИТАМИ, А НЕ ПОВЕРХ. Очередь приказов есть подсказка о том, что сущность
    /// собирается делать, и закрывать собой саму сущность она не должна: линия, проходящая
    /// по корпусу, мешает разглядеть и корпус, и повреждения на нём. Наземные эффекты —
    /// ближайший слой, лежащий выше земли, но ниже всего, что по ней ходит и стоит.
    /// </summary>
    private const WorldLayer Layer = WorldLayer.GroundEffects;

    /// <summary>
    /// Прозрачность и толщина звена очереди, одни на все виды приказов. Круг области сюда
    /// не входит: он очерчивает место работы, а не связывает точки, и приглушать его
    /// до неразличимости незачем — см. <see cref="DrawArea"/>.
    /// </summary>
    private const float LineAlpha = 0.3f;

    /// <inheritdoc cref="LineAlpha"/>
    private const float LineWidth = 1f;

    /// <summary>
    /// Приказы, круг области которых уже показан в этом кадре.
    ///
    /// ЗАЧЕМ. Один приказ раздаётся всему отряду и хранится единственным объектом, а цепочку
    /// рисует каждый исполнитель свою. Отсюда следовало, что круг области рисовался столько
    /// раз, сколько машин его получили, и полупрозрачные обводки, сложившись, давали
    /// сплошное пятно. Круг принадлежит приказу, а не исполнителю, и показывается один раз.
    /// </summary>
    private readonly HashSet<Order> _areas = new();

    public OrderOverlay()
    {
        // Показ, а не изменение мира: очереди объявляются в графическом цикле после ввода
        // и перемещений этого кадра, но раньше записи в сетки
        Phase = Phase.React;
        UpdateCycle = UpdateCycle.Process;
    }

    public override void Step(double dt)
    {
        var command = GM?.Command;

        if (command == null)
            return;

        _areas.Clear();

        if (command.ShowAllOrders)
        {
            foreach (var actor in GM.Index.All<IOrderable>())
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
            ShapeMesh.Rect(command.Band, DrawTheme.Radius(VizKind.Band), Layer);

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
        if (actor.Orders.Count == 0)
            return;

        var from = actor.GlobalPosition;
        int step = -1;
        Order first = null;

        foreach (var order in actor.Orders.Remaining)
        {
            step++;
            first ??= order;

            var to = order.Point;
            var kind = Order.Viz(order.Kind);

            // ВСЕ ЗВЕНЬЯ ОЧЕРЕДИ ПОКАЗЫВАЮТСЯ ОДИНАКОВО. Прежде текущий шаг выделялся
            // толщиной и яркостью, но линию тянет за собой каждая машина отряда, и держится
            // она всю дорогу: при полусотне исполнителей выделенные звенья закрывали собой
            // поле. Различают шаги значок и порядок их следования, а линия лишь связывает
            // точки, и заметной ей быть не нужно
            ShapeMesh.Line(from, to,
                DrawTheme.Line(kind, LineAlpha, LineWidth, WidthMode.MinScreen), Layer);

            OrderMarks.Draw(to, order.Kind, LineAlpha + 0.15f, Layer);

            // У приказа по области значка мало: важен не центр круга, а сам круг —
            // именно он говорит, где исполнитель будет искать себе цель или бегать
            if (order.Radius > 0f && _areas.Add(order))
                DrawArea(order.Point, order.Radius, order.Kind, preview: false);

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
    /// Приглушённее остатка: это уже сделанное, и спорить за внимание с предстоящим
    /// оно не должно.
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

            var to = order.Point;

            ShapeMesh.Line(from, to,
                DrawTheme.Line(Order.Viz(order.Kind), 0.22f, LineWidth, WidthMode.MinScreen), Layer);

            OrderMarks.Draw(to, order.Kind, 0.35f, Layer);

            if (order.Radius > 0f && _areas.Add(order))
                DrawArea(order.Point, order.Radius, order.Kind, preview: false);

            from = to;
        }

        // Последнее звено — к тому шагу, на котором исполнитель стоит сейчас: без него
        // круг остался бы разорванным ровно там, где юнит находится
        if (stop != null)
            ShapeMesh.Line(from, stop.Point,
                DrawTheme.Line(Order.Viz(stop.Kind), 0.22f, LineWidth, WidthMode.MinScreen), Layer);
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

        // Заливка слабая намеренно: область атаки занимает заметную часть экрана, и плотное
        // пятно скрыло бы под собой и землю, и стоящее на ней. Сама область при этом должна
        // читаться как область, а не как одна окружность, — отсюда заливка вообще есть
        float fill = preview ? (ready ? 0.07f : 0f) : 0.05f;

        ShapeMesh.Circle(center, radius,
            ShapeStyle.Filled(new Color(hue, fill), new Color(hue, alpha),
                1.5f, WidthMode.Screen), Layer);
    }

    /// <summary>
    /// Линия рисования и места, которые займут исполнители. Места показываются по ходу
    /// жеста, а не после него: игрок ведёт указателем и должен видеть будущий строй,
    /// пока ещё может его поправить.
    /// </summary>
    private static void DrawStroke(IReadOnlyList<Vector2> path, IReadOnlyList<Vector2> spots)
    {
        if (path == null || path.Count == 0)
            return;

        var hue = DrawTheme.Hue(VizKind.OrderMove);

        if (path.Count >= 2)
            ShapeMesh.Polyline(path,
                ShapeStyle.Outline(new Color(hue, 0.75f), 2f, WidthMode.Screen), Layer);

        if (spots == null)
            return;

        foreach (var spot in spots)
            ShapeMesh.Circle(spot, 4f,
                ShapeStyle.Filled(new Color(hue, 0.3f), new Color(hue, 0.9f), 2f,
                    WidthMode.Screen), Layer);
    }
}
