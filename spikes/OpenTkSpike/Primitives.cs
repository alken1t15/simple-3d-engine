using System.Numerics;
using System.Runtime.InteropServices;

namespace OpenTkSpike;

// Формат вершины совпадает с проектным Vertex из docs/api.md: позиция + UV.
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Vertex(Vector3 Position, Vector2 Uv)
{
    public static readonly int SizeInBytes = Marshal.SizeOf<Vertex>();
}

internal static class Primitives
{
    // UV углов каждой грани в порядке: левый нижний, правый нижний, правый верхний, левый верхний.
    // UV (0, 0) — левый нижний угол изображения (соглашение из docs/architecture.md).
    private static readonly Vector2[] QuadUvs = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];

    // Единичный куб с центром в начале координат: 6 граней по 4 вершины (24 вершины, 36 индексов).
    // Вершины грани перечислены против часовой стрелки, если смотреть на грань снаружи,
    // начиная с левого нижнего угла. «Верх» боковых граней — +Y, верхней грани — -Z, нижней — +Z.
    public static (Vertex[] Vertices, uint[] Indices) CreateCube()
    {
        Vector3[][] faces =
        [
            [new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f)],     // +X
            [new(-0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f)], // -X
            [new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f)],     // +Y
            [new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f)], // -Y
            [new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)],     // +Z
            [new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f)], // -Z
        ];

        var vertices = new Vertex[faces.Length * 4];
        var indices = new uint[faces.Length * 6];

        for (int face = 0; face < faces.Length; face++)
            WriteQuad(vertices, indices, face, faces[face]);

        return (vertices, indices);
    }

    // Квадратная плоскость в XZ с центром в начале координат, лицевая сторона смотрит вверх (+Y).
    public static (Vertex[] Vertices, uint[] Indices) CreatePlane(float size)
    {
        float h = size / 2f;
        var vertices = new Vertex[4];
        var indices = new uint[6];

        WriteQuad(vertices, indices, 0, [new(-h, 0f, h), new(h, 0f, h), new(h, 0f, -h), new(-h, 0f, -h)]);

        return (vertices, indices);
    }

    private static void WriteQuad(Vertex[] vertices, uint[] indices, int quad, Vector3[] corners)
    {
        uint first = (uint)(quad * 4);
        for (int i = 0; i < 4; i++)
            vertices[first + i] = new Vertex(corners[i], QuadUvs[i]);

        int at = quad * 6;
        indices[at] = first;
        indices[at + 1] = first + 1;
        indices[at + 2] = first + 2;
        indices[at + 3] = first;
        indices[at + 4] = first + 2;
        indices[at + 5] = first + 3;
    }
}
