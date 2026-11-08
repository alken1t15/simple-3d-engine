using System.Diagnostics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class FullscreenTests
{
    [TestMethod]
    public void Fullscreen_uses_entire_desktop_and_restores_fixed_window_and_limits()
    {
        AllowTestRunnerThreads();
        using var engine = Engine.Create(new EngineOptions { Width = 800, Height = 450, Resizable = false,
            MinimumClientSize = (640, 360), LimitToMonitorWorkArea = true, VSync = false });
        var windowedLimits = engine.WindowedClientSizeLimits;
        var desktop = engine.FullscreenSize;
        Assert.IsTrue(desktop.Width >= 800 && desktop.Height >= 450);
        var timer = Stopwatch.StartNew();
        int stage = 0;
        engine.Run(new Scene(), new Camera(), _ =>
        {
            if (timer.Elapsed.TotalSeconds > 10) Assert.Fail("Fullscreen transition timed out.");
            if (timer.Elapsed.TotalSeconds < (stage + 1) * 0.15) return;
            switch (stage++)
            {
                case 0:
                    Assert.IsFalse(engine.IsFullscreen);
                    engine.SetFullscreen(true);
                    break;
                case 1:
                    Assert.IsTrue(engine.IsFullscreen);
                    Assert.AreEqual(desktop, engine.ClientSize, "Fullscreen must not be clamped by window work area.");
                    Assert.AreEqual(windowedLimits, engine.WindowedClientSizeLimits);
                    Assert.AreEqual(new ClientSizeLimits(desktop.Width, desktop.Height, desktop.Width, desktop.Height), engine.ClientSizeLimits);
                    Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetClientSize(640, 360));
                    engine.SetFullscreen(true); // idempotent: does not replace saved window geometry.
                    engine.SetFullscreen(false);
                    break;
                case 2:
                    Assert.IsFalse(engine.IsFullscreen);
                    Assert.AreEqual((800, 450), engine.ClientSize);
                    Assert.AreEqual(windowedLimits, engine.ClientSizeLimits);
                    engine.SetClientSize(700, 400);
                    break;
                case 3:
                    Assert.AreEqual((700, 400), engine.ClientSize);
                    Assert.IsGreaterThan(0L, engine.RenderedFrames);
                    Console.WriteLine($"Fullscreen desktop={desktop}; restored={engine.ClientSize}; framebuffer={engine.FramebufferSize}");
                    engine.RequestClose();
                    break;
            }
        });
        Assert.AreEqual(4, stage);
    }

    [TestMethod]
    public void Fullscreen_obeys_owner_thread_update_scope_and_disposal()
    {
        AllowTestRunnerThreads();
        var engine = Engine.Create(new EngineOptions { Width = 400, Height = 300, VSync = false });
        try
        {
            engine.SetFullscreen(false);
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.SetFullscreen(true)).GetAwaiter().GetResult());
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.IsFullscreen).GetAwaiter().GetResult());
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.FullscreenSize).GetAwaiter().GetResult());
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.WindowedClientSizeLimits).GetAwaiter().GetResult());
            engine.FrameRendered = (_, _, _) =>
                Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetFullscreen(false));
            RunFrames(engine, new Scene(), new Camera(), 3);
            Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetFullscreen(false));
        }
        finally { engine.Dispose(); }
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.SetFullscreen(false));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = engine.IsFullscreen);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = engine.FullscreenSize);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = engine.WindowedClientSizeLimits);
    }
}
