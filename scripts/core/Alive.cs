using System;
using Godot;

/// <summary>
/// Безопасная проверка живости ноды.
/// У освобождённой C#-обёртки сам IsInstanceValid бросает ObjectDisposedException,
/// поэтому наружу ходим только через эту обёртку.
/// </summary>
public static class Alive
{
    public static bool Is(GodotObject obj)
    {
        try
        {
            return obj != null && GodotObject.IsInstanceValid(obj);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }
}
