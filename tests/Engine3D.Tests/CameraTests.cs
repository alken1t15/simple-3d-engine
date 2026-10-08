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

    [TestMethod]
    [DataRow("up 1e30")]
    [DataRow("up 1e-30")]
    [DataRow("target -1e30")]
    [DataRow("target -1e-30")]
    public void View_does_not_depend_on_length_of_up_or_distance_to_target(string kind)
    {
        // Регрессия ревью #7: раньше CreateLookAt получал исходные векторы и возвращал вырожденную или неконечную матрицу.
        var camera = new Camera();
        switch (kind)
        {
            case "up 1e30": camera.Up = new Vector3(0f, 1e30f, 0f); break;
            case "up 1e-30": camera.Up = new Vector3(0f, 1e-30f, 0f); break;
            case "target -1e30": camera.Target = new Vector3(0f, 0f, -1e30f); break;
            case "target -1e-30": camera.Target = new Vector3(0f, 0f, -1e-30f); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        camera.Validate();
        AssertClose(Matrix4x4.Identity, camera.GetViewMatrix()); // камера в начале координат смотрит вдоль −Z
    }

    [TestMethod]
    [DataRow(1e30f)]
    [DataRow(1e-30f)]
    [DataRow(3f)]
    public void Scaling_up_vector_keeps_orientation(float factor)
    {
        var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero, Up = new Vector3(factor, factor, 0f) };

        var expected = Matrix4x4.CreateLookAt(new Vector3(3f, 2f, 5f), Vector3.Zero, Vector3.Normalize(new Vector3(1f, 1f, 0f)));

        AssertClose(expected, camera.GetViewMatrix());
    }

    [TestMethod]
    [DataRow(0f, 0f, 5f, 0f, 0f, 0f, 60f, 4f / 3f, 0.1f, 100f)]
    [DataRow(3f, 2f, 5f, 0f, 0f, 0f, 45f, 16f / 9f, 0.5f, 200f)]
    [DataRow(-10f, 4f, -7f, 2f, -1f, 3f, 90f, 1f, 0.01f, 1000f)]
    [DataRow(35f, 30f, 40f, 9f, 9f, 9f, 60f, 1.6f, 0.1f, 200f)]
    public void Ordinary_cameras_match_System_Numerics(
        float px, float py, float pz, float tx, float ty, float tz, float fov, float aspect, float near, float far)
    {
        var camera = new Camera
        {
            Position = new Vector3(px, py, pz),
            Target = new Vector3(tx, ty, tz),
            FieldOfViewDegrees = fov,
            NearPlane = near,
            FarPlane = far,
        };

        AssertClose(Matrix4x4.CreateLookAt(camera.Position, camera.Target, Vector3.UnitY), camera.GetViewMatrix());
        AssertClose(
            Matrix4x4.CreatePerspectiveFieldOfView(fov * (MathF.PI / 180f), aspect, near, far),
            camera.GetProjectionMatrix(aspect));
    }

    [TestMethod]
    public void Points_far_apart_on_both_sides_of_origin_give_valid_camera()
    {
        // Регрессия ревью #7: разность Target − Position переполняется, но точки разные — камера смотрит вдоль +X.
        var camera = new Camera { Position = new Vector3(-3e38f, 0f, 0f), Target = new Vector3(3e38f, 0f, 0f) };

        camera.Validate();
        var view = camera.GetViewMatrix();

        AssertClose(new Vector3(0f, 0f, -1f), Vector3.TransformNormal(Vector3.UnitX, view));
    }

    [TestMethod]
    public void Equal_position_and_target_are_reported_as_equal()
    {
        var camera = new Camera { Position = new Vector3(1f, 2f, 3f), Target = new Vector3(1f, 2f, 3f) };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(camera.Validate);

        Assert.Contains("must differ", exception.Message);
    }

    [TestMethod]
    public void Position_too_far_for_view_translation_is_rejected_before_use()
    {
        // Регрессия ревью #7: сдвиг −dot(ось, Position) переполняет float, раньше возвращалась матрица из NaN.
        var camera = new Camera { Position = new Vector3(3e38f, 3e38f, 3e38f), Target = Vector3.Zero };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(camera.Validate);
        Assert.Contains("not representable", exception.Message);
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetViewMatrix());
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetProjectionMatrix(1f));
    }

    [TestMethod]
    [DataRow(1e-40f)]
    [DataRow(3e38f)]
    public void Aspect_ratio_with_unrepresentable_width_scale_is_an_argument_error(float aspectRatio)
    {
        // Регрессия ревью #7: при aspect = 1e-40 раньше возвращалась матрица с бесконечностью.
        var camera = new Camera();

        camera.Validate(); // состояние камеры корректно, неверен только аргумент
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => camera.GetProjectionMatrix(aspectRatio));
    }

    [TestMethod]
    [DataRow(float.Epsilon)]
    [DataRow(1e-38f)]
    public void Field_of_view_too_small_for_projection_is_invalid_state(float fieldOfViewDegrees)
    {
        // Регрессия ревью #7: угол проходил проверку (0, 180), но в радианах обращался в 0, и наружу выходило
        // ArgumentOutOfRangeException("fieldOfView") из System.Numerics.
        var camera = new Camera { FieldOfViewDegrees = fieldOfViewDegrees };

        Assert.ThrowsExactly<InvalidOperationException>(camera.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetViewMatrix());
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetProjectionMatrix(1f));
    }

    [TestMethod]
    public void Near_and_far_too_close_for_depth_range_are_invalid_state()
    {
        var camera = new Camera { NearPlane = 3e38f, FarPlane = MathF.BitIncrement(3e38f) };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(camera.Validate);
        Assert.Contains("too close", exception.Message);
        Assert.ThrowsExactly<InvalidOperationException>(() => camera.GetProjectionMatrix(1f));
    }

    private static void AssertClose(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float e = expected[row, column];
                float a = actual[row, column];
                Assert.IsTrue(float.IsFinite(a), $"Element [{row},{column}] is not finite: {a}.");
                Assert.AreEqual(e, a, Tolerance * MathF.Max(1f, MathF.Abs(e)), $"Element [{row},{column}].");
            }
        }
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Tolerance);
        Assert.AreEqual(expected.Y, actual.Y, Tolerance);
        Assert.AreEqual(expected.Z, actual.Z, Tolerance);
    }
}
