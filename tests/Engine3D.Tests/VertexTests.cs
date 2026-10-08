using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class VertexTests
{
    [TestMethod]
    public void Vertex_is_32_bytes_laid_out_as_position_uv_normal()
    {
        Vertex[] vertices = [new(new Vector3(1f, 2f, 3f), new Vector2(4f, 5f), new Vector3(6f, 7f, 8f))];

        // Renderer передает массив вершин на GPU как есть: шаг 32 байта, смещения 0 / 12 / 20.
        Assert.AreEqual(32, Unsafe.SizeOf<Vertex>());
        CollectionAssert.AreEqual(
            new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f },
            MemoryMarshal.Cast<Vertex, float>(vertices).ToArray());
    }
}
