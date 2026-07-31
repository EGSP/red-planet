using Godot;

/// <summary>
/// Распознавание повторного выбора одной и той же цели — «двойного щелчка» в широком смысле.
///
/// О МЫШИ НЕ ЗНАЕТ НАМЕРЕННО. Внутрь передаётся только цель, а откуда взялся выбор —
/// левая кнопка, палец по экрану, кнопка геймпада или горячая клавиша — детектора
/// не касается. Поэтому второй источник двойного выбора добавляется без правки этого кода:
/// он просто зовёт Register в своём обработчике.
///
/// СЧИТАЕТСЯ ТОЛЬКО ПОВТОР ПО ТОЙ ЖЕ ЦЕЛИ. Два быстрых щелчка по разным юнитам —
/// это два одиночных выбора, а не двойной: иначе беглая расстановка приказов то и дело
/// оборачивалась бы выделением половины карты. По той же причине не засчитывается повтор
/// по цели, погибшей между щелчками.
///
/// ВРЕМЯ БЕРЁТСЯ СИСТЕМНОЕ, а не игровое: двойной щелчок — свойство руки игрока,
/// и от паузы или масштаба времени зависеть не должен.
/// </summary>
public sealed class DoubleTap
{
    /// <summary>Наибольший промежуток между щелчками, секунды.</summary>
    public float Interval = 0.35f;

    private object _last;
    private ulong _lastMs;

    /// <summary>
    /// Отметить выбор цели. Возвращает true, если это повтор по той же цели,
    /// уложившийся в промежуток.
    /// </summary>
    public bool Register(object target)
    {
        if (target == null)
        {
            Reset();
            return false;
        }

        ulong now = Time.GetTicksMsec();
        bool again = ReferenceEquals(_last, target)
                     && Alive(target)
                     && now - _lastMs <= (ulong)Mathf.RoundToInt(Interval * 1000f);

        _last = target;
        _lastMs = now;

        // После срабатывания счёт начинается заново: три щелчка подряд — это один
        // двойной и один одиночный, а не два двойных
        if (again)
            _last = null;

        return again;
    }

    public void Reset()
    {
        _last = null;
        _lastMs = 0;
    }

    private static bool Alive(object target) =>
        target is not GodotObject obj || global::Alive.Is(obj);
}
