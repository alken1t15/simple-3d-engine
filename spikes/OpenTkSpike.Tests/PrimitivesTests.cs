using System.Numerics;

namespace OpenTkSpike.Tests;

public class PrimitivesTests
{
    [Fact]
    public void Cube_has_four_vertices_and_two_triangles_per_face()
    {
        var (vertices, indices) = Primitives.CreateCube();

        Assert.Equal(24, vertices.Length);
        Assert.Equal(36, indices.Length);
        Assert.All(indices, index => Assert.InRange(index, 0u, (uint)vertices.Length - 1));
    }

    [Fact]
    public void Cube_triangles_are_counter_clockwise_when_viewed_from_outside()
    {
        var (vertices, indices) = Primitives.CreateCube();

        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]].Position;
            var b = vertices[indices[i + 1]].Position;
            var c = vertices[indices[i + 2]].Position;

            // Для выпуклого куба с центром в начале координат «наружу» — это направление на центр треугольника.
            var normal = Vector3.Cross(b - a, c - a);
            var outward = (a + b + c) / 3f;

            Assert.True(Vector3.Dot(normal, outward) > 0f, $"Triangle {i / 3} is clockwise from outside.");
        }
    }

    [Fact]
    public void Each_cube_face_maps_the_whole_texture()
    {
        var (vertices, _) = Primitives.CreateCube();
        Vector2[] expectedCorners = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];

        for (int face = 0; face < 6; face++)
        {
            var uvs = vertices.Skip(face * 4).Take(4).Select(v => v.Uv);
            Assert.Equal(expectedCorners, uvs);
        }
    }

    [Fact]
    public void Plane_faces_up_and_maps_the_whole_texture()
    {
        var (vertices, indices) = Primitives.CreatePlane(3f);

        Assert.Equal(4, vertices.Length);
        Assert.Equal(6, indices.Length);
        Assert.All(vertices, v => Assert.Equal(0f, v.Position.Y));

        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]].Position;
            var normal = Vector3.Cross(vertices[indices[i + 1]].Position - a, vertices[indices[i + 2]].Position - a);
            Assert.True(normal.Y > 0f, $"Triangle {i / 3} does not face +Y.");
        }

        Assert.Equal(new Vector2(0f, 0f), vertices[0].Uv);
        Assert.Equal(new Vector3(-1.5f, 0f, 1.5f), vertices[0].Position);
    }
}
