namespace Engine3D.Core;

/// <summary>
/// Неизменяемые CPU-данные индексного треугольного меша. Один экземпляр может использоваться многими
/// <see cref="SceneObject"/>: renderer кеширует GPU-представление по ссылке на этот объект.
/// </summary>
public sealed class MeshData
{
    private readonly Vertex[] _vertices;
    private readonly uint[] _indices;

    /// <summary>
    /// Копирует и проверяет вершины и индексы. Нормали в собственной копии приводятся к единичной длине.
    /// </summary>
    /// <exception cref="ArgumentNullException">Список вершин или индексов равен null.</exception>
    /// <exception cref="ArgumentException">
    /// Нет вершин или индексов; число индексов не кратно трем; индекс вне диапазона вершин;
    /// позиция, UV или нормаль не конечны; нормаль нулевой длины.
    /// </exception>
    public MeshData(IReadOnlyList<Vertex> vertices, IReadOnlyList<uint> indices)
        : this(vertices, indices, primitive: null)
    {
    }

    internal MeshData(IReadOnlyList<Vertex> vertices, IReadOnlyList<uint> indices, PrimitiveDescription? primitive)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        // Сначала копия, затем проверка копии: вызывающий код не может изменить данные между проверкой и использованием.
        _vertices = vertices.ToArray();
        _indices = indices.ToArray();

        if (_vertices.Length == 0)
            throw new ArgumentException("Mesh must contain at least one vertex.", nameof(vertices));
        if (_indices.Length == 0)
            throw new ArgumentException("Mesh must contain at least one triangle.", nameof(indices));
        if (_indices.Length % 3 != 0)
            throw new ArgumentException($"Index count must be a multiple of 3, got {_indices.Length}.", nameof(indices));

        for (int i = 0; i < _vertices.Length; i++)
            _vertices[i] = ValidateAndNormalize(_vertices[i], i);

        for (int i = 0; i < _indices.Length; i++)
        {
            if (_indices[i] >= (uint)_vertices.Length)
            {
                throw new ArgumentException(
                    $"Index {_indices[i]} at position {i} is out of range: mesh has {_vertices.Length} vertices.",
                    nameof(indices));
            }
        }

        Primitive = primitive;
    }

    public int VertexCount => _vertices.Length;

    public int IndexCount => _indices.Length;

    public int TriangleCount => _indices.Length / 3;

    /// <summary>Вершины только для чтения — для загрузки на GPU без копирования.</summary>
    public ReadOnlySpan<Vertex> Vertices => _vertices;

    /// <summary>Индексы треугольников только для чтения, по три на треугольник.</summary>
    public ReadOnlySpan<uint> Indices => _indices;

    internal PrimitiveDescription? Primitive { get; }

    private static Vertex ValidateAndNormalize(Vertex vertex, int index)
    {
        if (!Finite.IsFinite(vertex.Position))
            throw new ArgumentException($"Vertex {index} has a non-finite position {vertex.Position}.", "vertices");
        if (!Finite.IsFinite(vertex.Uv))
            throw new ArgumentException($"Vertex {index} has non-finite UV {vertex.Uv}.", "vertices");
        if (!Finite.TryNormalize(vertex.Normal, out var normal))
            throw new ArgumentException($"Vertex {index} has an invalid normal {vertex.Normal}: it must be finite and non-zero.", "vertices");

        return vertex with { Normal = normal };
    }
}
