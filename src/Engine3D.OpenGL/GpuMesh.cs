using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>GPU-представление одного <see cref="MeshData"/>: VAO, вершинный и индексный буферы.</summary>
internal sealed class GpuMesh : IDisposable
{
    // Раскладка Vertex из Core: Position (смещение 0), Uv (12), Normal (20), шаг 32 байта.
    private static readonly int VertexSize = Unsafe.SizeOf<Vertex>();

    private readonly int _vertexArray;
    private readonly int _vertexBuffer;
    private readonly int _indexBuffer;
    private readonly int _indexCount;
    private bool _disposed;

    public GpuMesh(MeshData mesh)
    {
        _indexCount = mesh.IndexCount;
        _vertexArray = GL.GenVertexArray();
        _vertexBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();
        try
        {
            GL.BindVertexArray(_vertexArray);

            // Данные читаются прямо из ReadOnlySpan Core на время загрузки — без копии и без хранения span.
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, checked(mesh.VertexCount * VertexSize),
                ref MemoryMarshal.GetReference(mesh.Vertices), BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, checked(mesh.IndexCount * sizeof(uint)),
                ref MemoryMarshal.GetReference(mesh.Indices), BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, VertexSize, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VertexSize, 12);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, VertexSize, 20);

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
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vertexArray);
    }
}
