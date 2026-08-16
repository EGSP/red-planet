using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

/// <summary>
/// Действия перед выгрузкой игровой C#-сборки в редакторе Godot.
///
/// System.Text.Json хранит сведения о сериализуемых типах и способен удерживать
/// выгружаемый AssemblyLoadContext. Это известная причина сообщения
/// "Failed to unload assemblies" (godotengine/godot#78513).
///
/// Кроме кэша JSON здесь вызывается <see cref="BeforeUnload"/>: C#-узлы плагина
/// редактора контента должны покинуть дерево, пока управляемый код ещё исполняется.
/// EditorPlugin._ExitTree для этого недостаточен — Godot начинает выгрузку ALC раньше.
/// </summary>
internal static class AssemblyUnloadCleanup
{
    /// <summary>
    /// Подписчики исполняются в AssemblyLoadContext.Unloading, затем список обнуляется.
    /// </summary>
    internal static event Action BeforeUnload;

#pragma warning disable CA2255 // Godot загружает игровую DLL как приложение в отдельном ALC.
    [ModuleInitializer]
    internal static void Initialize()
    {
        AssemblyLoadContext context =
            AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
        if (context != null)
            context.Unloading += OnUnloading;
    }
#pragma warning restore CA2255

    private static void OnUnloading(AssemblyLoadContext _)
    {
        try
        {
            BeforeUnload?.Invoke();
        }
        catch (Exception)
        {
            // Освобождение узлов не должно прерывать остальную очистку выгрузки.
        }

        BeforeUnload = null;

        var handler = typeof(JsonSerializerOptions).Assembly.GetType(
            "System.Text.Json.JsonSerializerOptionsUpdateHandler");
        var clear = handler?.GetMethod(
            "ClearCache",
            BindingFlags.Public | BindingFlags.Static);
        clear?.Invoke(null, new object[] { null });
    }
}
