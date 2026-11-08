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
    public void Fixed_window_has_no_resize_frame_or_resize_hit_targets_but_API_can_resize()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires the Windows window manager.");
        AllowTestRunnerThreads();
        string title = $"Engine fixed frame regression {Guid.NewGuid()}";
        using var engine = Engine.Create(new EngineOptions { Title = title, Width = 400, Height = 300,
            Resizable = false, VSync = false });
        var clock = Stopwatch.StartNew();
        bool requested = false, verified = false;
        engine.Run(new Scene(), new Camera(), _ =>
        {
            if (clock.Elapsed.TotalSeconds > 5) Assert.Fail("Fixed window resize timed out.");
            if (clock.Elapsed.TotalSeconds < 0.15) return;
            nint window = FindWindow(null, title);
            Assert.AreNotEqual(nint.Zero, window);
            Assert.AreEqual(0, GetWindowLong(window, -16) & 0x00040000, "WS_THICKFRAME permits edge resizing.");
            Assert.IsTrue(GetWindowRect(window, out var rect));
            foreach (var (x, y) in new[] {
                (rect.Left + 1, rect.Top + 1), (rect.Right - 2, rect.Top + 1),
                (rect.Left + 1, rect.Bottom - 2), (rect.Right - 2, rect.Bottom - 2),
                ((rect.Left + rect.Right) / 2, rect.Top + 1), ((rect.Left + rect.Right) / 2, rect.Bottom - 2),
                (rect.Left + 1, (rect.Top + rect.Bottom) / 2), (rect.Right - 2, (rect.Top + rect.Bottom) / 2) })
            {
                nint point = (nint)((y & 0xffff) << 16 | (x & 0xffff));
                long target = SendMessage(window, 0x0084, 0, point).ToInt64(); // WM_NCHITTEST
                Assert.IsFalse(target is >= 10 and <= 17, $"Edge/angle hit-test returned resize target {target}.");
            }
            if (!requested)
            {
                Assert.AreEqual((400, 300), engine.ClientSize);
                engine.SetClientSize(500, 350);
                requested = true;
            }
            else if (clock.Elapsed.TotalSeconds > 0.3)
            {
                Assert.AreEqual((500, 350), engine.ClientSize);
                verified = true;
                engine.RequestClose();
            }
        });
        Assert.IsTrue(verified);
    }

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
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
