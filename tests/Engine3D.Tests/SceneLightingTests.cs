using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class SceneLightingTests
{
    private const float Tolerance = 1e-6f;

    [TestMethod]
    public void Defaults_match_contract()
    {
        var lighting = new SceneLighting();

        Assert.AreEqual(new Vector3(0.2f), lighting.AmbientColor);
        Assert.AreEqual(Vector3.One, lighting.DirectionalColor);
        Assert.AreEqual(new Vector3(-1f, -1f, -1f), lighting.Direction);
        lighting.Validate();
    }

    [TestMethod]
    [DataRow(-1f, -1f, -1f)]
    [DataRow(0f, -5f, 0f)]
    [DataRow(1e30f, 0f, 0f)]
    public void Direction_is_normalized(float x, float y, float z)
    {
        var lighting = new SceneLighting { Direction = new Vector3(x, y, z) };

        var direction = lighting.GetNormalizedDirection();

        Assert.AreEqual(1f, direction.Length(), Tolerance);
        var expected = Vector3.Normalize(new Vector3(x, y, z) / MathF.Max(MathF.Abs(x), MathF.Max(MathF.Abs(y), MathF.Abs(z))));
        Assert.AreEqual(expected.X, direction.X, Tolerance);
        Assert.AreEqual(expected.Y, direction.Y, Tolerance);
        Assert.AreEqual(expected.Z, direction.Z, Tolerance);
    }

    [TestMethod]
    public void Zero_colors_are_allowed()
    {
        new SceneLighting { AmbientColor = Vector3.Zero, DirectionalColor = Vector3.Zero }.Validate();
    }

    [TestMethod]
    [DataRow("zero direction")]
    [DataRow("NaN direction")]
    [DataRow("infinite direction")]
    [DataRow("ambient above one")]
    [DataRow("directional negative")]
    [DataRow("ambient NaN")]
    public void Invalid_state_can_be_assigned_but_fails_validation(string kind)
    {
        var lighting = new SceneLighting();
        switch (kind)
        {
            case "zero direction": lighting.Direction = Vector3.Zero; break;
            case "NaN direction": lighting.Direction = new Vector3(float.NaN, -1f, 0f); break;
            case "infinite direction": lighting.Direction = new Vector3(float.NegativeInfinity, 0f, 0f); break;
            case "ambient above one": lighting.AmbientColor = new Vector3(1.1f, 0f, 0f); break;
            case "directional negative": lighting.DirectionalColor = new Vector3(0f, -0.5f, 0f); break;
            case "ambient NaN": lighting.AmbientColor = new Vector3(0f, 0f, float.NaN); break;
            default: Assert.Fail($"Unknown case '{kind}'."); break;
        }

        Assert.ThrowsExactly<InvalidOperationException>(lighting.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(() => lighting.GetNormalizedDirection());
    }
}
