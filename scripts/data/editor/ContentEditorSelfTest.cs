using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Автоматические проверки TomlPatchWriter. Запуск: dotnet run --project tools/ContentEditorTests
/// либо вызов ContentEditorSelfTest.Run() из редактора.
/// </summary>
public static class ContentEditorSelfTest
{
    public static int Run()
    {
        int failed = 0;
        failed += Check("set root key", SetRootKey);
        failed += Check("set section key", SetSectionKey);
        failed += Check("preserve comment", PreserveComment);
        failed += Check("remove last key drops section", RemoveLastKeyDropsSection);
        failed += Check("snake enum", SnakeEnum);
        return failed;
    }

    private static int Check(string name, Func<bool> test)
    {
        try
        {
            if (test())
            {
                Console.WriteLine($"[ok] {name}");
                return 0;
            }

            Console.WriteLine($"[fail] {name}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[fail] {name}: {ex.Message}");
            return 1;
        }
    }

    private static bool SetRootKey()
    {
        const string source = """
            id = "maul"
            name = "Молот"

            [body]
            max_health = 400.0
            """;

        string result = TomlPatchWriter.SetKey(source, null, "color", "[0.8, 0.4, 0.4]");
        return result.Contains("color = [0.8, 0.4, 0.4]")
               && result.Contains("id = \"maul\"")
               && result.Contains("[body]");
    }

    private static bool SetSectionKey()
    {
        const string source = """
            id = "maul"

            [body]
            max_health = 400.0
            # прочность
            radius = 0.48
            """;

        string result = TomlPatchWriter.SetKey(source, "body", "max_health", "500.0");
        return result.Contains("max_health = 500.0")
               && result.Contains("# прочность")
               && result.Contains("radius = 0.48");
    }

    private static bool PreserveComment()
    {
        const string source = """
            # заголовок файла
            id = "x"

            [movement]
            speed = 2.0 # быстро
            """;

        string result = TomlPatchWriter.SetKey(source, "movement", "turn_speed", "45.0");
        return result.Contains("# заголовок файла")
               && result.Contains("speed = 2.0 # быстро")
               && result.Contains("turn_speed = 45.0");
    }

    private static bool RemoveLastKeyDropsSection()
    {
        const string source = """
            id = "x"

            [terror]
            army_power = 2.0
            """;

        string result = TomlPatchWriter.RemoveKey(source, "terror", "army_power");
        return !result.Contains("[terror]")
               && !result.Contains("army_power")
               && result.Contains("id = \"x\"");
    }

    private static bool SnakeEnum() =>
        TomlPatchWriter.ToSnake("MetalArea") == "metal_area"
        && TomlPatchWriter.FormatEnum("AttackMove") == "\"attack_move\"";
}
