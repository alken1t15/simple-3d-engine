using Engine3D.Demo;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTK.Graphics.OpenGL4;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class PerformanceCacheTests
{
    [TestMethod]
    public void Performance_scene_reuses_one_GPU_mesh_and_texture_and_releases_both()
    {
        if (OperatingSystem.IsMacOS() && !TestProcess.IsPerformanceSmoke)
        {
            var (exitCode, output) = TestProcess.Run(new Dictionary<string, string?>(), TestProcess.PerformanceSmokeScenario);
            Assert.AreEqual(0, exitCode, output);
            return;
        }
        var engine = Engine.Create(new EngineOptions { Width = 320, Height = 180, VSync = false });
        try
        {
            Console.WriteLine($"OpenGL: {GL.GetString(StringName.Vendor)}; {GL.GetString(StringName.Renderer)}; {GL.GetString(StringName.Version)}");
            var snapshot = PerformanceScene.Create(engine.LoadTexture(TextureDecoderTests.ControlPath));
            Assert.AreEqual(0, engine.GpuMeshCount);
            Assert.AreEqual(0, engine.GpuTextureCount);
            Assert.IsTrue(engine.FramebufferSize.Width > 0 && engine.FramebufferSize.Height > 0);
            Exception? wrongThread = null;
            Task.Run(() => { try { _ = engine.FramebufferSize; } catch (Exception exception) { wrongThread = exception; } }).GetAwaiter().GetResult();
            Assert.IsInstanceOfType<InvalidOperationException>(wrongThread);
            RunFrames(engine, snapshot.Scene, snapshot.Camera, frames: 4, frame =>
            {
                if (frame > 0)
                {
                    Assert.AreEqual(1, engine.GpuMeshCount);
                    Assert.AreEqual(1, engine.GpuTextureCount);
                }
            });
            Assert.AreEqual(1, engine.GpuMeshCount);
            Assert.AreEqual(1, engine.GpuTextureCount);
            Console.WriteLine($"1000 lit cubes: GPU meshes={engine.GpuMeshCount}, textures={engine.GpuTextureCount}; framebuffer={engine.FramebufferSize}");
        }
        finally
        {
            engine.Dispose();
        }
        Assert.AreEqual(0, engine.GpuMeshCount);
        Assert.AreEqual(0, engine.GpuTextureCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = engine.FramebufferSize);
        Console.WriteLine("After Dispose: GPU meshes=0, textures=0");
    }
}
