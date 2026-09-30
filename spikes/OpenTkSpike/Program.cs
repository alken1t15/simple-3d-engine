using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTkSpike;

// Использование: dotnet run -- [--frames N] [--cycles N] [--screenshot file.png] [--depth on|off]
// Описание параметров — в SpikeOptions.
var options = SpikeOptions.Parse(args);

Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");

for (int cycle = 1; cycle <= options.Cycles; cycle++)
{
    using (var window = new SpikeWindow(options))
    {
        window.Run();
        Console.WriteLine($"Cycle {cycle}/{options.Cycles}: rendered frames: {window.RenderedFrames}");
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
