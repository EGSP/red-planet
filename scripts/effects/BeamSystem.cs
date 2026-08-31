using System.Collections.Generic;
using Godot;

/// <summary>
/// Лучи работы всего мира — множественными сетками, по три сетки на вид луча.
///
/// ЧЕМ ЭТО БЫЛО РАНЬШЕ. Луч рисовал себя сам: узел <see cref="BeamVisual"/> набирал полосу
/// отрезками и вспышки окружностями в собственном <c>_Draw</c>, а носитель без луча в модели
/// выводил запасной отрезок в своём слое пометок. Стоимость росла вместе с числом
/// работающих: у каждого свой список команд отрисовки и своя перерисовка каждый кадр.
///
/// ЧТО ЗДЕСЬ ПРОИСХОДИТ. Система ведёт разгорание каждого луча, разводит лучи по видам
/// и отдаёт их <see cref="BeamPainter"/>: у вида свой отрисовщик, у отрисовщика три сетки —
/// тело, вспышка у среза, вспышка в точке попадания. Число вызовов отрисовки перестаёт
/// зависеть от числа работающих и зависит только от числа видов луча в партии.
///
/// ОТКУДА БЕРУТСЯ ЛУЧИ. Из двух источников. Первый — узлы <see cref="BeamVisual"/>,
/// объявленные в сценах моделей; они вносят себя в перечень при входе в дерево. Второй —
/// носители без луча в модели: они объявляют признак <see cref="IWorkBeam"/> и называют
/// точку работы, а вид берётся из свода графики. Оба источника опрашиваются, а не
/// опрашивают сами: то же правило, что у полос прочности.
///
/// ПОЧЕМУ СИСТЕМОЙ. Разгорание обязано идти после того, как наведение инструментов
/// и перемещения этого кадра уже применены, иначе луч отстаёт от руки на кадр. Порядок
/// задаёт планировщик, а обход дерева нод его не даёт.
/// </summary>
public partial class BeamSystem : GameSystem
{
    private readonly Dictionary<BeamStyle, BeamPainter> _painters = new();

    private Node2D _root;

    public BeamSystem()
    {
        // Показ, а не изменение мира: лучи ведутся в графическом цикле после наведения
        Phase = Phase.React;
        UpdateCycle = UpdateCycle.Process;
    }

    public override void Step(double dt)
    {
        if (!EnsureRoot())
            return;

        CollectDeclared(dt);
        CollectFallback();

        // Записываются ВСЕ отрисовщики, а не только принявшие лучи: отрисовщик, оставшийся
        // в этом кадре пустым, обязан объявить своим сеткам ноль мест, иначе на экране
        // останутся лучи прошлого кадра
        foreach (var painter in _painters.Values)
            painter.Flush();
    }

    /// <summary>
    /// Лучи, объявленные в сценах моделей. Здесь же идёт их разгорание: узел ведёт своё
    /// состояние, но кадровый шаг ему называет система.
    /// </summary>
    private void CollectDeclared(double dt)
    {
        var live = BeamVisual.Live;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var beam = live[i];

            // Узел, освобождённый движком без выхода из дерева, снимается здесь: перечень
            // ведут сами узлы, и мёртвая ссылка иначе осталась бы в нём навсегда
            if (!Alive.Is(beam))
            {
                live.RemoveAt(i);
                continue;
            }

            beam.Advance(dt);

            if (beam.Power > 0f && beam.Style != null)
                PainterFor(beam.Style).Add(beam.From, beam.To, beam.Power, beam.Time);
        }
    }

    /// <summary>
    /// Запасные лучи носителей, у которых в модели луча нет. Разгорания у них не заведено:
    /// признак называет точку работы либо не называет вовсе, и луч появляется и исчезает
    /// вместе с ней.
    /// </summary>
    private void CollectFallback()
    {
        float time = (float)Godot.Time.GetTicksMsec() / 1000f;

        foreach (var host in GM.Index.All<IWorkBeam>())
        {
            if (host.WorkBeamPoint is not { } point || host.WorkBeamStyle is not { } style)
                continue;

            if (host is Node node && !Alive.Is(node))
                continue;

            PainterFor(style).Add(host.WorkBeamOrigin, point, 1f, time);
        }
    }

    /// <summary>Отрисовщик вида, заводимый при первом луче этого вида.</summary>
    private BeamPainter PainterFor(BeamStyle style)
    {
        if (_painters.TryGetValue(style, out var painter))
            return painter;

        painter = new BeamPainter(_root, style);
        _painters[style] = painter;
        return painter;
    }

    /// <summary>
    /// Завести общий узел лучей, если его ещё нет. Лучи лежат в слое надземных эффектов:
    /// они поднимаются над корпусом и обязаны быть видны поверх машин и построек, но под
    /// туманом войны и служебной графикой.
    /// </summary>
    private bool EnsureRoot()
    {
        if (Alive.Is(_root))
            return true;

        if (GM?.Playground == null)
            return false;

        _painters.Clear();
        _root = GM.Playground.Add(WorldLayer.AirEffects, new Node2D { Name = "Beams" });
        return true;
    }
}

/// <summary>
/// Носитель, показывающий работу запасным лучом. Признак объявляют те, у кого в модели
/// нет узла <see cref="BeamVisual"/>: до появления моделей луч рисовался отрезком, и терять
/// изображение работы вместе с отсутствием сцены нельзя.
///
/// Точка работы называется каждый кадр, а пустой ответ означает, что работы нет.
/// </summary>
public interface IWorkBeam
{
    /// <summary>Вид запасного луча. Пустой ответ означает, что луч не рисуется.</summary>
    BeamStyle WorkBeamStyle { get; }

    /// <summary>Начало луча в мировых координатах — срез инструмента.</summary>
    Vector2 WorkBeamOrigin { get; }

    /// <summary>Конец луча в мировых координатах либо пусто, если работы нет.</summary>
    Vector2? WorkBeamPoint { get; }
}
