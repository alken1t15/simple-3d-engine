using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace OpenTkSpike;

internal sealed class ShaderProgram : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    public ShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertex = Compile(ShaderType.VertexShader, vertexSource);
        int fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertex);
        GL.AttachShader(_handle, fragment);
        GL.LinkProgram(_handle);

        GL.DetachShader(_handle, vertex);
        GL.DetachShader(_handle, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = GL.GetProgramInfoLog(_handle);
            GL.DeleteProgram(_handle);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }
    }

    public void Use() => GL.UseProgram(_handle);

    public int GetUniformLocation(string name)
    {
        int location = GL.GetUniformLocation(_handle, name);
        if (location < 0)
            throw new InvalidOperationException($"Uniform '{name}' not found.");
        return location;
    }

    // System.Numerics хранит матрицу построчно (M11, M12, M13, M14, M21, ...) и умножает вектор-строку слева: v' = v * M.
    // GLSL читает те же 16 чисел по столбцам, то есть видит транспонированную матрицу, и умножает вектор-столбец справа: M^T * v.
    // (v * M)^T == M^T * v^T, поэтому матрицу передаем без транспонирования (transpose: false).
    public static void SetMatrix(int location, Matrix4x4 matrix) =>
        GL.UniformMatrix4(location, 1, false, ref matrix.M11);

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteProgram(_handle);
        _disposed = true;
    }

    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compilation failed: {log}");
        }

        return shader;
    }
}
