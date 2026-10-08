using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class PrimitiveFactoryTests
{
    private static readonly Vector2[] QuadUvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
    private static readonly Vector3[] CubeNormals =
        [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    [TestMethod]
    public void Default_cube_has_expected_counts_and_centered_unit_bounds()
    {
        var cube = PrimitiveFactory.CreateCube();

        Assert.AreEqual(24, cube.VertexCount);
        Assert.AreEqual(36, cube.IndexCount);
        Assert.AreEqual(12, cube.TriangleCount);
        AssertBounds(cube, new(-0.5f), new(0.5f));
        Assert.AreEqual(new CubeDescription(1f), cube.Primitive);
        AssertValidIndices(cube);
    }

    [TestMethod]
    [DataRow(2f)]
    [DataRow(0.25f)]
    [DataRow(1e30f)]
    [DataRow(1e-30f)]
    public void Cube_preserves_size_and_has_ccw_outward_normals(float size)
    {
        var cube = PrimitiveFactory.CreateCube(size);

        AssertBounds(cube, new(-size / 2f), new(size / 2f));
        Assert.AreEqual(new CubeDescription(size), cube.Primitive);
        AssertValidIndices(cube);
        AssertTriangleNormals(cube);
        for (int face = 0; face < 6; face++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                var vertex = cube.Vertices[face * 4 + corner];
                Assert.AreEqual(CubeNormals[face], vertex.Normal);
                Assert.AreEqual(size / 2f, Vector3.Dot(vertex.Position, vertex.Normal));
            }
        }
    }

    [TestMethod]
    public void Cube_duplicates_eight_corners_for_three_independent_face_normals()
    {
        var vertices = PrimitiveFactory.CreateCube().Vertices.ToArray();
        var corners = vertices.GroupBy(v => v.Position).ToArray();

        Assert.HasCount(8, corners);
        foreach (var corner in corners)
        {
            Assert.AreEqual(3, corner.Count());
            Assert.AreEqual(3, corner.Select(v => v.Normal).Distinct().Count());
        }
    }

    [TestMethod]
    public void Cube_uvs_and_face_up_directions_match_spike()
    {
        var cube = PrimitiveFactory.CreateCube();
        for (int face = 0; face < 6; face++)
        {
            var vertices = cube.Vertices.Slice(face * 4, 4);
            for (int corner = 0; corner < 4; corner++)
                Assert.AreEqual(QuadUvs[corner], vertices[corner].Uv);

            // UV сверху должны соответствовать принятому локальному верху грани.
            Vector3 up = face switch { 2 => -Vector3.UnitZ, 3 => Vector3.UnitZ, _ => Vector3.UnitY };
            Vector3 right = Vector3.Cross(up, CubeNormals[face]);
            Assert.AreEqual(up, Vector3.Normalize(vertices[3].Position - vertices[0].Position));
            Assert.AreEqual(right, Vector3.Normalize(vertices[1].Position - vertices[0].Position));
            uint first = (uint)(face * 4);
            uint[] expectedIndices = [first, first + 1, first + 2, first, first + 2, first + 3];
            CollectionAssert.AreEqual(expectedIndices, cube.Indices.Slice(face * 6, 6).ToArray());
        }
    }

    [TestMethod]
    public void Default_plane_has_four_vertices_two_triangles_and_unit_xz_bounds()
    {
        var plane = PrimitiveFactory.CreatePlane();

        Assert.AreEqual(4, plane.VertexCount);
        Assert.AreEqual(6, plane.IndexCount);
        Assert.AreEqual(2, plane.TriangleCount);
        AssertBounds(plane, new(-0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f));
        Assert.AreEqual(new PlaneDescription(1f, 1f), plane.Primitive);
        AssertValidIndices(plane);
        AssertTriangleNormals(plane);
        CollectionAssert.AreEqual(new uint[] { 0, 1, 2, 0, 2, 3 }, plane.Indices.ToArray());
    }

    [TestMethod]
    [DataRow(6f, 4f)]
    [DataRow(0.25f, 8f)]
    [DataRow(1e30f, 1e-30f)]
    public void Rectangular_plane_preserves_width_depth_uvs_and_upward_normals(float width, float depth)
    {
        var plane = PrimitiveFactory.CreatePlane(width, depth);

        Vector3[] positions = [new(-width / 2f, 0f, depth / 2f), new(width / 2f, 0f, depth / 2f),
            new(width / 2f, 0f, -depth / 2f), new(-width / 2f, 0f, -depth / 2f)];
        for (int i = 0; i < 4; i++)
        {
            Assert.AreEqual(positions[i], plane.Vertices[i].Position);
            Assert.AreEqual(QuadUvs[i], plane.Vertices[i].Uv);
            Assert.AreEqual(Vector3.UnitY, plane.Vertices[i].Normal);
        }
        Assert.AreEqual(new PlaneDescription(width, depth), plane.Primitive);
        AssertValidIndices(plane);
        AssertTriangleNormals(plane);
    }

    public static IEnumerable<object[]> InvalidDimensions =>
        [new object[] { 0f }, [-0f], [-1f], [float.NaN], [float.PositiveInfinity], [float.NegativeInfinity]];

    [TestMethod]
    [DynamicData(nameof(InvalidDimensions))]
    public void Rejects_invalid_cube_size_with_parameter_name(float size)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrimitiveFactory.CreateCube(size));
        Assert.AreEqual("size", error.ParamName);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDimensions))]
    public void Rejects_invalid_plane_width_with_parameter_name(float width)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrimitiveFactory.CreatePlane(width, 2f));
        Assert.AreEqual("width", error.ParamName);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDimensions))]
    public void Rejects_invalid_plane_depth_with_parameter_name(float depth)
    {
        var error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PrimitiveFactory.CreatePlane(2f, depth));
        Assert.AreEqual("depth", error.ParamName);
    }

    [TestMethod]
    [DataRow(float.MaxValue)]
    [DataRow(float.Epsilon)]
    public void Positive_finite_extreme_dimensions_produce_finite_data(float size)
    {
        // MeshData допускает вырожденные треугольники. size/2 для float.Epsilon
        // округляется в ноль: положительные конечные размеры не отклоняем сверх API-контракта.
        foreach (var mesh in new[] { PrimitiveFactory.CreateCube(size), PrimitiveFactory.CreatePlane(size, size) })
        {
            foreach (var vertex in mesh.Vertices)
            {
                Assert.IsTrue(float.IsFinite(vertex.Position.X));
                Assert.IsTrue(float.IsFinite(vertex.Position.Y));
                Assert.IsTrue(float.IsFinite(vertex.Position.Z));
                Assert.AreEqual(1f, vertex.Normal.Length());
            }
            AssertValidIndices(mesh);
        }
    }

    [TestMethod]
    public void Primitive_descriptions_reconstruct_identical_geometry_for_future_serializer()
    {
        var cube = PrimitiveFactory.CreateCube(2.5f);
        var cubeDescription = (CubeDescription)cube.Primitive!;
        AssertSameGeometry(cube, PrimitiveFactory.CreateCube(cubeDescription.Size));

        var plane = PrimitiveFactory.CreatePlane(7f, 3.5f);
        var planeDescription = (PlaneDescription)plane.Primitive!;
        AssertSameGeometry(plane, PrimitiveFactory.CreatePlane(planeDescription.Width, planeDescription.Depth));
    }

    [TestMethod]
    public void Repeated_calls_create_independent_mesh_instances_with_same_data()
    {
        var first = PrimitiveFactory.CreateCube();
        var second = PrimitiveFactory.CreateCube();
        Assert.AreNotSame(first, second);
        AssertSameGeometry(first, second);
    }

    private static void AssertSameGeometry(MeshData expected, MeshData actual)
    {
        CollectionAssert.AreEqual(expected.Vertices.ToArray(), actual.Vertices.ToArray());
        CollectionAssert.AreEqual(expected.Indices.ToArray(), actual.Indices.ToArray());
    }

    private static void AssertValidIndices(MeshData mesh)
    {
        Assert.AreEqual(0, mesh.IndexCount % 3);
        var referenced = new HashSet<uint>();
        foreach (uint index in mesh.Indices)
        {
            Assert.IsLessThan((uint)mesh.VertexCount, index);
            referenced.Add(index);
        }
        Assert.HasCount(mesh.VertexCount, referenced, "Every generated vertex must be referenced.");
    }

    private static void AssertBounds(MeshData mesh, Vector3 minimum, Vector3 maximum)
    {
        var positions = mesh.Vertices.ToArray().Select(v => v.Position).ToArray();
        Assert.AreEqual(minimum, positions.Aggregate(Vector3.Min));
        Assert.AreEqual(maximum, positions.Aggregate(Vector3.Max));
    }

    private static void AssertTriangleNormals(MeshData mesh)
    {
        for (int i = 0; i < mesh.IndexCount; i += 3)
        {
            var a = mesh.Vertices[(int)mesh.Indices[i]];
            var b = mesh.Vertices[(int)mesh.Indices[i + 1]];
            var c = mesh.Vertices[(int)mesh.Indices[i + 2]];
            // double сохраняет проверку winding для очень маленьких и больших float-размеров.
            double ux = (double)b.Position.X - a.Position.X, uy = (double)b.Position.Y - a.Position.Y,
                uz = (double)b.Position.Z - a.Position.Z;
            double vx = (double)c.Position.X - a.Position.X, vy = (double)c.Position.Y - a.Position.Y,
                vz = (double)c.Position.Z - a.Position.Z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            Assert.IsGreaterThan(0d, length, $"Triangle {i / 3} must not be degenerate.");
            Assert.AreEqual((double)a.Normal.X, nx / length, 1e-6);
            Assert.AreEqual((double)a.Normal.Y, ny / length, 1e-6);
            Assert.AreEqual((double)a.Normal.Z, nz / length, 1e-6);
            Assert.AreEqual(a.Normal, b.Normal);
            Assert.AreEqual(a.Normal, c.Normal);
            Assert.AreEqual(1f, a.Normal.Length(), 1e-6f);
        }
    }
}
