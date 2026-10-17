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
public sealed partial class CpuScenarioTests
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

    /// <summary>Сцена B из api.md §5: кубическая сетка ScenarioBGridSize³ объектов с шагом ScenarioBGridStep.</summary>
    private const int ScenarioBGridSize = 10;
    private const float ScenarioBGridStep = 2f;
    private const int ScenarioBObjectCount = ScenarioBGridSize * ScenarioBGridSize * ScenarioBGridSize;

    /// <summary>Удаляется каждый второй объект сцены B, начиная с индекса 1.</summary>
    private const int ScenarioBRemovalStride = 2;

    // Общие MeshData/MaterialData через шов Prepare не наблюдаемы (FramePreparation отдаёт только матрицы),
    // а переиспользование GPU-ресурсов по идентичности объектов покрыто GpuResourceCacheTests.

    [TestMethod]
    public void Scenario_B_prepares_1000_shared_objects_inside_the_camera_view()
    {
        var scene = CreateScenarioBScene(PrimitiveFactory.CreateCube(), new MaterialData());
        var frame = new FramePreparation();

        frame.Prepare(scene, CreateScenarioBCamera(), FramebufferWidth, FramebufferHeight);

        Assert.AreEqual(ScenarioBObjectCount, frame.Models.Length);
        for (int i = 0; i < ScenarioBObjectCount; i++)
        {
            // Центр куба — начало локальных координат; model без поворота и масштаба переносит его в позицию сетки.
            AssertInsideClipVolume(ToClip(Vector3.Zero, frame.Models[i], frame), $"object {i}");
            Assert.AreEqual(Matrix4x4.CreateTranslation(ScenarioBPosition(i)), frame.Models[i], $"model {i}");
        }
    }

    [TestMethod]
    public void Scenario_B_repeated_prepare_follows_removal_of_every_second_object()
    {
        var scene = CreateScenarioBScene(PrimitiveFactory.CreateCube(), new MaterialData());
        var camera = CreateScenarioBCamera();
        var frame = new FramePreparation();
        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        var removed = new List<SceneObject>();
        for (int i = 1; i < ScenarioBObjectCount; i += ScenarioBRemovalStride)
            removed.Add(scene.Objects[i]);
        foreach (var item in removed)
            Assert.IsTrue(scene.Remove(item));

        frame.Prepare(scene, camera, FramebufferWidth, FramebufferHeight);

        // Остались объекты с чётными исходными индексами, в исходном порядке.
        int remaining = ScenarioBObjectCount / ScenarioBRemovalStride;
        Assert.AreEqual(remaining, frame.Models.Length);
        for (int k = 0; k < remaining; k++)
        {
            Assert.AreEqual(Matrix4x4.CreateTranslation(ScenarioBPosition(ScenarioBRemovalStride * k)), frame.Models[k], $"model {k}");
            AssertInsideClipVolume(ToClip(Vector3.Zero, frame.Models[k], frame), $"object {k}");
        }
    }

    /// <summary>Сцена B из api.md §5: сетка ScenarioBGridSize³ с шагом ScenarioBGridStep, один MeshData и один MaterialData.</summary>
    private static Scene CreateScenarioBScene(MeshData mesh, MaterialData material)
    {
        var scene = new Scene();
        for (int i = 0; i < ScenarioBObjectCount; i++)
        {
            var item = new SceneObject(mesh, material);
            item.Transform.Position = ScenarioBPosition(i);
            scene.Add(item);
        }

        return scene;
    }

    /// <summary>Позиция i-го объекта сетки: индекс раскладывается по X, затем Y, затем Z.</summary>
    private static Vector3 ScenarioBPosition(int i) => new Vector3(
        i % ScenarioBGridSize,
        i / ScenarioBGridSize % ScenarioBGridSize,
        i / (ScenarioBGridSize * ScenarioBGridSize)) * ScenarioBGridStep;

    private static Camera CreateScenarioBCamera() =>
        new() { Position = new Vector3(35f, 30f, 40f), Target = new Vector3(9f, 9f, 9f), FarPlane = 200f };

    /// <summary>
    /// Clip-координаты локальной точки объекта: row-vector, MVP = model · view · projection. Проекция берётся из
    /// Prepare уже в соглашении OpenGL, поэтому повторная коррекция глубины здесь не выполняется.
    /// </summary>
    private static Vector4 ToClip(Vector3 local, Matrix4x4 model, FramePreparation frame) =>
        Vector4.Transform(new Vector4(local, 1f), model * frame.View * frame.Projection);

    /// <summary>Точка внутри clip-объёма OpenGL: w &gt; 0 и −w ≤ x, y, z ≤ w с допуском <see cref="ClipVolumeRelativeTolerance"/>.</summary>
    private static void AssertInsideClipVolume(Vector4 clip, string context)
    {
        Assert.IsGreaterThan(0f, clip.W, $"{context}: w = {clip.W} (point behind the camera)");
        float limit = clip.W * (1f + ClipVolumeRelativeTolerance);
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.X), $"{context}: x = {clip.X}, w = {clip.W}");
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.Y), $"{context}: y = {clip.Y}, w = {clip.W}");
        Assert.IsLessThanOrEqualTo(limit, MathF.Abs(clip.Z), $"{context}: z = {clip.Z}, w = {clip.W}");
    }
}
