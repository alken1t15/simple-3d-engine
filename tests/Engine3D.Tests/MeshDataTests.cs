using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class MeshDataTests
{
    private static Vertex V(float x, float y, float z) => new(new Vector3(x, y, z), Vector2.Zero, Vector3.UnitZ);

    private static Vertex[] Quad() => [V(0f, 0f, 0f), V(1f, 0f, 0f), V(1f, 1f, 0f), V(0f, 1f, 0f)];

    private static uint[] QuadIndices() => [0u, 1u, 2u, 0u, 2u, 3u];

    [TestMethod]
    public void Counts_vertices_indices_and_triangles()
    {
        var mesh = new MeshData(Quad(), QuadIndices());

        Assert.AreEqual(4, mesh.VertexCount);
        Assert.AreEqual(6, mesh.IndexCount);
        Assert.AreEqual(2, mesh.TriangleCount);
        CollectionAssert.AreEqual(QuadIndices(), mesh.Indices.ToArray());
        CollectionAssert.AreEqual(Quad(), mesh.Vertices.ToArray());
    }

    [TestMethod]
    public void Copies_input_so_later_changes_to_source_arrays_do_not_affect_mesh()
    {
        var vertices = Quad();
        var indices = QuadIndices();
        var mesh = new MeshData(vertices, indices);

        vertices[0] = V(9f, 9f, 9f);
        indices[0] = 3u;

        Assert.AreEqual(Vector3.Zero, mesh.Vertices[0].Position);
        Assert.AreEqual(0u, mesh.Indices[0]);
    }

    [TestMethod]
    [DataRow(0f, 0f, 5f, 0f, 0f, 1f)]
    [DataRow(3f, 4f, 0f, 0.6f, 0.8f, 0f)]
    [DataRow(1e30f, 0f, 0f, 1f, 0f, 0f)]    // квадрат длины переполнил бы float
    [DataRow(0f, -1e-40f, 0f, 0f, -1f, 0f)] // квадрат длины обнулился бы
    public void Normalizes_normals_in_own_copy(float x, float y, float z, float ex, float ey, float ez)
    {
        Vertex[] vertices = [new(Vector3.Zero, Vector2.Zero, new Vector3(x, y, z))];

        var normal = new MeshData(vertices, [0u, 0u, 0u]).Vertices[0].Normal;

        Assert.AreEqual(ex, normal.X, 1e-6f);
        Assert.AreEqual(ey, normal.Y, 1e-6f);
        Assert.AreEqual(ez, normal.Z, 1e-6f);
    }

    [TestMethod]
    public void Rejects_null_arguments()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new MeshData(null!, QuadIndices()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new MeshData(Quad(), null!));
    }

    [TestMethod]
    public void Rejects_empty_vertices_and_indices()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new MeshData([], [0u, 0u, 0u]));
        Assert.ThrowsExactly<ArgumentException>(() => new MeshData(Quad(), []));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(5)]
    public void Rejects_index_count_not_multiple_of_three(int count)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new MeshData(Quad(), new uint[count]));

        Assert.Contains(count.ToString(), exception.Message);
    }

    [TestMethod]
    public void Rejects_out_of_range_index_with_its_value_and_position()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new MeshData(Quad(), [0u, 1u, 2u, 0u, 2u, 4u]));

        Assert.Contains("Index 4 at position 5", exception.Message);
        Assert.AreEqual("indices", exception.ParamName);
    }

    public static IEnumerable<object[]> InvalidVertices =>
    [
        [new Vertex(new Vector3(float.NaN, 0f, 0f), Vector2.Zero, Vector3.UnitZ)],
        [new Vertex(new Vector3(0f, float.PositiveInfinity, 0f), Vector2.Zero, Vector3.UnitZ)],
        [new Vertex(Vector3.Zero, new Vector2(float.NaN, 0f), Vector3.UnitZ)],
        [new Vertex(Vector3.Zero, new Vector2(0f, float.NegativeInfinity), Vector3.UnitZ)],
        [new Vertex(Vector3.Zero, Vector2.Zero, Vector3.Zero)],
        [new Vertex(Vector3.Zero, Vector2.Zero, new Vector3(0f, float.NaN, 1f))],
        [new Vertex(Vector3.Zero, Vector2.Zero, new Vector3(float.PositiveInfinity, 0f, 0f))],
    ];

    [TestMethod]
    [DynamicData(nameof(InvalidVertices))]
    public void Rejects_non_finite_data_and_zero_normals(Vertex invalid)
    {
        Vertex[] vertices = [V(0f, 0f, 0f), invalid, V(1f, 1f, 0f)];

        var exception = Assert.ThrowsExactly<ArgumentException>(() => new MeshData(vertices, [0u, 1u, 2u]));

        Assert.Contains("Vertex 1", exception.Message);
    }

    [TestMethod]
    public void Allows_degenerate_triangles()
    {
        var mesh = new MeshData([V(0f, 0f, 0f), V(1f, 0f, 0f)], [0u, 0u, 1u, 1u, 1u, 1u]);

        Assert.AreEqual(2, mesh.TriangleCount);
    }

    [TestMethod]
    public void Public_constructor_has_no_primitive_description_and_factories_can_attach_one()
    {
        Assert.IsNull(new MeshData(Quad(), QuadIndices()).Primitive);

        var cube = new MeshData(Quad(), QuadIndices(), new CubeDescription(2f));
        var plane = new MeshData(Quad(), QuadIndices(), new PlaneDescription(3f, 4f));

        Assert.AreEqual<PrimitiveDescription?>(new CubeDescription(2f), cube.Primitive);
        Assert.AreEqual<PrimitiveDescription?>(new PlaneDescription(3f, 4f), plane.Primitive);
    }
}
