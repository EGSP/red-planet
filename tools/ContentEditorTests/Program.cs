using System;

internal static class Program
{
    private static int Main()
    {
        int failed = ContentEditorSelfTest.Run();
        Console.WriteLine(failed == 0 ? "все проверки пройдены" : $"провалено: {failed}");
        return failed == 0 ? 0 : 1;
    }
}
