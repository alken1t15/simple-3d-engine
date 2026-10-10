using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

internal static class GlErrors
{
    // glGetError хранит флаги ошибок до чтения, поэтому одной проверки на этап (кадр, загрузку ресурса)
    // достаточно, чтобы ни одна ошибка не прошла незамеченной.
    public static void ThrowIfAny(string operation)
    {
        List<ErrorCode>? errors = null;
        for (var error = GL.GetError(); error != ErrorCode.NoError; error = GL.GetError())
            (errors ??= []).Add(error);

        if (errors is not null)
            throw new InvalidOperationException($"OpenGL error while {operation}: {string.Join(", ", errors)}.");
    }
}
