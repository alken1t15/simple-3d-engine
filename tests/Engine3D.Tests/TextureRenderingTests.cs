using System.Numerics;
using System.Runtime.CompilerServices;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTK.Graphics.OpenGL4;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

[TestClass]
[GraphicsTest]
public sealed class TextureRenderingTests
{
    private static bool RunOnMainThread([CallerMemberName] string test = "")
    {
        if (!OperatingSystem.IsMacOS() || TestProcess.IsTextureSmoke)
            return false;
        var (exitCode, output) = TestProcess.Run(new Dictionary<string, string?>(), TestProcess.TextureSmokeScenario, test);
        Assert.AreEqual(0, exitCode, output);
        return true;
    }

    [TestMethod]
    public void Cube_front_has_correct_corners_and_asymmetric_F()
    {
        if (RunOnMainThread())
            return;
        using var engine = CreateEngine(nameof(Cube_front_has_correct_corners_and_asymmetric_F));
        var texture = engine.LoadTexture(TextureDecoderTests.ControlPath);
        var scene = new Scene();
        scene.Lighting.AmbientColor = Vector3.One;
        scene.Lighting.DirectionalColor = Vector3.Zero;
        scene.Add(new SceneObject(PrimitiveFactory.CreateCube(2f), new MaterialData { Texture = texture }));
        var camera = new Camera { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        RunFrames(engine, scene, camera, 2);
        Assert.IsNotNull(last);
        AssertColor(0, 90, 255, last.At(camera, new Vector3(-0.75f, 0.75f, 1f)), "upper left");
        AssertColor(255, 210, 0, last.At(camera, new Vector3(0.75f, 0.75f, 1f)), "upper right");
        AssertColor(230, 30, 30, last.At(camera, new Vector3(-0.75f, -0.75f, 1f)), "lower left");
        AssertColor(20, 180, 60, last.At(camera, new Vector3(0.75f, -0.75f, 1f)), "lower right");
        AssertColor(0, 0, 0, last.At(camera, new Vector3(-0.22f, -0.25f, 1f)), "F left stem");
        AssertColor(235, 235, 235, last.At(camera, new Vector3(0.22f, -0.25f, 1f)), "no mirrored stem");
        Assert.AreEqual(1, engine.GpuTextureCount);
    }

    [TestMethod]
    public void Plane_uses_the_same_UV_orientation_with_top_toward_negative_Z()
    {
        if (RunOnMainThread())
            return;
        using var engine = CreateEngine(nameof(Plane_uses_the_same_UV_orientation_with_top_toward_negative_Z));
        var scene = new Scene();
        scene.Lighting.AmbientColor = Vector3.One;
        scene.Lighting.DirectionalColor = Vector3.Zero;
        scene.Add(new SceneObject(PrimitiveFactory.CreatePlane(2f, 2f), new MaterialData
        {
            Texture = engine.LoadTexture(TextureDecoderTests.ControlPath),
        }));
        var camera = new Camera { Position = new Vector3(0f, 4f, 0f), Target = Vector3.Zero, Up = -Vector3.UnitZ };
        Frame? last = null;
        engine.FrameRendered = (w, h, pixels) => last = new Frame(w, h, pixels);
        RunFrames(engine, scene, camera, 2);
        Assert.IsNotNull(last);
        AssertColor(0, 90, 255, last.At(camera, new Vector3(-0.75f, 0f, -0.75f)), "plane upper left");
        AssertColor(255, 210, 0, last.At(camera, new Vector3(0.75f, 0f, -0.75f)), "plane upper right");
        AssertColor(230, 30, 30, last.At(camera, new Vector3(-0.75f, 0f, 0.75f)), "plane lower left");
        AssertColor(20, 180, 60, last.At(camera, new Vector3(0.75f, 0f, 0.75f)), "plane lower right");
    }

    [TestMethod]
    public void Shared_texture_is_uploaded_once_changes_are_observed_and_cache_lives_until_dispose()
    {
        if (RunOnMainThread())
            return;
        var engine = CreateEngine(nameof(Shared_texture_is_uploaded_once_changes_are_observed_and_cache_lives_until_dispose));
        try
        {
            var texture = engine.LoadTexture(TextureDecoderTests.ControlPath);
            Assert.AreEqual(0, engine.GpuTextureCount); // LoadTexture только CPU
            var scene = new Scene();
            var cube = PrimitiveFactory.CreateCube();
            for (int i = 0; i < 1000; i++)
                scene.Add(new SceneObject(cube, new MaterialData { Texture = texture }));
            var camera = new Camera { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };
            RunFrames(engine, scene, camera, 4, frame =>
            {
                if (frame == 1)
                {
                    Assert.AreEqual(1, engine.GpuTextureCount);
                    scene.Objects[0].Material.Texture = new TextureData(texture.Width, texture.Height, texture.Pixels);
                }
                if (frame == 2)
                {
                    Assert.AreEqual(2, engine.GpuTextureCount); // равные пиксели, другая идентичность
                    foreach (var item in scene.Objects.ToArray())
                        scene.Remove(item);
                }
            });
            Assert.AreEqual(2, engine.GpuTextureCount);
        }
        finally
        {
            engine.Dispose();
        }
        Assert.AreEqual(0, engine.GpuTextureCount);
    }

    [TestMethod]
    public void Texture_multiplies_base_color_and_light_and_can_be_removed_between_frames()
    {
        if (RunOnMainThread())
            return;
        using var engine = CreateEngine(nameof(Texture_multiplies_base_color_and_light_and_can_be_removed_between_frames));
        var scene = new Scene();
        var material = new MaterialData
        {
            Texture = new TextureData(1, 1, [200, 100, 50, 0]),
            BaseColor = new ColorRgba(0.5f, 0.8f, 1f, 0f), // alpha не включает blending
        };
        scene.Add(new SceneObject(PrimitiveFactory.CreateCube(2f), material));
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(0.4f, 0.6f, 0.8f), new Vector3(1.7f, 0f, 1f), new Vector3(0.8f));
        scene.Lighting.AmbientColor = new Vector3(0.2f);
        scene.Lighting.DirectionalColor = new Vector3(0.5f);
        var camera = new Camera { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };
        var frames = new Frame?[3];
        int current = -1;
        engine.FrameRendered = (w, h, pixels) =>
        {
            if (current >= 0)
                frames[current] = new Frame(w, h, pixels);
        };
        RunFrames(engine, scene, camera, 3, frame =>
        {
            current = frame;
            scene.Lighting.Direction = frame == 1 ? Vector3.UnitZ : -Vector3.UnitZ;
            if (frame == 2)
                material.Texture = null;
        });
        Assert.IsNotNull(frames[0]);
        Assert.IsNotNull(frames[1]);
        Assert.IsNotNull(frames[2]);
        AssertColor(70, 56, 35, frames[0]!.At(camera, Vector3.UnitZ), "textured, light toward front");
        AssertColor(20, 16, 10, frames[1]!.At(camera, Vector3.UnitZ), "textured, ambient only");
        AssertColor(Level(0.35f), Level(0.56f), Level(0.7f), frames[2]!.At(camera, Vector3.UnitZ), "untextured");
        for (int i = 0; i < frames.Length; i++)
        {
            float intensity = i == 1 ? 0.2f : 0.7f;
            AssertColor(Level(0.4f * intensity), Level(0.6f * intensity), Level(0.8f * intensity),
                frames[i]!.At(camera, new Vector3(1.7f, 0f, 1.4f)), "untextured object drawn after textured object");
        }
        foreach (var frame in frames)
            Assert.AreEqual((byte)255, frame!.Pixels[(frame.Height / 2 * frame.Width + frame.Width / 2) * 4 + 3]);
    }

    [TestMethod]
    public void LoadTexture_enforces_thread_state_and_disposal()
    {
        if (RunOnMainThread())
            return;
        var engine = CreateEngine(nameof(LoadTexture_enforces_thread_state_and_disposal));
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.LoadTexture(TextureDecoderTests.ControlPath)).GetAwaiter().GetResult());
            Assert.ThrowsExactly<FileNotFoundException>(() => engine.LoadTexture("missing-texture.png"));
            engine.LoadTexture(TextureDecoderTests.ControlPath); // ошибка загрузки не портит Engine
            RunFrames(engine, new Scene(), new Camera(), 1, _ =>
                Assert.ThrowsExactly<InvalidOperationException>(() => engine.LoadTexture(TextureDecoderTests.ControlPath)));
            Assert.ThrowsExactly<InvalidOperationException>(() => engine.LoadTexture(TextureDecoderTests.ControlPath));
        }
        finally
        {
            engine.Dispose();
        }
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.LoadTexture(TextureDecoderTests.ControlPath));
    }

    [TestMethod]
    public void GPU_texture_is_deleted_and_CPU_data_survives()
    {
        if (RunOnMainThread())
            return;
        using var engine = CreateEngine(nameof(GPU_texture_is_deleted_and_CPU_data_survives));
        var texture = new TextureData(1, 1, [200, 100, 50, 255]);
        var gpu = new GpuTexture(texture);
        Assert.IsTrue(GL.IsTexture(gpu.Handle));
        gpu.Dispose();
        gpu.Dispose();
        Assert.IsFalse(GL.IsTexture(gpu.Handle));
        CollectionAssert.AreEqual(new byte[] { 200, 100, 50, 255 }, texture.Pixels.ToArray());
    }
}
