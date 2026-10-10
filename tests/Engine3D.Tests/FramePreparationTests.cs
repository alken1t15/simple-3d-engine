using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class FramePreparationTests
{
    private static readonly MeshData Cube = PrimitiveFactory.CreateCube();

    private static Camera ValidCamera() => new() { Position = new Vector3(2f, 3f, 6f), Target = new Vector3(0f, 0.5f, 0f) };

    private static Scene SceneWith(int count)
    {
        var scene = new Scene();
        for (int i = 0; i < count; i++)
        {
            var item = new SceneObject(Cube, new MaterialData());
            item.Transform.Position = new Vector3(i, -i, 0.5f * i);
            item.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f * i);
            item.Transform.Scale = new Vector3(1f + i, 1f, 0.5f);
            scene.Add(item);
        }

        return scene;
    }

    [TestMethod]
    public void Uses_Core_matrices_and_framebuffer_aspect_ratio()
    {
        var scene = SceneWith(3);
        scene.Lighting.Direction = new Vector3(0f, -4f, 3f);
        var camera = ValidCamera();
        var frame = new FramePreparation();

        frame.Prepare(scene, camera, 800, 400);

        Assert.AreEqual(camera.GetViewMatrix(), frame.View);
        Assert.AreEqual(GlProjection.ToOpenGl(camera.GetProjectionMatrix(2f)), frame.Projection);
        Assert.AreEqual(2f, frame.Projection.M22 / frame.Projection.M11, 1e-6f); // ширина/высота кадра
        Assert.AreEqual(scene.Lighting.GetNormalizedDirection(), frame.LightDirection);
        Assert.AreEqual(3, frame.Models.Length);
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(scene.Objects[i].Transform.GetModelMatrix(), frame.Models[i]);
    }

    [TestMethod]
    public void Model_buffer_follows_the_current_object_count()
    {
        var frame = new FramePreparation();
        var large = SceneWith(200);
        var small = SceneWith(2);

        frame.Prepare(large, ValidCamera(), 640, 480);
        Assert.AreEqual(200, frame.Models.Length);
        Assert.AreEqual(large.Objects[199].Transform.GetModelMatrix(), frame.Models[199]);

        frame.Prepare(small, ValidCamera(), 640, 480);
        Assert.AreEqual(2, frame.Models.Length);
        Assert.AreEqual(small.Objects[1].Transform.GetModelMatrix(), frame.Models[1]);

        frame.Prepare(new Scene(), ValidCamera(), 640, 480);
        Assert.AreEqual(0, frame.Models.Length);
    }

    [TestMethod]
    [DataRow("zero scale")]
    [DataRow("NaN position")]
    [DataRow("zero rotation")]
    [DataRow("base color above one")]
    [DataRow("base color NaN")]
    public void Invalid_object_is_reported_with_its_index(string kind)
    {
        var scene = SceneWith(4);
        var item = scene.Objects[2];
        switch (kind)
        {
            case "zero scale": item.Transform.Scale = new Vector3(1f, 0f, 1f); break;
            case "NaN position": item.Transform.Position = new Vector3(float.NaN, 0f, 0f); break;
            case "zero rotation": item.Transform.Rotation = default; break;
            case "base color above one": item.Material.BaseColor = new ColorRgba(1.5f, 0f, 0f); break;
            case "base color NaN": item.Material.BaseColor = new ColorRgba(0f, float.NaN, 0f); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => new FramePreparation().Prepare(scene, ValidCamera(), 640, 480));

        Assert.StartsWith("Scene object 2 is invalid: ", exception.Message);
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
    }

    [TestMethod]
    [DataRow("camera position equals target")]
    [DataRow("camera far below near")]
    [DataRow("zero light direction")]
    [DataRow("ambient above one")]
    public void Invalid_camera_or_light_is_rejected(string kind)
    {
        var scene = SceneWith(1);
        var camera = ValidCamera();
        switch (kind)
        {
            case "camera position equals target": camera.Target = camera.Position; break;
            case "camera far below near": camera.FarPlane = 0.01f; break;
            case "zero light direction": scene.Lighting.Direction = Vector3.Zero; break;
            case "ambient above one": scene.Lighting.AmbientColor = new Vector3(2f); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => new FramePreparation().Prepare(scene, camera, 640, 480));
    }
}
