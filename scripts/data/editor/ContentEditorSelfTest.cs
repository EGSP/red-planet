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
        failed += Check("root key stops at table array", RootKeyStopsAtTableArray);
        failed += Check("set key in table array item", SetKeyInTableArrayItem);
        failed += Check("add table array item", AddTableArrayItem);
        failed += Check("remove table array item with comment", RemoveTableArrayItem);
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

    private const string WaveSource = """
        id   = "swarm"
        name = "Рой"

        # ядро волны
        [[unit_list]]
        mode  = "allow"
        units = ["shank"]

        [[unit_list]]
        mode                = "limit"
        units               = ["maul"]
        target_budget_share = 0.35

        [spawn]
        near_arc_degrees = 45.0
        """;

    /// <summary>
    /// Корневой ключ не должен попасть внутрь блока [[unit_list]]: до поддержки
    /// массивов таблиц строки таких блоков считались продолжением корня файла.
    /// </summary>
    private static bool RootKeyStopsAtTableArray()
    {
        string result = TomlPatchWriter.SetKey(WaveSource, null, "army_power_budget", "20.0");
        int budget = result.IndexOf("army_power_budget", StringComparison.Ordinal);
        int firstBlock = result.IndexOf("[[unit_list]]", StringComparison.Ordinal);
        return budget > 0 && firstBlock > 0 && budget < firstBlock;
    }

    private static bool SetKeyInTableArrayItem()
    {
        if (TomlPatchWriter.ArrayItemCount(WaveSource, "unit_list") != 2)
            return false;

        string result = TomlPatchWriter.SetArrayItemKey(
            WaveSource, "unit_list", 1, "target_budget_share", "0.5");
        return result.Contains("target_budget_share = 0.5")
               && result.Contains("units = [\"shank\"]")
               && result.Contains("# ядро волны")
               && !result.Contains("0.35");
    }

    private static bool AddTableArrayItem()
    {
        string result = TomlPatchWriter.AddArrayItem(
            WaveSource, "unit_list",
            new[] { ("mode", "\"deny\""), ("units", "[\"ares\"]") });
        return TomlPatchWriter.ArrayItemCount(result, "unit_list") == 3
               && result.Contains("mode = \"deny\"")
               && result.Contains("[spawn]");
    }

    private static bool RemoveTableArrayItem()
    {
        string result = TomlPatchWriter.RemoveArrayItem(WaveSource, "unit_list", 0);
        return TomlPatchWriter.ArrayItemCount(result, "unit_list") == 1
               && !result.Contains("# ядро волны")
               && !result.Contains("\"shank\"")
               && result.Contains("\"maul\"")
               && result.Contains("[spawn]");
    }

    private static bool SnakeEnum() =>
        TomlPatchWriter.ToSnake("MetalArea") == "metal_area"
        && TomlPatchWriter.FormatEnum("AttackMove") == "\"attack_move\"";
}
