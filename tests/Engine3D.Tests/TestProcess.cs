using System.Diagnostics;
using System.Runtime.InteropServices;
using Engine3D.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Engine3D.Tests;

/// <summary>
/// Сценарии, которым нужен отдельный процесс: состояние GLFW общее для процесса, поэтому отказ его инициализации
/// нельзя проверять рядом с другими графическими тестами. Сборка тестов запускается как программа
/// (<c>dotnet Engine3D.Tests.dll сценарий</c>); testhost загружает ее как библиотеку и <see cref="Main"/> не вызывает.
/// </summary>
public static class TestProcess
{
    /// <summary>
    /// Заведомо недоступный X11-дисплей: TCP-порт 6000 + 59999 вне допустимого диапазона, соединение не устанавливается.
    /// </summary>
    public const string UnavailableDisplay = "127.0.0.1:59999";

    public const string CreateWithoutDisplayScenario = "create-without-display";

    public static int Main(string[] args) => args switch
    {
        [CreateWithoutDisplayScenario] => CreateWithoutDisplay(restoreDisplay: null),
        [CreateWithoutDisplayScenario, var display] => CreateWithoutDisplay(display),
        _ => 2,
    };

    /// <summary>Запускает сценарий в новом процессе с измененным окружением (null удаляет переменную).</summary>
    public static (int ExitCode, string Output) Run(IReadOnlyDictionary<string, string?> environment, params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(TestProcess).Assembly.Location);
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        foreach (var (name, value) in environment)
        {
            if (value is null)
                start.Environment.Remove(name);
            else
                start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start the test process.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Test process '{string.Join(' ', args)}' did not finish in 1 minute.");
        }

        return (process.ExitCode, output.Result + errors.Result);
    }

    // Дважды пытается создать Engine на недоступном дисплее (ожидаются две содержательные ошибки), затем, если задан
    // настоящий дисплей, возвращает его и проверяет, что Create в том же процессе снова работает.
    private static int CreateWithoutDisplay(string? restoreDisplay)
    {
        // Только X11: иначе GLFW может найти Wayland (например, WSLg) и при пустых DISPLAY и WAYLAND_DISPLAY.
        GLFW.InitHint(InitHintPlatform.Platform, Platform.X11);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var engine = Engine.Create(new EngineOptions());
                Console.WriteLine($"attempt {attempt}: created unexpectedly");
                return 1;
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine($"attempt {attempt}: {exception.Message}");
            }
        }

        if (restoreDisplay is null)
            return 0;

        // GLFW читает DISPLAY из окружения процесса C; Environment.SetEnvironmentVariable его не меняет.
        if (SetEnvironmentVariable("DISPLAY", restoreDisplay, overwrite: 1) != 0)
            return 3;

        using (Engine.Create(new EngineOptions { Width = 320, Height = 240 }))
            Console.WriteLine("after restore: created");
        return 0;
    }

    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int SetEnvironmentVariable(string name, string value, int overwrite);
}
