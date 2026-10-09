using System.Numerics;

namespace Engine3D.Core;

/// <summary>Процедурные CPU-меши с UV и нормалями, без внешних ассетов и OpenGL.</summary>
public static class PrimitiveFactory
{
    /// <summary>
    /// Создает куб с центром в начале координат: 24 вершины, 36 индексов, 12 треугольников.
    /// У каждой грани собственные вершины, UV и внешняя единичная нормаль.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Размер не положительный или не конечный.</exception>
    public static MeshData CreateCube(float size = 1f)
    {
        ValidateDimension(size, nameof(size));
        float h = size / 2f;
        var vertices = new Vertex[24];
        var indices = new uint[36];

        // Как в spike: CCW снаружи, начиная снизу слева. Верх боковых граней — +Y,
        // верх грани +Y — -Z, верх грани -Y — +Z. UV (0, 0) снизу слева.
        WriteQuad(vertices, indices, 0, Vector3.UnitX,
            new(h, -h, h), new(h, -h, -h), new(h, h, -h), new(h, h, h));
        WriteQuad(vertices, indices, 1, -Vector3.UnitX,
            new(-h, -h, -h), new(-h, -h, h), new(-h, h, h), new(-h, h, -h));
        WriteQuad(vertices, indices, 2, Vector3.UnitY,
            new(-h, h, h), new(h, h, h), new(h, h, -h), new(-h, h, -h));
        WriteQuad(vertices, indices, 3, -Vector3.UnitY,
            new(-h, -h, -h), new(h, -h, -h), new(h, -h, h), new(-h, -h, h));
        WriteQuad(vertices, indices, 4, Vector3.UnitZ,
            new(-h, -h, h), new(h, -h, h), new(h, h, h), new(-h, h, h));
        WriteQuad(vertices, indices, 5, -Vector3.UnitZ,
            new(h, -h, -h), new(-h, -h, -h), new(-h, h, -h), new(h, h, -h));

        return new MeshData(vertices, indices, new CubeDescription(size));
    }

    /// <summary>
    /// Создает центрированную плоскость в XZ: ширина вдоль X, глубина вдоль Z,
    /// четыре вершины, шесть индексов и нормаль +Y. Лицевая сторона видна сверху.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Ширина или глубина не положительна или не конечна.</exception>
    public static MeshData CreatePlane(float width = 1f, float depth = 1f)
    {
        ValidateDimension(width, nameof(width));
        ValidateDimension(depth, nameof(depth));
        float x = width / 2f;
        float z = depth / 2f;
        var vertices = new Vertex[4];
        var indices = new uint[6];

        WriteQuad(vertices, indices, 0, Vector3.UnitY,
            new(-x, 0f, z), new(x, 0f, z), new(x, 0f, -z), new(-x, 0f, -z));

        return new MeshData(vertices, indices, new PlaneDescription(width, depth));
    }

    private static void ValidateDimension(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(parameterName, value, "Dimension must be positive and finite.");
    }

    private static void WriteQuad(Vertex[] vertices, uint[] indices, int quad, Vector3 normal,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = quad * 4;
        vertices[first] = new Vertex(a, new(0f, 0f), normal);
        vertices[first + 1] = new Vertex(b, new(1f, 0f), normal);
        vertices[first + 2] = new Vertex(c, new(1f, 1f), normal);
        vertices[first + 3] = new Vertex(d, new(0f, 1f), normal);

        int at = quad * 6;
        indices[at] = (uint)first;
        indices[at + 1] = (uint)(first + 1);
        indices[at + 2] = (uint)(first + 2);
        indices[at + 3] = (uint)first;
        indices[at + 4] = (uint)(first + 2);
        indices[at + 5] = (uint)(first + 3);
    }
}
