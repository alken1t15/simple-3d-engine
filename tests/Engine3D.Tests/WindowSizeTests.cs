using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
public sealed class WindowSizeTests
{
    [TestMethod]
    public void Policy_clamps_both_axes_and_reduces_minimum_on_small_displays()
    {
        var normal = WindowSizePolicy.Calculate((800, 450), 1904, 1009);
        Assert.AreEqual((800, 450), WindowSizePolicy.Clamp(normal, 10, 10));
        Assert.AreEqual((1904, 1009), WindowSizePolicy.Clamp(normal, 10000, 10000));
        Assert.AreEqual((960, 640), WindowSizePolicy.Clamp(normal, 960, 640));
        var small = WindowSizePolicy.Calculate((800, 450), 640, 400);
        Assert.AreEqual(new ClientSizeLimits(640, 400, 640, 400), small);
        Assert.AreEqual((640, 400), WindowSizePolicy.Clamp(small, 960, 640));
        Assert.AreEqual(new ClientSizeLimits(1, 1, 1, 1), WindowSizePolicy.Calculate((800, 450), 0, -1));
        Assert.AreEqual((64, 48), WindowSizePolicy.Clamp(
            WindowSizePolicy.Calculate(null, int.MaxValue, int.MaxValue), 64, 48));
    }

    [TestMethod]
    public void Invalid_minimum_is_rejected_before_creating_a_window()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Engine.Create(
            new EngineOptions { MinimumClientSize = (0, 450) }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Engine.Create(
            new EngineOptions { MinimumClientSize = (800, -1) }));
    }

    [TestMethod]
    [GraphicsTest]
    public void Native_limits_and_resize_request_keep_client_inside_available_bounds()
    {
        AllowTestRunnerThreads();
        using var engine = Engine.Create(new EngineOptions { Width = 64, Height = 48,
            MinimumClientSize = (800, 450), LimitToMonitorWorkArea = true, VSync = false });
        int updates = 0;
        bool sawMinimum = false, sawMaximum = false;
        engine.Run(new Scene(), new Camera(), _ =>
        {
            updates++;
            if (updates == 1) engine.SetClientSize(10, 10);
            if (updates == 20)
            {
                Assert.AreEqual((engine.ClientSizeLimits.MinimumWidth, engine.ClientSizeLimits.MinimumHeight), engine.ClientSize);
                sawMinimum = true;
                engine.SetClientSize(10000, 10000);
            }
            if (updates == 40)
            {
                var size = engine.ClientSize;
                var limits = engine.ClientSizeLimits;
                Assert.IsTrue(size.Width >= limits.MinimumWidth && size.Width <= limits.MaximumWidth);
                Assert.IsTrue(size.Height >= limits.MinimumHeight && size.Height <= limits.MaximumHeight);
                sawMaximum = true;
                Console.WriteLine($"Bounded client={size}; limits={limits}; framebuffer={engine.FramebufferSize}");
                engine.RequestClose();
            }
        });
        Assert.IsTrue(sawMinimum && sawMaximum);
    }

    [TestMethod]
    [GraphicsTest]
    public void Resize_obeys_owner_thread_and_update_lifecycle()
    {
        AllowTestRunnerThreads();
        var engine = Engine.Create(new EngineOptions { Width = 320, Height = 240, VSync = false });
        try
        {
            engine.SetClientSize(400, 300);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => engine.SetClientSize(0, 300));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => engine.SetClientSize(400, -1));
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.SetClientSize(400, 300)).GetAwaiter().GetResult());
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.ClientSizeLimits).GetAwaiter().GetResult());
            engine.FrameRendered = (_, _, _) =>
                Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetClientSize(400, 300));
            RunFrames(engine, new Scene(), new Camera(), 4, _ => engine.SetClientSize(400, 300));
            Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetClientSize(400, 300));
        }
        finally { engine.Dispose(); }
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.SetClientSize(400, 300));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = engine.ClientSizeLimits);
    }
}
