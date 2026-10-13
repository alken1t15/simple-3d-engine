using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

/// <summary>
/// CPU-сценарии: сцена строится публичным Core API, результат проверяется через подготовку кадра
/// (<see cref="FramePreparation.Prepare"/>) без окна и GL-контекста.
/// </summary>
[TestClass]
public sealed class CpuScenarioTests
{
    /// <summary>
    /// Относительный допуск clip-объёма: |x|, |y|, |z| ≤ w · (1 + допуск). Координаты сцен — единицы и десятки,
    /// ошибка округления float при перемножении матриц 4×4 — порядка 1e-6 относительно w; 1e-5 даёт запас,
    /// но не пропускает точки, вышедшие за frustum на заметную долю.
    /// </summary>
    private const float ClipVolumeRelativeTolerance = 1e-5f;

    /// <summary>Размер кадра по умолчанию из <see cref="EngineOptions"/> (1280×720).</summary>
    private const int FramebufferWidth = 1280;
    private const int FramebufferHeight = 720;

    [TestMethod]
    public void Scenario_B_prepares_1000_shared_objects_inside_the_camera_view()
    {
        var mesh = PrimitiveFactory.CreateCube();
        var material = new MaterialData();
        var scene = CreateScenarioBScene(mesh, material);
        var frame = new FramePreparation();

        frame.Prepare(scene, CreateScenarioBCamera(), FramebufferWidth, FramebufferHeight);

        Assert.HasCount(1000, scene.Objects);
        Assert.AreEqual(1000, frame.Models.Length);
        foreach (var item in scene.Objects)
        {
            Assert.AreSame(mesh, item.Mesh);
            Assert.AreSame(material, item.Material);
        }

        for (int i = 0; i < 1000; i++)
        {
            // Центр куба — начало локальных координат; model без поворота и масштаба переносит его в позицию сетки.
            AssertInsideClipVolume(ToClip(Vector3.Zero, frame.Models[i], frame), $"object {i}");
            Assert.AreEqual(Matrix4x4.CreateTranslation(ScenarioBPosition(i)), frame.Models[i]);
        }
    }

    [TestMethod]
    public void Scenario_B_repeated_prepare_follows_removal_of_every_second_object()
    {
        var mesh = PrimitiveFactory.CreateCube();
        int vertexCount = mesh.VertexCount, indexCount = mesh.IndexCount;
        var material = new MaterialData();
        var scene = CreateScenarioBScene(mesh, material);
        var camera = CreateScenarioBCamera();
        var frame = new FramePreparation();
        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        var removed = new List<SceneObject>();
        for (int i = 1; i < 1000; i += 2)
            removed.Add(scene.Objects[i]);
        foreach (var item in removed)
            Assert.IsTrue(scene.Remove(item));

        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        // Остались объекты с чётными исходными индексами, в исходном порядке.
        Assert.AreEqual(500, frame.Models.Length);
        for (int k = 0; k < 500; k++)
        {
            Assert.AreEqual(Matrix4x4.CreateTranslation(ScenarioBPosition(2 * k)), frame.Models[k], $"model {k}");
            Assert.AreSame(mesh, scene.Objects[k].Mesh);
            AssertInsideClipVolume(ToClip(Vector3.Zero, frame.Models[k], frame), $"object {k}");
        }

        // Удаление объектов не затрагивает общий неизменяемый mesh.
        Assert.AreEqual(vertexCount, mesh.VertexCount);
        Assert.AreEqual(indexCount, mesh.IndexCount);
        Assert.AreEqual(indexCount / 3, mesh.TriangleCount);
    }

    /// <summary>Сцена B из api.md §5: сетка 10×10×10 с шагом 2, один MeshData и один MaterialData.</summary>
    private static Scene CreateScenarioBScene(MeshData mesh, MaterialData material)
    {
        var scene = new Scene();
        for (int i = 0; i < 1000; i++)
        {
            var item = new SceneObject(mesh, material);
            item.Transform.Position = ScenarioBPosition(i);
            scene.Add(item);
        }

        return scene;
    }

    private static Vector3 ScenarioBPosition(int i) => new((i % 10) * 2f, ((i / 10) % 10) * 2f, (i / 100) * 2f);

    private static Camera CreateScenarioBCamera() =>
        new() { Position = new Vector3(35f, 30f, 40f), Target = new Vector3(9f, 9f, 9f), FarPlane = 200f };

    /// <summary>
    /// Clip-координаты локальной точки объекта: row-vector, MVP = model · view · projection. Проекция берётся из
    /// Prepare уже в соглашении OpenGL, поэтому повторная коррекция глубины здесь не выполняется.
    /// </summary>
    internal static Vector4 ToClip(Vector3 local, Matrix4x4 model, FramePreparation frame) =>
        Vector4.Transform(new Vector4(local, 1f), model * frame.View * frame.Projection);

    /// <summary>Точка внутри clip-объёма OpenGL: w &gt; 0 и −w ≤ x, y, z ≤ w с допуском <see cref="ClipVolumeRelativeTolerance"/>.</summary>
    internal static void AssertInsideClipVolume(Vector4 clip, string context)
    {
        Assert.IsGreaterThan(0f, clip.W, $"{context}: w = {clip.W} (точка за камерой)");
        float limit = clip.W * (1f + ClipVolumeRelativeTolerance);
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.X), $"{context}: x = {clip.X}, w = {clip.W}");
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.Y), $"{context}: y = {clip.Y}, w = {clip.W}");
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.Z), $"{context}: z = {clip.Z}, w = {clip.W}");
    }
}
