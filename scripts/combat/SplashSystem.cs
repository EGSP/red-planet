using Godot;

/// <summary>
/// Разворачивает требования взрыва (<see cref="SplashRequested"/>) в обычные попадания
/// (<see cref="DamageDealt"/>): находит всех в радиусе и каждому выписывает свою долю урона.
///
/// ОТДЕЛЬНЫЙ ЭТАП МЕЖДУ ПОПАДАНИЕМ И УРОНОМ. Снаряд знает только то, во что уткнулся,
/// а прочность правит <see cref="DamageSystem"/>; между ними и встаёт эта система. Порядок
/// в фазе реакции у неё меньше, поэтому выписанные ею попадания разбираются тем же кадром,
/// и взрыв не отстаёт от прямого попадания на кадр.
///
/// ПОЧЕМУ ОНА НЕ ПРАВИТ ПРОЧНОСТЬ САМА. Место, где прочность убывает, во всём проекте одно.
/// Раздача урона по области есть выбор жертв, а не нанесение урона; всё, что за выбором
/// следует, — гибель, взрыв, запись в показатели — принадлежит разбору <c>DamageDealt</c>
/// и повторяться здесь не должно.
///
/// СТОИМОСТЬ. Обход идёт по разрезу целей одной стороны, то есть по тому же составу,
/// в который смотрит выбор цели у стрельбы. Отдельного пространственного разбиения под
/// взрывы нет намеренно: взрывов за кадр единицы, тогда как выстрелов и полётов сотни.
/// </summary>
public partial class SplashSystem : GameSystem
{
    public override void Step(double dt)
    {
        foreach (var request in GM.Events.Stream<SplashRequested>().Records)
        {
            if (request.Radius <= 0f || request.Amount <= 0f)
                continue;

            Spread(request, request.Side.Opposite());

            // Своя сторона задевается только тем оружием, которому это позволено:
            // признак принадлежит стволу, а не игре целиком
            if (request.FriendlyFire)
                Spread(request, request.Side);
        }
    }

    /// <summary>
    /// Раздать урон целям одной стороны.
    ///
    /// РАССТОЯНИЕ МЕРЯЕТСЯ ДО ПОВЕРХНОСТИ ЦЕЛИ, а не до её середины: у постройки шесть
    /// на шесть середина отстоит от стены на три клетки, и по расстоянию между центрами
    /// взрыв у самой стены не задевал бы её вовсе. Считает поправку <see cref="Reach"/> —
    /// тот же расчёт, которым меряется огневая граница.
    /// </summary>
    private void Spread(in SplashRequested request, Faction side)
    {
        foreach (var target in GM.Targets[side])
        {
            if (target.Health.IsDead || Targeting.Leaving(target))
                continue;

            // Прямая цель уже получила полный урон снаряда — см. SplashRequested.DirectId
            if (target.EntityId == request.DirectId)
                continue;

            float distance = Reach.Distance(request.Pos, target);
            if (distance > request.Radius)
                continue;

            float amount = request.Amount * CombatSettings.Falloff(distance / request.Radius);
            if (amount <= 0f)
                continue;

            var at = target.GlobalPosition;

            GM.Events.Append(new DamageDealt
            {
                TargetId = target.EntityId,
                SourceId = request.SourceId,
                Amount = amount,
                Pos = at,

                // Направление от середины взрыва к цели: вспышка попадания разворачивается
                // против него и потому смотрит на место, где рвануло
                Facing = (at - request.Pos).Angle(),
                FromSplash = true,
            });
        }
    }
}
