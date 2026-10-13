using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

public sealed partial class CpuScenarioTests
{
    /// <summary>
    /// Абсолютный допуск совпадения NDC x, y двух точек на одном луче камеры. NDC x, y здесь по модулю ≤ 1, ошибка
    /// округления float при MVP и делении на w — порядка 1e-7…1e-6; 1e-5 даёт запас, но на порядки меньше
    /// пикселя кадра 1280×720 (2 / 720 ≈ 2.8e-3), поэтому разные пиксели за совпадение не сойдут.
    /// </summary>
    private const float SameRayNdcTolerance = 1e-5f;

    /// <summary>
    /// Минимальный зазор NDC-глубины, при котором куб считается ближе пола. При near = 0.1, far = 100 ожидаемый
    /// зазор между точками на расстояниях ≈ 6.3 и ≈ 8.9 вдоль взгляда около 1e-2; 1e-5 отсекает шум округления,
    /// но не равенство глубин.
    /// </summary>
    private const float DepthSeparationTolerance = 1e-5f;

    /// <summary>
    /// Абсолютный допуск NDC-глубины относительно формулы перспективы OpenGL. Глубины точек сцены ≈ 0.97, ошибка
    /// округления float в MVP — порядка 1e-6; 1e-4 даёт запас. Двойная коррекция (z → 4z − 3w) сдвигает эти глубины
    /// примерно на 3e-2, отсутствие коррекции (глубина [0, 1]) — примерно на 1.7e-2, оба случая заметно больше допуска.
    /// </summary>
    private const float GlDepthTolerance = 1e-4f;

    /// <summary>Индексы объектов сцены A в порядке добавления (Models идут в порядке Scene.Objects).</summary>
    private const int ScenarioACubeIndex = 0;
    private const int ScenarioAFloorIndex = 1;

    /// <summary>Центр нижней грани куба размера 1 в его локальных координатах.</summary>
    private static readonly Vector3 CubeBottomCenter = new(0f, -0.5f, 0f);

    [TestMethod]
    public void Scenario_A_cube_vertices_are_inside_the_camera_view()
    {
        var (scene, cube, _, camera) = CreateScenarioA();
        var frame = new FramePreparation();

        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        var model = frame.Models[ScenarioACubeIndex];
        var vertices = cube.Mesh.Vertices;
        for (int i = 0; i < vertices.Length; i++)
            AssertInsideClipVolume(ToClip(vertices[i].Position, model, frame), $"cube vertex {i}");
    }

    [TestMethod]
    public void Scenario_A_cube_is_in_front_of_the_floor_on_the_same_camera_ray()
    {
        var (scene, cube, floor, camera) = CreateScenarioA();

        // Точка пола P — пересечение луча камера → центр нижней грани куба с плоскостью пола (y = −1.5).
        // Луч через центр куба не подходит: пересекает плоскость за краем пола 6×6.
        var cubeWorld = cube.Transform.Position + CubeBottomCenter;
        var direction = cubeWorld - camera.Position;
        float t = (floor.Transform.Position.Y - camera.Position.Y) / direction.Y;
        var floorWorld = camera.Position + t * direction;
        Assert.IsGreaterThan(1f, t, "the floor must lie behind the cube on the camera ray");

        // P лежит в пределах фактической сетки пола, а не только бесконечной плоскости.
        var floorLocal = floorWorld - floor.Transform.Position;
        AssertWithinMeshXz(floor.Mesh, floorLocal);

        var frame = new FramePreparation();
        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        var cubeNdc = ToNdc(ToClip(CubeBottomCenter, frame.Models[ScenarioACubeIndex], frame));
        var floorNdc = ToNdc(ToClip(floorLocal, frame.Models[ScenarioAFloorIndex], frame));

        // Обе точки внутри кадра и на одном пикселе; куб ближе, значит перекрывает пол в depth-тесте GL_LESS.
        Assert.IsLessThanOrEqualTo(1f, MathF.Abs(cubeNdc.Z), $"cube depth {cubeNdc.Z} outside [-1, 1]");
        Assert.IsLessThanOrEqualTo(1f, MathF.Abs(floorNdc.Z), $"floor depth {floorNdc.Z} outside [-1, 1]");
        Assert.AreEqual(floorNdc.X, cubeNdc.X, SameRayNdcTolerance, "NDC x");
        Assert.AreEqual(floorNdc.Y, cubeNdc.Y, SameRayNdcTolerance, "NDC y");
        // Глубина обеих точек совпадает с классической перспективой OpenGL (glFrustum): коррекция [0, 1] → [−1, 1]
        // выполнена ровно один раз. Порядок глубин коррекция сохраняет, поэтому без этой проверки двойная
        // коррекция осталась бы незамеченной.
        Assert.AreEqual(OpenGlNdcDepth(camera, cubeWorld), cubeNdc.Z, GlDepthTolerance, "cube NDC depth");
        Assert.AreEqual(OpenGlNdcDepth(camera, floorWorld), floorNdc.Z, GlDepthTolerance, "floor NDC depth");
        Assert.IsLessThan(floorNdc.Z - DepthSeparationTolerance, cubeNdc.Z,
            $"cube depth {cubeNdc.Z} must be less than floor depth {floorNdc.Z}");
    }

    /// <summary>Сцена A из api.md §4 в начальный момент (угол поворота куба 0).</summary>
    private static (Scene Scene, SceneObject Cube, SceneObject Floor, Camera Camera) CreateScenarioA()
    {
        var scene = new Scene();
        var cube = new SceneObject(PrimitiveFactory.CreateCube(), new MaterialData());
        cube.Transform.Position = new Vector3(0f, 0f, 0f);
        scene.Add(cube);

        var floor = new SceneObject(PrimitiveFactory.CreatePlane(6f, 6f), new MaterialData());
        floor.Transform.Position = new Vector3(0f, -1.5f, 0f);
        scene.Add(floor);

        var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero };
        return (scene, cube, floor, camera);
    }

    /// <summary>
    /// NDC-глубина точки по формуле перспективной проекции OpenGL: (f + n) / (f − n) − 2fn / ((f − n) · d),
    /// где d — расстояние до точки вдоль направления взгляда. Near-плоскость даёт −1, far-плоскость — +1.
    /// </summary>
    private static float OpenGlNdcDepth(Camera camera, Vector3 world)
    {
        float n = camera.NearPlane, f = camera.FarPlane;
        float d = Vector3.Dot(world - camera.Position, Vector3.Normalize(camera.Target - camera.Position));
        return (f + n) / (f - n) - 2f * f * n / ((f - n) * d);
    }

    private static Vector3 ToNdc(Vector4 clip)
    {
        Assert.IsGreaterThan(0f, clip.W, $"w = {clip.W} (point behind the camera)");
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    /// <summary>Локальная точка лежит в прямоугольнике XZ, охватывающем вершины сетки.</summary>
    private static void AssertWithinMeshXz(MeshData mesh, Vector3 local)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var vertex in mesh.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        Assert.IsTrue(local.X >= min.X && local.X <= max.X, $"P.x = {local.X} outside [{min.X}, {max.X}]");
        Assert.IsTrue(local.Z >= min.Z && local.Z <= max.Z, $"P.z = {local.Z} outside [{min.Z}, {max.Z}]");
    }
}
