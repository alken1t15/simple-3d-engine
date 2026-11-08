using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class WindowIconTests
{
    [TestMethod]
    public void Icon_obeys_owner_thread_update_scope_and_disposal()
    {
        AllowTestRunnerThreads();
        var engine = CreateEngine("Icon lifecycle");
        var icon = new TextureData(1, 1, [255, 90, 30, 255]);
        try
        {
            engine.SetWindowIcon(icon);
            Assert.ThrowsExactly<ArgumentNullException>(() => engine.SetWindowIcon(null!));
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.SetWindowIcon(icon)).GetAwaiter().GetResult());
            engine.FrameRendered = (_, _, _) => Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetWindowIcon(icon));
            RunFrames(engine, new Scene(), new Camera(), 3, _ => engine.SetWindowIcon(icon));
            Assert.ThrowsExactly<InvalidOperationException>(() => engine.SetWindowIcon(icon));
        }
        finally { engine.Dispose(); }
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.SetWindowIcon(icon));
    }
}
