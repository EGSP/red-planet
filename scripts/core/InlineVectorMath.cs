using System.Runtime.CompilerServices;
using Godot;

/// <summary>
/// Плоская арифметика над <see cref="Vector2"/>, встраиваемая в место вызова.
///
/// ЗАЧЕМ, ЕСЛИ ОПЕРАТОРЫ УЖЕ ЕСТЬ. Операторы <c>Vector2</c> живут в сборке GodotSharp
/// и признака встраивания не несут, поэтому в горячем цикле каждый из них остаётся
/// настоящим вызовом. По замеру системы движения на вычитание, сложение, деление и длину
/// уходило 7,7 % времени главного потока — при том, что работы там на несколько тактов.
/// Здесь те же действия расписаны по координатам и помечены к встраиванию.
///
/// ЧТО ЗДЕСЬ ГЛАВНОЕ. Составные величины, которые в обычной записи требуют промежуточного
/// вектора: расстояние между двумя точками, расстояние до предсказанной точки, проекция.
/// Промежуточный вектор сам по себе дёшев, но каждое обращение к нему есть вызов, и
/// в цикле по соседям их набирается по пять-шесть на кандидата.
///
/// ГДЕ ПРИМЕНЯТЬ. В перечислениях, идущих по разу на пару сущностей за шаг: обход соседей,
/// расхождение, выбор цели, урон по области. В коде, исполняемом по разу за кадр,
/// разницы не будет, и там обычная запись через операторы понятнее.
/// </summary>
public static class InlineVectorMath
{
    /// <summary>Квадрат расстояния между точками. Без корня и без промежуточного вектора.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistanceSquared(in Vector2 a, in Vector2 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;

        return dx * dx + dy * dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(in Vector2 a, in Vector2 b) =>
        Mathf.Sqrt(DistanceSquared(in a, in b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LengthSquared(in Vector2 v) => v.X * v.X + v.Y * v.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Length(in Vector2 v) => Mathf.Sqrt(LengthSquared(in v));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Sub(in Vector2 a, in Vector2 b) => new(a.X - b.X, a.Y - b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Add(in Vector2 a, in Vector2 b) => new(a.X + b.X, a.Y + b.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Mul(in Vector2 v, float scale) => new(v.X * scale, v.Y * scale);

    /// <summary>Сложить с вектором, умноженным на число: <c>a + b · scale</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Fma(in Vector2 a, in Vector2 b, float scale) =>
        new(a.X + b.X * scale, a.Y + b.Y * scale);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Dot(in Vector2 a, in Vector2 b) => a.X * b.X + a.Y * b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross(in Vector2 a, in Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// Единичный вектор из уже известной длины. В обходе соседей длина считается ради
    /// сравнения с порогом, и делить на неё дешевле, чем звать нормализацию, которая
    /// посчитает её заново.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Unit(in Vector2 v, float length)
    {
        float inverse = 1f / length;

        return new Vector2(v.X * inverse, v.Y * inverse);
    }

    /// <summary>Перпендикуляр в том же смысле, что <see cref="Vector2.Orthogonal"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Orthogonal(in Vector2 v) => new(v.Y, -v.X);
}
