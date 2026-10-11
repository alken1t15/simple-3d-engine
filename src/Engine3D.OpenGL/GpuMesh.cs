using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>
/// GPU-представление одного <see cref="MeshData"/>: VAO, вершинный буфер, буфер нормалей (см. <see cref="EncodeNormals"/>)
/// и индексный буфер.
/// </summary>
internal sealed class GpuMesh : IDisposable
{
    /// <summary>Показатель log2 для нулевой компоненты нормали: никогда не бывает наибольшим, а ее знак равен 0.</summary>
    internal const float ZeroComponentLog2 = -1e30f;

    // Раскладка Vertex из Core: Position (смещение 0), Uv (12), Normal (20), шаг 32 байта. Нормаль шейдер берет
    // из буфера нормалей, поэтому Normal отсюда не читается.
    private static readonly int VertexSize = Unsafe.SizeOf<Vertex>();
    private const int EncodedNormalSize = 6 * sizeof(float);

    private readonly int _vertexArray;
    private readonly int _vertexBuffer;
    private readonly int _normalBuffer;
    private readonly int _indexBuffer;
    private readonly int _indexCount;
    private bool _disposed;

    public GpuMesh(MeshData mesh)
    {
        _indexCount = mesh.IndexCount;
        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        _normalBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();
        try
        {
            GL.BindVertexArray(_vertexArray);

            // Вершины читаются прямо из ReadOnlySpan Core на время загрузки — без копии и без хранения span.
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, checked(mesh.VertexCount * VertexSize),
                ref MemoryMarshal.GetReference(mesh.Vertices), BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexSize, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexSize, 12);

            float[] normals = EncodeNormals(mesh.Vertices);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _normalBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, checked(normals.Length * sizeof(float)), normals, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, EncodedNormalSize, 0);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, EncodedNormalSize, 3 * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, checked(mesh.IndexCount * sizeof(uint)),
                ref MemoryMarshal.GetReference(mesh.Indices), BufferUsageHint.StaticDraw);

            GL.BindVertexArray(0);
            GlErrors.ThrowIfAny("uploading a mesh to the GPU");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Draw()
    {
        GL.BindVertexArray(_vertexArray);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteBuffer(_normalBuffer);
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vertexArray);
    }

    /// <summary>
    /// Нормали для шейдера: на вершину 6 float — log2 модулей компонент (x, y, z), затем их знаки (−1, 0 или 1).
    /// Компонента единичной нормали бывает subnormal (например, 1e-40): GPU вправе обнулить такое число, а после деления
    /// на малый допустимый Scale оно значимо. Показатель, посчитанный на CPU в double, — обычное число, поэтому шейдер
    /// получает все компоненты без потерь и делит на масштаб сложением показателей.
    /// </summary>
    internal static float[] EncodeNormals(ReadOnlySpan<Vertex> vertices)
    {
        var data = new float[vertices.Length * 6];
        for (int i = 0; i < vertices.Length; i++)
        {
            var normal = vertices[i].Normal;
            Encode(normal.X, data, i * 6);
            Encode(normal.Y, data, i * 6 + 1);
            Encode(normal.Z, data, i * 6 + 2);
        }

        return data;
    }

    private static void Encode(float component, float[] data, int index)
    {
        data[index] = component == 0f ? ZeroComponentLog2 : (float)Math.Log2(Math.Abs((double)component));
        data[index + 3] = MathF.Sign(component);
    }
}
