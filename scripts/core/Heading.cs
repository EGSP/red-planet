using Godot;

/// <summary>
/// Сущность, у которой есть направление взгляда — своя ось «вперёд».
///
/// ЗАЧЕМ. Ось нужна не только тем, кто стреляет: направление — свойство любой сущности мира.
/// У подвижных оно меняется со своей скоростью вращения, у зданий задано справочником
/// и дальше не меняется. Единый контракт избавляет от вопроса «а у этого есть поворот?».
///
/// Угол мировой, в радианах, 0 — вправо (как у Node2D.Rotation и Vector2.Angle()).
/// У подвижных ось прицеливания — направление инструмента, если умение позволяет
/// наводиться на ходу; иначе это поворот корпуса (ноды). Здания ноду не крутят:
/// их визуал привязан к сетке, поэтому ось у них живёт отдельным числом.
/// </summary>
public interface IFacing
{
    float Facing { get; }
}

/// <summary>Работа с углами взгляда: ось вперёд, доворот с ограниченной скоростью, конус.</summary>
public static class Heading
{
    /// <summary>Единичный вектор оси «вперёд» для угла.</summary>
    public static Vector2 Forward(float facing) => Vector2.Right.Rotated(facing);

    public static Vector2 ForwardOf(IFacing entity) => Forward(entity.Facing);

    /// <summary>
    /// Кратчайшая разница углов в диапазоне [-pi, pi]: со знаком, в какую сторону крутиться.
    /// </summary>
    public static float Delta(float from, float to) => Mathf.Wrap(to - from, -Mathf.Pi, Mathf.Pi);

    /// <summary>
    /// Довернуть угол к желаемому не более чем на maxStep радиан.
    /// Отсюда и берётся разница между неповоротливым толстяком и вертлявым стрелком.
    /// </summary>
    public static float TurnToward(float current, float desired, float maxStep)
    {
        float delta = Delta(current, desired);

        if (Mathf.Abs(delta) <= maxStep)
            return desired;

        return current + Mathf.Sign(delta) * maxStep;
    }

    /// <summary>Угол, под которым цель видна из точки.</summary>
    public static float AngleTo(Vector2 from, Vector2 to) => (to - from).Angle();

    /// <summary>Попадает ли точка в конус вокруг оси взгляда (halfAngle — половина раствора).</summary>
    public static bool InCone(float facing, Vector2 from, Vector2 to, float halfAngle)
    {
        if (from.IsEqualApprox(to))
            return true;

        return Mathf.Abs(Delta(facing, AngleTo(from, to))) <= halfAngle;
    }
}
