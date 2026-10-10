using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class EngineCreateTests
{
    [TestMethod]
    public void Options_defaults_match_contract()
    {
        var options = new EngineOptions();

        Assert.AreEqual(1280, options.Width);
        Assert.AreEqual(720, options.Height);
        Assert.AreEqual("Engine3D Demo", options.Title);
        Assert.IsTrue(options.VSync);
    }

    [TestMethod]
    public void Create_rejects_null_options()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Engine.Create(null!));
    }

    [TestMethod]
    [DataRow(0, 720)]
    [DataRow(-1, 720)]
    [DataRow(1280, 0)]
    [DataRow(1280, -720)]
    public void Create_rejects_invalid_size_before_creating_a_window(int width, int height)
    {
        // Проверка до обращения к GLFW: тест проходит и без дисплея.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Engine.Create(new EngineOptions { Width = width, Height = height }));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_empty_title_before_creating_a_window(string title)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Engine.Create(new EngineOptions { Title = title }));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public void Create_reports_unavailable_display_on_every_attempt()
    {
        // Отдельный процесс с заведомо недоступным X11-дисплеем: GLFW там еще не инициализирован, а его состояние
        // не влияет на остальные тесты. Повторный Create должен снова сообщить причину, а не NotInitialized.
        var (exitCode, output) = RunWithUnavailableDisplay();

        Assert.AreEqual(0, exitCode, output);
        AssertPlatformUnavailable(output);
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [GraphicsTest]
    public void Create_succeeds_in_the_same_process_after_the_display_becomes_available()
    {
        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(display))
            Assert.Inconclusive("Requires an X11 display in DISPLAY.");

        var (exitCode, output) = RunWithUnavailableDisplay(display);

        Assert.AreEqual(0, exitCode, output);
        AssertPlatformUnavailable(output);
        Assert.Contains("after restore: created", output);
    }

    private static (int ExitCode, string Output) RunWithUnavailableDisplay(params string[] restoreDisplay) =>
        TestProcess.Run(
            new Dictionary<string, string?> { ["DISPLAY"] = TestProcess.UnavailableDisplay, ["WAYLAND_DISPLAY"] = null },
            [TestProcess.CreateWithoutDisplayScenario, .. restoreDisplay]);

    private static void AssertPlatformUnavailable(string output)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
            Assert.Contains($"attempt {attempt}: Failed to create an OpenGL 3.3 core window: GLFW reported PlatformUnavailable: ", output);
    }
}
