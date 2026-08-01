using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Сравнение по ссылке в обход Equals и GetHashCode самого объекта: у освобождённой
/// обёртки движка они небезопасны, а от снятого объекта нужна только его личность.
/// </summary>
public sealed class ByReference : IEqualityComparer<object>
{
    public static readonly ByReference Instance = new();

    public new bool Equals(object a, object b) => ReferenceEquals(a, b);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
