using System.Numerics;
using System.Text.Json;
using Engine3D.Core;
using Engine3D.Demo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class PerformanceTests
{
    [TestMethod]
    public void Workload_is_1000_shared_lit_textured_cubes_inside_camera_frustum()
    {
        var texture = new TextureData(1, 1, [255, 255, 255, 255]);
        var snapshot = PerformanceScene.Create(texture);
        var objects = snapshot.Scene.Objects;
        Assert.HasCount(1000, objects);
        Assert.AreEqual(12000, objects.Sum(item => item.Mesh.TriangleCount));
        var positions = new HashSet<Vector3>();
        var viewProjection = snapshot.Camera.GetViewMatrix() * snapshot.Camera.GetProjectionMatrix(1280f / 720);
        foreach (var item in objects)
        {
            Assert.AreSame(objects[0].Mesh, item.Mesh);
            Assert.AreSame(objects[0].Material, item.Material);
            Assert.AreSame(texture, item.Material.Texture);
            Assert.IsTrue(positions.Add(item.Transform.Position));
            foreach (var vertex in item.Mesh.Vertices)
            {
                var clip = Vector4.Transform(new Vector4(vertex.Position, 1), item.Transform.GetModelMatrix() * viewProjection);
                // Геометрический тест объёма Core (глубина [0,w]); небольшой допуск на float-матрицы.
                Assert.IsTrue(clip.W > 0 && MathF.Abs(clip.X) <= clip.W * 1.00001f
                    && MathF.Abs(clip.Y) <= clip.W * 1.00001f && clip.Z >= 0 && clip.Z <= clip.W * 1.00001f);
            }
        }
        Assert.AreEqual(new Vector3(0.25f), snapshot.Scene.Lighting.AmbientColor);
        Assert.AreEqual(new Vector3(0.75f), snapshot.Scene.Lighting.DirectionalColor);
        snapshot.Scene.Lighting.Validate();
    }

    [TestMethod]
    public void Performance_defaults_and_explicit_settings_are_reproducible()
    {
        var defaults = PerformanceOptions.Parse([]);
        Assert.AreEqual(1280, defaults.Width);
        Assert.AreEqual(720, defaults.Height);
        Assert.IsFalse(defaults.VSync);
        Assert.AreEqual(10d, defaults.WarmupSeconds);
        Assert.AreEqual(60d, defaults.DurationSeconds);
        var custom = PerformanceOptions.Parse(["--size", "960x540", "--warmup", "12.5", "--duration", "90", "--vsync", "on", "--hardware", "test hardware", "--output", "my run"]);
        Assert.AreEqual(960, custom.Width);
        Assert.AreEqual(540, custom.Height);
        Assert.AreEqual(12.5, custom.WarmupSeconds);
        Assert.AreEqual(90d, custom.DurationSeconds);
        Assert.IsTrue(custom.VSync);
        Assert.AreEqual("test hardware", custom.Hardware);
        Assert.AreEqual(Path.GetFullPath("my run"), custom.OutputDirectory);
        Assert.IsTrue(PerformanceOptions.Parse(["--help"]).Help);
    }

    [TestMethod]
    [DataRow("--warmup", "9.9")]
    [DataRow("--warmup", "NaN")]
    [DataRow("--duration", "59")]
    [DataRow("--duration", "Infinity")]
    [DataRow("--duration", "1e99")]
    [DataRow("--duration", "1,5")]
    [DataRow("--vsync", "maybe")]
    [DataRow("--size", "0x720")]
    [DataRow("--size", "1280")]
    [DataRow("--frames", "10")]
    [DataRow("--load", "scene.json")]
    public void Invalid_performance_options_fail_before_creating_window(string option, string value)
        => Assert.ThrowsExactly<ArgumentException>(() => PerformanceOptions.Parse([option, value]));

    [TestMethod]
    public void Missing_and_duplicate_performance_options_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PerformanceOptions.Parse(["--duration"]));
        Assert.ThrowsExactly<ArgumentException>(() => PerformanceOptions.Parse(["--duration", "60", "--duration", "61"]));
    }

    [TestMethod]
    public void Measurement_excludes_warmup_and_counts_swaps_not_callbacks_with_actual_duration()
    {
        var measurement = new PerformanceMeasurement(10, 60);
        int memoryReads = 0;
        MemorySample Read() { memoryReads++; return new MemorySample(100, 50); }
        Assert.IsFalse(measurement.Observe(0, 0, Read));
        Assert.IsFalse(measurement.Observe(9.9, 500, Read));
        Assert.AreEqual(0, memoryReads);
        Assert.IsFalse(measurement.Observe(10.25, 700, Read)); // Опоздание первого callback после прогрева.
        Assert.IsFalse(measurement.Observe(10.5, 703, Read)); // Refresh: больше одного кадра между callbacks.
        Assert.IsFalse(measurement.Observe(11.5, 703, Read)); // Callback без показанных кадров.
        Assert.IsFalse(measurement.Observe(69, 3500, Read));
        Assert.IsTrue(measurement.Observe(70.5, 3700, Read));
        measurement.Finish(70.75, 3700, Read); // Время после возврата Run, не заданные ровно 60 с.
        Assert.AreEqual(700L, measurement.StartFrames);
        Assert.AreEqual(3700L, measurement.EndFrames);
        Assert.AreEqual(60.5, measurement.MeasuredSeconds);
        Assert.AreEqual(3000L, measurement.MeasuredFrames);
        Assert.AreEqual(3000d / 60.5, measurement.Fps);
        Assert.AreEqual(60.5 * 1000 / 3000, measurement.MeanFrameMs);
        Assert.AreEqual("complete", measurement.Status);
        Assert.AreEqual(3000L, measurement.Samples.Sum(sample => sample.Frames));
        Assert.AreEqual(60.5, measurement.Samples.Sum(sample => sample.Seconds), 1e-10);
        Assert.HasCount(memoryReads - 1, measurement.Samples);
    }

    [TestMethod]
    public void Early_close_and_close_before_warmup_never_report_complete()
    {
        var early = new PerformanceMeasurement(10, 60);
        early.Observe(10, 100, Memory);
        early.Finish(20, 200, Memory);
        Assert.AreEqual("incomplete", early.Status);
        Assert.AreEqual(10d, early.Fps);
        var cancelled = new PerformanceMeasurement(10, 60);
        cancelled.Observe(1, 0, Memory);
        cancelled.Finish(9, 50, Memory);
        Assert.AreEqual("cancelled-before-measurement", cancelled.Status);
        Assert.IsNull(cancelled.Fps);
        Assert.IsNull(cancelled.StartMemory);
        Assert.AreEqual(0L, cancelled.MeasuredFrames);
        Assert.HasCount(0, cancelled.Samples);
    }

    [TestMethod]
    public void Zero_frames_and_zero_duration_have_no_infinite_frame_time_or_fps()
    {
        var measurement = new PerformanceMeasurement(10, 60);
        measurement.Observe(10, 100, Memory);
        measurement.Observe(11, 100, Memory);
        measurement.Finish(12, 100, Memory);
        Assert.AreEqual(0d, measurement.Fps);
        Assert.IsNull(measurement.MeanFrameMs);
        Assert.IsTrue(measurement.Samples.All(s => s.MeanFrameMs is null && s.Fps == 0));
        var empty = new PerformanceMeasurement(10, 60);
        empty.Observe(10, 0, Memory);
        empty.Finish(10, 0, Memory);
        Assert.IsNull(empty.Fps);
        Assert.IsNull(empty.MeanFrameMs);
    }

    [TestMethod]
    public void Invalid_window_and_nonmonotonic_observations_are_not_accepted()
    {
        var measurement = new PerformanceMeasurement(10, 60);
        measurement.Observe(10, 100, Memory);
        Assert.ThrowsExactly<InvalidOperationException>(() => measurement.Observe(9, 101, Memory));
        Assert.ThrowsExactly<InvalidOperationException>(() => measurement.Observe(11, 99, Memory));
        measurement.Invalidate("resize");
        Assert.IsTrue(measurement.Observe(12, 101, Memory));
        measurement.Finish(12.1, 101, Memory);
        Assert.AreEqual("invalid", measurement.Status);
        Assert.AreEqual("resize", measurement.InvalidReason);
        Assert.ThrowsExactly<InvalidOperationException>(() => measurement.Finish(13, 102, Memory));
    }

    [TestMethod]
    public void Report_preserves_boundaries_and_samples_and_rejects_overwriting_existing_directory()
    {
        string directory = TemporaryDirectory.Create("engine3d-performance-tests-");
        try
        {
            var options = PerformanceOptions.Parse(["--output", directory, "--hardware", "test fixture"]);
            var measurement = new PerformanceMeasurement(10, 60);
            measurement.Observe(10, 100, Memory);
            measurement.Observe(70.5, 700, Memory);
            measurement.Finish(70.6, 700, Memory);
            var snapshot = PerformanceScene.Create(new TextureData(1, 1, [255, 255, 255, 255]));
            PerformanceRun.WriteReport(options, snapshot, measurement, (2560, 1440), (0, 0, 1280, 720), DateTimeOffset.UtcNow);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "measurement.json")));
            var root = document.RootElement;
            Assert.AreEqual("complete", root.GetProperty("Status").GetString());
            Assert.AreEqual(100L, root.GetProperty("StartFrames").GetInt64());
            Assert.AreEqual(700L, root.GetProperty("EndFrames").GetInt64());
            Assert.AreEqual(2560, root.GetProperty("Window").GetProperty("FramebufferWidth").GetInt32());
            Assert.AreEqual(12000, root.GetProperty("Workload").GetProperty("Triangles").GetInt32());
            Assert.AreEqual(1, root.GetProperty("Workload").GetProperty("UniqueMeshes").GetInt32());
            Assert.AreEqual(2, root.GetProperty("Samples").GetArrayLength());
            string report = File.ReadAllText(Path.Combine(directory, "report.md"));
            StringAssert.Contains(report, "не точный frame time");
            StringAssert.Contains(report, "заполнить оператору");
            Assert.ThrowsExactly<IOException>(() => PerformanceRun.Execute(options)); // До Engine.Create.
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MemorySample Memory() => new(1000, 100);
}
