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

    public const string TextureSmokeScenario = "texture-smoke";
    internal static bool IsTextureSmoke { get; private set; }

    public static int Main(string[] args) => args switch
    {
        [CreateWithoutDisplayScenario] => CreateWithoutDisplay(restoreDisplay: null),
        [CreateWithoutDisplayScenario, var display] => CreateWithoutDisplay(display),
        [TextureSmokeScenario] => TextureSmoke(null),
        [TextureSmokeScenario, var test] => TextureSmoke(test),
        _ => 2,
    };

    // Cocoa требует создания окна на главном потоке; MSTest вызывает тесты на рабочем.
    // Этот же entry point позволяет явно запускать весь текстурный smoke на реальном GPU.
    private static int TextureSmoke(string? selected)
    {
        IsTextureSmoke = true;
        var tests = new TextureRenderingTests();
        Action[] checks =
        [
            tests.Cube_front_has_correct_corners_and_asymmetric_F,
            tests.Plane_uses_the_same_UV_orientation_with_top_toward_negative_Z,
            tests.Shared_texture_is_uploaded_once_changes_are_observed_and_cache_lives_until_dispose,
            tests.Texture_multiplies_base_color_and_light_and_can_be_removed_between_frames,
            tests.LoadTexture_enforces_thread_state_and_disposal,
            tests.GPU_texture_is_deleted_and_CPU_data_survives,
            tests.Oversized_texture_reports_source_path_or_in_memory_origin,
        ];
        if (selected is not null && !checks.Any(check => check.Method.Name == selected))
            return 2;
        try
        {
            foreach (var check in checks.Where(check => selected is null || check.Method.Name == selected))
            {
                check();
                Console.WriteLine($"PASS {check.Method.Name}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

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
