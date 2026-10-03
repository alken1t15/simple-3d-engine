using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTkSpike;

// Использование: dotnet run -- [параметры окна]  — описание параметров в SpikeOptions;
//                dotnet run -- --compare a.png b.png [...] — сравнение кадров, см. ImageCompare.
//
// Любая ошибка перехватывается здесь и завершает процесс штатно с кодом 1. Неперехваченное исключение
// .NET завершает процесс сигналом SIGABRT, а macOS показывает на это системный диалог о сбое программы.
try
{
    return Run(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception}");
    return 1;
}

static int Run(string[] args)
{
    if (args.Length > 0 && args[0] == "--compare")
        return ImageCompare.Run(args[1..]);

    var options = SpikeOptions.Parse(args);

    Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
    Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");

    for (int cycle = 1; cycle <= options.Cycles; cycle++)
    {
        using (var window = new SpikeWindow(options))
        {
            window.Run();
            Console.WriteLine($"Cycle {cycle}/{options.Cycles}: rendered frames: {window.RenderedFrames} (from refresh callback: {window.RefreshFrames})");
        }

        // Рост памяти от цикла к циклу означал бы, что окно, контекст или GPU-ресурсы не освобождаются.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        using var process = Process.GetCurrentProcess();
        Console.WriteLine($"  managed heap: {GC.GetTotalMemory(forceFullCollection: true) / 1024} KiB, "
            + $"process working set: {process.WorkingSet64 / (1024 * 1024)} MiB");
    }

    if (options.ScreenshotPath is not null)
        Console.WriteLine($"Screenshot: {Path.GetFullPath(options.ScreenshotPath)}");

    Console.WriteLine("Window closed without exceptions.");
    return 0;
}
