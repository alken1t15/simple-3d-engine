using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

internal sealed class ShaderProgram : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    /// <exception cref="InvalidOperationException">Шейдер не скомпилировался или программа не слинковалась.</exception>
    public ShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertex = Compile(ShaderType.VertexShader, vertexSource);
        int fragment;
        try
        {
            fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        }
        catch
        {
            GL.DeleteShader(vertex);
            throw;
        }

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

    /// <exception cref="InvalidOperationException">Uniform не найден (например, удален компилятором как неиспользуемый).</exception>
    public int GetUniformLocation(string name)
    {
        int location = GL.GetUniformLocation(_handle, name);
        return location >= 0 ? location : throw new InvalidOperationException($"Shader uniform '{name}' not found.");
    }

    // System.Numerics хранит матрицу построчно и умножает вектор-строку (v * M); GLSL читает те же 16 чисел по
    // столбцам, то есть видит транспонированную матрицу, и умножает вектор-столбец (M^T * v) — результат тот же.
    // Поэтому transpose: false, а в шейдере порядок projection * view * model * v.
    public static void SetMatrix(int location, Matrix4x4 matrix) => GL.UniformMatrix4(location, 1, false, ref matrix.M11);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GL.DeleteProgram(_handle);
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
