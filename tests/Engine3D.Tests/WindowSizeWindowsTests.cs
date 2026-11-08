using System.Diagnostics;
using System.Runtime.InteropServices;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class WindowSizeWindowsTests
{
    [TestMethod]
    public void Resize_restores_native_WM_maximized_and_minimized_states()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires the Windows window manager.");
        AllowTestRunnerThreads();
        string title = $"Engine size regression {Guid.NewGuid()}";
        using var engine = Engine.Create(new EngineOptions { Title = title, Width = 400, Height = 300,
            MinimumClientSize = (320, 240), LimitToMonitorWorkArea = true, VSync = false });
        var clock = Stopwatch.StartNew();
        int stage = 0;
        engine.Run(new Scene(), new Camera(), _ =>
        {
            if (clock.Elapsed.TotalSeconds > 5) Assert.Fail("Window state transition timed out.");
            if (clock.Elapsed.TotalSeconds < (stage + 1) * 0.15) return;
            nint window = FindWindow(null, title);
            Assert.AreNotEqual(nint.Zero, window);
            switch (stage++)
            {
                case 0: ShowWindow(window, 3); break; // SW_SHOWMAXIMIZED — изменение через WM, не OpenTK cache.
                case 1:
                    Assert.IsTrue(IsZoomed(window));
                    engine.SetClientSize(400, 300);
                    break;
                case 2:
                    Assert.IsFalse(IsZoomed(window));
                    Assert.AreEqual((400, 300), engine.ClientSize);
                    ShowWindow(window, 3);
                    ShowWindow(window, 6); // SW_MINIMIZE из maximized.
                    break;
                case 3:
                    Assert.IsTrue(IsIconic(window)); // Refresh не восстанавливает окно сам.
                    engine.SetClientSize(400, 300);
                    break;
                case 4:
                    Assert.IsFalse(IsIconic(window));
                    Assert.IsFalse(IsZoomed(window));
                    Assert.AreEqual((400, 300), engine.ClientSize);
                    engine.RequestClose();
                    break;
            }
        });
        Assert.AreEqual(5, stage);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint window);
}
