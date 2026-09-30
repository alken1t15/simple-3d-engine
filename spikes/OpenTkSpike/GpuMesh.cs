using OpenTK.Graphics.OpenGL4;

namespace OpenTkSpike;

// GPU-представление одного меша: VAO + вершинный и индексный буферы.
// Один экземпляр рисуется многими объектами с разными model-матрицами.
internal sealed class GpuMesh : IDisposable
{
    private readonly int _vertexArray;
    private readonly int _vertexBuffer;
    private readonly int _indexBuffer;
    private readonly int _indexCount;
    private bool _disposed;

    public GpuMesh(Vertex[] vertices, uint[] indices)
    {
        _indexCount = indices.Length;

        _vertexArray = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArray);

        _vertexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * Vertex.SizeInBytes, vertices, BufferUsageHint.StaticDraw);

        _indexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Vertex.SizeInBytes, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Vertex.SizeInBytes, 3 * sizeof(float));

        GL.BindVertexArray(0);
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

        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        _disposed = true;
    }
}
