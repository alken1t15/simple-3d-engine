using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class GpuMeshTests
{
    [TestMethod]
    public void Normal_encoding_keeps_subnormal_components_and_signs()
    {
        var general = Vector3.Normalize(new Vector3(-1f, 2f, -3f));
        Vertex[] vertices =
        [
            new(Vector3.Zero, Vector2.Zero, new Vector3(1f, 1e-40f, 0f)),
            new(Vector3.Zero, Vector2.Zero, general),
        ];

        float[] data = GpuMesh.EncodeNormals(vertices);

        Assert.HasCount(12, data);
        // (1, 1e-40, 0): показатели 0, log2(1e-40) ≈ −132.9 (обычное float) и метка нуля; знаки 1, 1, 0.
        Assert.AreEqual(0f, data[0]);
        Assert.AreEqual(Math.Log2(1e-40), data[1], 1e-3);
        Assert.AreEqual(GpuMesh.ZeroComponentLog2, data[2]);
        CollectionAssert.AreEqual(new[] { 1f, 1f, 0f }, data[3..6]);
        // Обычная нормаль восстанавливается: знак · 2^показатель.
        float[] expected = [general.X, general.Y, general.Z];
        for (int i = 0; i < 3; i++)
            Assert.AreEqual(expected[i], data[9 + i] * MathF.Pow(2f, data[6 + i]), 1e-6f);
    }
}
