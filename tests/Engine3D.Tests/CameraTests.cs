using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class CameraTests
{
    private const float Tolerance = 1e-5f;

    private static Camera ValidCamera() => new() { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero };

    [TestMethod]
    public void Defaults_match_contract()
    {
        var camera = new Camera();

        Assert.AreEqual(Vector3.Zero, camera.Position);
        Assert.AreEqual(-Vector3.UnitZ, camera.Target);
        Assert.AreEqual(Vector3.UnitY, camera.Up);
        Assert.AreEqual(60f, camera.FieldOfViewDegrees);
        Assert.AreEqual(0.1f, camera.NearPlane);
        Assert.AreEqual(100f, camera.FarPlane);
    }

    [TestMethod]
    public void Default_camera_is_valid_and_looks_along_negative_z()
    {
        var camera = new Camera();

        camera.Validate();
        AssertClose(new Vector3(0f, 0f, -1f), Vector3.Transform(-Vector3.UnitZ, camera.GetViewMatrix()));
    }

    [TestMethod]
    public void View_matrix_looks_along_negative_z()
    {
        var view = ValidCamera().GetViewMatrix();

        AssertClose(new Vector3(0f, 0f, -5f), Vector3.Transform(Vector3.Zero, view));
        AssertClose(new Vector3(1f, 0f, -5f), Vector3.Transform(Vector3.UnitX, view));
    }

    [TestMethod]
    [DataRow(0.1f, 0f)]
    [DataRow(100f, 1f)]
    public void Projection_uses_system_numerics_zero_to_one_depth(float distance, float expectedDepth)
    {
        // Соглашение Core: глубина NDC в [0, 1]; перевод в [-1, 1] для OpenGL — задача renderer.
        var clip = Vector4.Transform(new Vector4(0f, 0f, -distance, 1f), ValidCamera().GetProjectionMatrix(4f / 3f));

        Assert.AreEqual(expectedDepth, clip.Z / clip.W, Tolerance);
    }

    [TestMethod]
    public void Field_of_view_is_in_degrees()
    {
        var camera = ValidCamera();
        camera.FieldOfViewDegrees = 90f;

        // При 90° и соотношении 1 точка на краю пирамиды видимости (x = -z) попадает на край экрана.
        var clip = Vector4.Transform(new Vector4(1f, 0f, -1f, 1f), camera.GetProjectionMatrix(1f));

        Assert.AreEqual(1f, clip.X / clip.W, Tolerance);
    }

    [TestMethod]
    public void Near_and_far_can_be_changed_in_any_order()
    {
        var camera = ValidCamera();

        camera.NearPlane = 200f; // временно near > far — при присваивании ошибки нет
        camera.FarPlane = 500f;

        camera.Validate();
    }

    [TestMethod]
    [DataRow("near zero")]
    [DataRow("near negative")]
    [DataRow("near NaN")]
    [DataRow("far equals near")]
    [DataRow("far less than near")]
    [DataRow("far infinite")]
    [DataRow("fov zero")]
    [DataRow("fov 180")]
    [DataRow("fov NaN")]
    [DataRow("position equals target")]
    [DataRow("up zero")]
    [DataRow("up parallel")]
    [DataRow("target NaN")]
    public void Invalid_state_fails_validation_and_matrix_computation(string kind)
    {
        var camera = ValidCamera();
        switch (kind)
        {
            case "near zero": camera.NearPlane = 0f; break;
            case "near negative": camera.NearPlane = -1f; break;
            case "near NaN": camera.NearPlane = float.NaN; break;
            case "far equals near": camera.FarPlane = camera.NearPlane; break;
            case "far less than near": camera.FarPlane = 0.05f; break;
            case "far infinite": camera.FarPlane = float.PositiveInfinity; break;
            case "fov zero": camera.FieldOfViewDegrees = 0f; break;
            case "fov 180": camera.FieldOfViewDegrees = 180f; break;
            case "fov NaN": camera.FieldOfViewDegrees = float.NaN; break;
            case "position equals target": camera.Target = camera.Position; break;
            case "up zero": camera.Up = Vector3.Zero; break;
            case "up parallel": camera.Up = new Vector3(0f, 0f, -3f); break;
            case "target NaN": camera.Target = new Vector3(float.NaN, 0f, 0f); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(camera.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetViewMatrix());
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetProjectionMatrix(1f));
    }

    [TestMethod]
    [DataRow(0f)]
    [DataRow(-1f)]
    [DataRow(float.NaN)]
    [DataRow(float.PositiveInfinity)]
    public void Rejects_invalid_aspect_ratio(float aspectRatio)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ValidCamera().GetProjectionMatrix(aspectRatio));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Tolerance);
        Assert.AreEqual(expected.Y, actual.Y, Tolerance);
        Assert.AreEqual(expected.Z, actual.Z, Tolerance);
    }
}
