using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Engine3D.Tests.GraphicsTestSupport;

namespace Engine3D.Tests;

/// <summary>Картинка renderer на реальном контексте OpenGL: перекрытие по глубине, GPU-кэш мешей и свет P0-10.</summary>
[TestClass]
[GraphicsTest]
public sealed class SceneRendererTests
{
    private static readonly float InverseSqrt3 = 1f / MathF.Sqrt(3f);

    [TestMethod]
    public void Nearer_object_covers_farther_one_and_cube_with_plane_is_shown()
    {
        var scene = new Scene();
        var cube = PrimitiveFactory.CreateCube();
        // Ближний зеленый куб добавлен первым: без depth test дальний красный, нарисованный позже, закрыл бы его.
        AddObject(scene, cube, new ColorRgba(0f, 1f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0.5f));
        AddObject(scene, cube, new ColorRgba(1f, 0f, 0f), new Vector3(0f, 0f, -2f), new Vector3(3f));
        AddObject(scene, PrimitiveFactory.CreatePlane(20f, 20f), new ColorRgba(0.5f, 0.5f, 0.5f), new Vector3(0f, -2f, 0f));
        // Только фоновый свет единичной яркости: пиксель равен базовому цвету материала.
        scene.Lighting.AmbientColor = Vector3.One;
        scene.Lighting.DirectionalColor = Vector3.Zero;
        var camera = new Camera { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };
        Frame? last = null;

        using (var engine = CreateEngine(nameof(Nearer_object_covers_farther_one_and_cube_with_plane_is_shown)))
        {
            engine.FrameRendered = (width, height, pixels) => last = new Frame(width, height, pixels);
            RunFrames(engine, scene, camera, frames: 2);
        }

        Assert.IsNotNull(last);
        AssertColor(0, 255, 0, last.At(camera, new Vector3(0f, 0f, 1.25f)), "near cube in front of the far one");
        AssertColor(255, 0, 0, last.At(camera, new Vector3(1.2f, 0f, -0.5f)), "visible part of the far cube");
        AssertColor(Level(0.5f), Level(0.5f), Level(0.5f), last.At(camera, new Vector3(2.5f, -2f, -1f)), "plane");
    }

    [TestMethod]
    public void Shared_mesh_has_one_GPU_mesh_until_Dispose()
    {
        var scene = new Scene();
        var cube = PrimitiveFactory.CreateCube();
        for (int i = 0; i < 1000; i++)
            AddObject(scene, cube, new ColorRgba(1f, 0.6f, 0.2f), new Vector3(i % 10 * 2f, i / 10 % 10 * 2f, i / 100 * -2f), new Vector3(0.8f));
        AddObject(scene, PrimitiveFactory.CreatePlane(24f, 24f), new ColorRgba(0.6f, 0.6f, 0.6f), new Vector3(9f, -2f, -9f));
        var camera = new Camera { Position = new Vector3(35f, 30f, 25f), Target = new Vector3(9f, 9f, -9f), FarPlane = 200f };
        var countBeforeFrame = new int[4];
        var engine = CreateEngine(nameof(Shared_mesh_has_one_GPU_mesh_until_Dispose));

        RunFrames(engine, scene, camera, frames: 4, frame =>
        {
            countBeforeFrame[frame] = engine.GpuMeshCount;
            if (frame == 1)
                AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f), Vector3.Zero); // те же данные, другой MeshData
            if (frame == 2)
            {
                foreach (var item in scene.Objects.ToArray())
                    scene.Remove(item);
            }
        });
        int countAfterRun = engine.GpuMeshCount;
        engine.Dispose();

        Assert.AreEqual(2, countBeforeFrame[1]); // 1001 объект на двух MeshData
        Assert.AreEqual(3, countBeforeFrame[2]); // новый экземпляр MeshData — отдельное GPU-представление
        Assert.AreEqual(3, countBeforeFrame[3]); // объекты удалены, кэш хранится до Dispose
        Assert.AreEqual(3, countAfterRun);
        Assert.AreEqual(0, engine.GpuMeshCount);
    }

    [TestMethod]
    public void Light_direction_changes_brightness_of_cube_faces_and_plane()
    {
        var scene = new Scene();
        AddObject(scene, PrimitiveFactory.CreateCube(), new ColorRgba(1f, 1f, 1f), Vector3.Zero);
        AddObject(scene, PrimitiveFactory.CreatePlane(4f, 4f), new ColorRgba(1f, 1f, 1f), new Vector3(0f, -0.6f, 0f));
        var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero };
        // Яркость грани = ambient 0.2 + directional * max(dot(n, −direction), 0); у плоскости нормаль +Y, как у грани +Y.
        var cases = new (Vector3 Direction, Vector3 Color, float PlusX, float PlusY, float PlusZ)[]
        {
            (new Vector3(-1f, 0f, 0f), Vector3.One, 1.2f, 0.2f, 0.2f),
            (new Vector3(0f, -1f, 0f), Vector3.One, 0.2f, 1.2f, 0.2f),
            (new Vector3(0f, 0f, -5f), Vector3.One, 0.2f, 0.2f, 1.2f), // направление не обязано быть единичным
            (new Vector3(-1f, -1f, -1f), Vector3.One, 0.2f + InverseSqrt3, 0.2f + InverseSqrt3, 0.2f + InverseSqrt3),
            (new Vector3(-1f, 0f, 0f), Vector3.Zero, 0.2f, 0.2f, 0.2f), // направленный свет выключен
        };
        var frames = new Frame?[cases.Length];
        int current = -1;

        using (var engine = CreateEngine(nameof(Light_direction_changes_brightness_of_cube_faces_and_plane)))
        {
            // Перерисовка по запросу системы показывает последнее состояние — тот же случай, что и обычный кадр.
            engine.FrameRendered = (width, height, pixels) =>
            {
                if (current >= 0)
                    frames[current] = new Frame(width, height, pixels);
            };
            RunFrames(engine, scene, camera, cases.Length, frame =>
            {
                current = frame;
                scene.Lighting.Direction = cases[frame].Direction;
                scene.Lighting.DirectionalColor = cases[frame].Color;
            });
        }

        for (int i = 0; i < cases.Length; i++)
        {
            var frame = frames[i];
            var expected = cases[i];
            Assert.IsNotNull(frame);
            string name = $"light {expected.Direction} color {expected.Color}";
            AssertColor(Level(expected.PlusX), Level(expected.PlusX), Level(expected.PlusX), frame.At(camera, new Vector3(0.5f, 0f, 0f)), $"{name}, face +X");
            AssertColor(Level(expected.PlusY), Level(expected.PlusY), Level(expected.PlusY), frame.At(camera, new Vector3(0f, 0.5f, 0f)), $"{name}, face +Y");
            AssertColor(Level(expected.PlusZ), Level(expected.PlusZ), Level(expected.PlusZ), frame.At(camera, new Vector3(0f, 0f, 0.5f)), $"{name}, face +Z");
            AssertColor(Level(expected.PlusY), Level(expected.PlusY), Level(expected.PlusY), frame.At(camera, new Vector3(1.5f, -0.6f, 1.5f)), $"{name}, plane");
        }
    }

    [TestMethod]
    public void Normals_use_inverse_transpose_under_non_uniform_scale()
    {
        // Квадрат в плоскости XY с наклонной нормалью (1, 1, 0)/√2, растянутый вдоль X в 4 раза, свет вдоль −X.
        // Верная нормаль ∝ diag(1/4, 1, 1)·n = (1/4, 1, 0): x = 0.25/√1.0625 ≈ 0.2425, уровень ≈ 113.
        // Умножение нормали на model дало бы (4, 1, 0)/√17, x ≈ 0.970 и уровень 255.
        var normal = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var quad = new MeshData(
            [
                new Vertex(new Vector3(-0.5f, -0.5f, 0f), new Vector2(0f, 0f), normal),
                new Vertex(new Vector3(0.5f, -0.5f, 0f), new Vector2(1f, 0f), normal),
                new Vertex(new Vector3(0.5f, 0.5f, 0f), new Vector2(1f, 1f), normal),
                new Vertex(new Vector3(-0.5f, 0.5f, 0f), new Vector2(0f, 1f), normal),
            ],
            [0, 1, 2, 0, 2, 3]);
        var scene = new Scene();
        AddObject(scene, quad, new ColorRgba(1f, 1f, 1f), Vector3.Zero, new Vector3(4f, 1f, 1f));
        scene.Lighting.Direction = new Vector3(-1f, 0f, 0f);
        var camera = new Camera { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };
        Frame? last = null;

        using (var engine = CreateEngine(nameof(Normals_use_inverse_transpose_under_non_uniform_scale)))
        {
            engine.FrameRendered = (width, height, pixels) => last = new Frame(width, height, pixels);
            RunFrames(engine, scene, camera, frames: 2);
        }

        int expected = Level(0.2f + 0.25f / MathF.Sqrt(1.0625f));
        Assert.IsNotNull(last);
        AssertColor(expected, expected, expected, last.At(camera, Vector3.Zero), "stretched quad");
    }
}
