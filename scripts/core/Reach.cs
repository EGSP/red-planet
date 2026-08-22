using Godot;

/// <summary>
/// Досягаемость: дотягивается ли инструмент до цели и с какой дистанции.
///
/// ПОЧЕМУ НЕ РАССТОЯНИЕ МЕЖДУ ЦЕНТРАМИ. Дальность инструмента задана от носителя, а цель
/// занимает место, и до её края ближе, чем до середины. У постройки два на два расстояние
/// до угла в 1.41 раза больше расстояния до середины стороны, поэтому строитель, вставший
/// по диагонали, отвергался проверкой, хотя нарисованный круг радиуса накрывал угол здания.
/// То же самое случалось с помощником, оказавшимся с дальней стороны каркаса.
///
/// ПОЧЕМУ НЕ ОПИСАННАЯ ОКРУЖНОСТЬ. Прибавить к дальности <c>HitRadius</c> проще, но у него
/// радиус описанной окружности, и у постройки два на четыре он завышает досягаемость поперёк
/// корпуса почти вдвое: работать разрешалось бы, стоя в стороне от каркаса. Поэтому там, где
/// у цели есть <see cref="IFootprint"/>, расстояние меряется до прямоугольника, а окружность
/// остаётся тем, у кого прямоугольника нет, — юнитам.
///
/// ЕДИНСТВЕННЫЙ РАСЧЁТ ДОСЯГАЕМОСТИ ВО ВСЁМ ПРОЕКТЕ. И работа инструментом, и огневая граница
/// (<see cref="Targeting.FiringDistance"/>) выводятся отсюда. Две формулы неизбежно разойдутся,
/// и исполнитель остановится там, где действовать ещё нельзя.
/// </summary>
public static class Reach
{
    /// <summary>
    /// Насколько поверхность цели выступает из её центра в сторону точки.
    ///
    /// Величина зависит от направления и потому считается заново каждый кадр: подошедший
    /// сбоку и подошедший с угла получают разную поправку. Точка внутри цели даёт поправку,
    /// равную всему расстоянию, — досягаемость в этом случае выполнена при любой дальности.
    /// </summary>
    public static float Inset(Vector2 from, object target)
    {
        if (!Center(target, out var center))
            return 0f;

        float toCenter = from.DistanceTo(center);

        if (target is IFootprint { Footprint.IsEmpty: false } shaped)
            return Mathf.Max(toCenter - shaped.Footprint.DistanceTo(from), 0f);

        if (target is IDamageable damageable)
            return Mathf.Min(damageable.HitRadius, toCenter);

        return 0f;
    }

    /// <summary>Расстояние от точки до ПОВЕРХНОСТИ цели. Внутри цели — ноль.</summary>
    public static float Distance(Vector2 from, object target) =>
        Center(target, out var center)
            ? Mathf.Max(from.DistanceTo(center) - Inset(from, target), 0f)
            : float.MaxValue;

    /// <summary>Дотягивается ли инструмент указанной дальности до цели.</summary>
    public static bool Within(Vector2 from, object target, float reach) =>
        Distance(from, target) <= reach;

    /// <summary>
    /// Дистанция ОТ ЦЕНТРА цели, на которой инструмент до неё дотягивается.
    ///
    /// Нужна движению: <see cref="Movement.Seek"/> ведёт к точке и останавливается за
    /// столько-то пикселей до неё, а точка — это центр цели. Поправка на габарит переносится
    /// сюда, и остановка происходит там же, где проверка досягаемости даёт согласие.
    /// </summary>
    public static float StopDistance(Vector2 from, object target, float reach) =>
        reach + Inset(from, target);

    /// <summary>Наименьший запас подхода к месту работы. Смысл тот же, что у огневого.</summary>
    private static readonly float WorkMargin = Const.Unit * 0.5f;

    /// <summary>
    /// Дистанция, на которой исполнитель прекращает сближение с местом работы.
    ///
    /// Считается вычитанием запаса из досягаемости, поэтому по построению лежит внутри неё:
    /// встать там, где рука уже не дотягивается, при таком определении невозможно ни при
    /// каких числах справочника. Запас берётся долей дальности
    /// (<see cref="WorkToolDefinition.ApproachHold"/>), но не меньше <see cref="WorkMargin"/>:
    /// у короткой руки доля от дальности составила бы несколько пикселей, и исполнитель
    /// вставал бы фактически на самой границе.
    ///
    /// Устроено так же, как <see cref="Targeting.ApproachDistance"/> у стрельбы, и по той же
    /// причине: две формулы подхода неизбежно разойдутся, и исполнитель остановится там,
    /// где действовать ещё нельзя.
    /// </summary>
    public static float WorkStopDistance(Vector2 from, object target, WorkToolDefinition tool)
    {
        float reach = tool?.RangePx ?? Const.Unit;
        float slack = Mathf.Max(reach * (1f - Mathf.Clamp(tool?.ApproachHold ?? 1f, 0f, 1f)),
            WorkMargin);

        return StopDistance(from, target, Mathf.Max(reach - slack, 0f));
    }

    /// <summary>
    /// Положение цели. Сущности приходят сюда под разными признаками — нода, уязвимое,
    /// место работы, — а общего предка с положением у них нет, поэтому разбор по типу.
    /// </summary>
    private static bool Center(object target, out Vector2 center)
    {
        switch (target)
        {
            case Node2D node:
                center = node.GlobalPosition;
                return true;

            case IDamageable damageable:
                center = damageable.GlobalPosition;
                return true;

            case IWorkSite site:
                center = site.GlobalPosition;
                return true;

            default:
                center = Vector2.Zero;
                return false;
        }
    }
}
