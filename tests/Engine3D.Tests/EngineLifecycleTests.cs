using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

/// <summary>Жизненный цикл Engine по разделу 7 API на реальном окне и контексте OpenGL.</summary>
[TestClass]
[GraphicsTest]
public sealed class EngineLifecycleTests
{
    [TestMethod]
    public void Run_shows_frames_until_RequestClose_and_counts_them()
    {
        var engine = CreateEngine(nameof(Run_shows_frames_until_RequestClose_and_counts_them));
        long rendered = 0;
        engine.FrameRendered = (_, _, _) => rendered++;
        var deltas = new List<float>();

        engine.Run(new Scene(), new Camera(), deltaSeconds =>
        {
            deltas.Add(deltaSeconds);
            if (deltas.Count == 30)
                engine.RequestClose(); // следующий кадр уже не показывается
        });
        engine.Dispose();

        Assert.HasCount(30, deltas);
        Assert.AreEqual(0f, deltas[0]);
        foreach (float delta in deltas)
            Assert.IsTrue(float.IsFinite(delta) && delta >= 0f, $"Delta {delta} must be finite and non-negative.");

        // 29 кадров после onUpdate; перерисовка по запросу системы (refresh) тоже считается показанным кадром.
        Assert.IsGreaterThanOrEqualTo(29L, engine.RenderedFrames);
        Assert.AreEqual(rendered, engine.RenderedFrames); // счетчик доступен и после Dispose
    }

    [TestMethod]
    public void Misuse_is_rejected_with_contract_exceptions()
    {
        var scene = new Scene();
        var camera = new Camera();
        var engine = CreateEngine(nameof(Misuse_is_rejected_with_contract_exceptions));
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(engine.RequestClose); // Run еще не вызывался
            Assert.ThrowsExactly<ArgumentNullException>(() => engine.Run(null!, camera));
            Assert.ThrowsExactly<ArgumentNullException>(() => engine.Run(scene, null!));
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(() => engine.Run(scene, camera)).GetAwaiter().GetResult());

            RunFrames(engine, scene, camera, frames: 2);

            Assert.ThrowsExactly<InvalidOperationException>(() => engine.Run(scene, camera)); // один Run на Engine
            engine.RequestClose(); // после завершения Run ничего не делает
            Assert.ThrowsExactly<InvalidOperationException>(() => Task.Run(engine.Dispose).GetAwaiter().GetResult());
        }
        finally
        {
            engine.Dispose();
        }

        engine.Dispose(); // повторный Dispose безопасен
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.Run(scene, camera));
        Assert.ThrowsExactly<ObjectDisposedException>(engine.RequestClose);
        Assert.IsGreaterThanOrEqualTo(2L, engine.RenderedFrames);
    }

    [TestMethod]
    public void OnUpdate_exception_leaves_Run_unchanged_and_using_releases_the_engine()
    {
        var failure = new InvalidTimeZoneException("onUpdate failed");

        using (var engine = CreateEngine(nameof(OnUpdate_exception_leaves_Run_unchanged_and_using_releases_the_engine)))
        {
            var thrown = Assert.ThrowsExactly<InvalidTimeZoneException>(() => engine.Run(new Scene(), new Camera(), _ => throw failure));
            Assert.AreSame(failure, thrown);
        }
    }

    [TestMethod]
    public void Dispose_from_onUpdate_is_rejected()
    {
        using var engine = CreateEngine(nameof(Dispose_from_onUpdate_is_rejected));

        Assert.ThrowsExactly<InvalidOperationException>(() => engine.Run(new Scene(), new Camera(), _ => engine.Dispose()));
    }

    [TestMethod]
    public void Invalid_object_after_onUpdate_stops_Run_before_the_frame()
    {
        var scene = new Scene();
        var cube = PrimitiveFactory.CreateCube();
        for (int i = 0; i < 5; i++)
            AddObject(scene, cube, new ColorRgba(1f, 1f, 1f), new Vector3(2f * i, 0f, 0f));
        var camera = new Camera { Position = new Vector3(4f, 3f, 10f), Target = new Vector3(4f, 0f, 0f) };
        using var engine = CreateEngine(nameof(Invalid_object_after_onUpdate_stops_Run_before_the_frame));
        long framesBeforeChange = -1;

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => RunFrames(engine, scene, camera, frames: 5, frame =>
        {
            if (frame == 2)
            {
                framesBeforeChange = engine.RenderedFrames;
                scene.Objects[3].Transform.Scale = new Vector3(1f, 0f, 1f);
            }
        }));

        Assert.StartsWith("Scene object 3 is invalid: ", exception.Message);
        Assert.AreEqual(framesBeforeChange, engine.RenderedFrames); // кадр с неверным состоянием не показан
    }

    [TestMethod]
    public void Invalid_camera_is_reported_before_the_first_frame()
    {
        var camera = new Camera { Position = Vector3.One, Target = Vector3.One };
        using var engine = CreateEngine(nameof(Invalid_camera_is_reported_before_the_first_frame));

        Assert.ThrowsExactly<InvalidOperationException>(() => engine.Run(new Scene(), camera));
        Assert.AreEqual(0L, engine.RenderedFrames);
    }

    [TestMethod]
    public void New_engine_can_be_created_repeatedly_for_a_large_scene()
    {
        // Повторный показ — новым Engine; каждый освобождает свой GPU-кэш, шейдер, окно и контекст.
        var scene = new Scene();
        var cube = PrimitiveFactory.CreateCube();
        for (int i = 0; i < 1000; i++)
            AddObject(scene, cube, new ColorRgba(1f, 0.6f, 0.2f), new Vector3(i % 10, i / 10 % 10, -(i / 100)), new Vector3(0.8f));
        var camera = new Camera { Position = new Vector3(5f, 5f, 15f), Target = new Vector3(5f, 5f, -5f) };

        for (int cycle = 0; cycle < 10; cycle++)
        {
            using var engine = CreateEngine($"{nameof(New_engine_can_be_created_repeatedly_for_a_large_scene)} {cycle}");
            RunFrames(engine, scene, camera, frames: 3);

            Assert.IsGreaterThanOrEqualTo(3L, engine.RenderedFrames);
            Assert.AreEqual(1, engine.GpuMeshCount);
        }
    }
}
