using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class TransformTests
{
    private const float Tolerance = 1e-5f;

    [TestMethod]
    public void Default_transform_gives_identity_matrix()
    {
        Assert.AreEqual(Matrix4x4.Identity, new Transform().GetModelMatrix());
    }

    [TestMethod]
    public void Model_matrix_applies_scale_then_rotation_then_translation()
    {
        var transform = new Transform
        {
            Scale = new Vector3(2f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            Position = new Vector3(0f, 0f, 5f),
        };

        // (1,0,0) → масштаб (2,0,0) → поворот на +90° вокруг Y в правой системе (0,0,-2) → сдвиг (0,0,3).
        var point = Vector3.Transform(Vector3.UnitX, transform.GetModelMatrix());

        AssertClose(new Vector3(0f, 0f, 3f), point);
        Assert.AreEqual(new Vector3(0f, 0f, 5f), transform.GetModelMatrix().Translation); // сдвиг в M41..M43
    }

    [TestMethod]
    [DataRow(3f)]
    [DataRow(1e-30f)]
    [DataRow(1e30f)]
    public void Non_unit_rotation_is_normalized(float factor)
    {
        var unit = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 0.7f);
        var scaled = new Quaternion(unit.X * factor, unit.Y * factor, unit.Z * factor, unit.W * factor);

        var expected = new Transform { Rotation = unit }.GetModelMatrix();
        var actual = new Transform { Rotation = scaled }.GetModelMatrix();

        Assert.AreEqual(Round(expected), Round(actual));
    }

    [TestMethod]
    public void Negative_scale_is_allowed()
    {
        var transform = new Transform { Scale = new Vector3(-1f, 1f, 1f) };

        transform.Validate();
        Assert.IsLessThan(0f, transform.GetModelMatrix().GetDeterminant()); // (верхняя граница, значение): зеркальное отражение
    }

    [TestMethod]
    [DataRow("zero rotation")]
    [DataRow("NaN rotation")]
    [DataRow("NaN position")]
    [DataRow("inf scale")]
    [DataRow("zero scale x")]
    [DataRow("zero scale y")]
    [DataRow("zero scale z")]
    public void Invalid_state_can_be_assigned_but_fails_validation(string kind)
    {
        var transform = new Transform();
        switch (kind)
        {
            case "zero rotation": transform.Rotation = default; break;
            case "NaN rotation": transform.Rotation = new Quaternion(float.NaN, 0f, 0f, 1f); break;
            case "NaN position": transform.Position = new Vector3(0f, float.NaN, 0f); break;
            case "inf scale": transform.Scale = new Vector3(float.PositiveInfinity, 1f, 1f); break;
            case "zero scale x": transform.Scale = new Vector3(0f, 1f, 1f); break;
            case "zero scale y": transform.Scale = new Vector3(1f, 0f, 1f); break;
            case "zero scale z": transform.Scale = new Vector3(1f, 1f, 0f); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(transform.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(() => transform.GetModelMatrix());
    }

    private static Matrix4x4 Round(Matrix4x4 m)
    {
        static float R(float v) => MathF.Round(v, 5);
        return new Matrix4x4(R(m.M11), R(m.M12), R(m.M13), R(m.M14), R(m.M21), R(m.M22), R(m.M23), R(m.M24),
            R(m.M31), R(m.M32), R(m.M33), R(m.M34), R(m.M41), R(m.M42), R(m.M43), R(m.M44));
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.AreEqual(expected.X, actual.X, Tolerance);
        Assert.AreEqual(expected.Y, actual.Y, Tolerance);
        Assert.AreEqual(expected.Z, actual.Z, Tolerance);
    }
}
