using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class ClientSizeTests
{
    [TestMethod]
    public void Logical_size_and_overlay_do_not_query_global_window_position()
    {
        AllowTestRunnerThreads();
        using var engine = Engine.Create(new EngineOptions { Width = 320, Height = 240, VSync = false });
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        engine.SetOverlay(new OverlayFrame([
            new(new UiRect(20, 30, 40, 50), new ColorRgba(1, 0, 0))], [], []));
        RunFrames(engine, new Scene(), new Camera(), 6, _ =>
        {
            Assert.IsTrue(engine.ClientSize.Width > 0 && engine.ClientSize.Height > 0);
        });
        var client = engine.ClientSize;
        Assert.IsNotNull(last);
        float sx = (float)last.Width / client.Width, sy = (float)last.Height / client.Height;
        Console.WriteLine($"Client={client}; framebuffer={engine.FramebufferSize}; scale={sx},{sy}");
        if (Environment.GetEnvironmentVariable("ENGINE_EXPECT_HIDPI") == "1")
        {
            Assert.AreEqual(2f, sx, 0.01f);
            Assert.AreEqual(2f, sy, 0.01f);
        }
        // Проверяем фактические pixels от renderer, а не синтетический размер viewport.
        var inside = last.At((int)(40 * sx), last.Height - 1 - (int)(50 * sy));
        Assert.IsGreaterThan(240, inside.R);
        Assert.IsLessThan(10, inside.G);
        var outside = last.At((int)(70 * sx), last.Height - 1 - (int)(50 * sy));
        Assert.IsLessThan(100, outside.R);
    }
}
