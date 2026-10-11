using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class GlProjectionTests
{
    private const float Tolerance = 1e-5f;

    private static readonly Camera Camera = new() { Position = new Vector3(0f, 0f, 5f), Target = Vector3.Zero, NearPlane = 0.5f, FarPlane = 50f };

    [TestMethod]
    [DataRow(0.5f, -1f)]
    [DataRow(50f, 1f)]
    public void Near_and_far_planes_map_to_OpenGL_depth_range(float distance, float expectedDepth)
    {
        var clip = ToClip(new Vector3(0f, 0f, 5f - distance), GlProjection.ToOpenGl(Camera.GetProjectionMatrix(1.5f)));

        Assert.AreEqual(expectedDepth, clip.Z / clip.W, Tolerance);
    }

    [TestMethod]
    [DataRow(0.7f, -0.3f, 2f)]
    [DataRow(-4f, 2f, -30f)]
    public void Depth_is_remapped_once_and_x_y_w_are_unchanged(float x, float y, float z)
    {
        var projection = Camera.GetProjectionMatrix(1.5f);
        var point = new Vector3(x, y, z);

        var core = ToClip(point, projection);
        var openGl = ToClip(point, GlProjection.ToOpenGl(projection));

        // Core: глубина в [0, 1]; OpenGL: z' = 2z − w, то есть z'/w = 2(z/w) − 1.
        Assert.AreEqual(core.X, openGl.X, Tolerance);
        Assert.AreEqual(core.Y, openGl.Y, Tolerance);
        Assert.AreEqual(core.W, openGl.W, Tolerance);
        Assert.AreEqual(2f * core.Z / core.W - 1f, openGl.Z / openGl.W, Tolerance);
    }

    private static Vector4 ToClip(Vector3 world, Matrix4x4 projection) =>
        Vector4.Transform(new Vector4(world, 1f), Camera.GetViewMatrix() * projection);
}
