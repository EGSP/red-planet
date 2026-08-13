using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

/// <summary>
/// Очистка внешних кэшей перед hot reload C# в Godot.
///
/// System.Text.Json хранит сведения о сериализуемых типах и способен удерживать
/// выгружаемый AssemblyLoadContext. Это известная причина сообщения
/// "Failed to unload assemblies" (godotengine/godot#78513).
/// </summary>
internal static class AssemblyUnloadCleanup
{
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
        var handler = typeof(JsonSerializerOptions).Assembly.GetType(
            "System.Text.Json.JsonSerializerOptionsUpdateHandler");
        var clear = handler?.GetMethod(
            "ClearCache",
            BindingFlags.Public | BindingFlags.Static);
        clear?.Invoke(null, new object[] { null });
    }
}
