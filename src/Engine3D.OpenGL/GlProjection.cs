using System.Numerics;

namespace Engine3D.OpenGL;

internal static class GlProjection
{
    // Core строит проекцию в соглашении System.Numerics: глубина NDC в [0, 1]. OpenGL ожидает [−1, 1],
    // поэтому справа умножаем на C (z' = 2z − w). Поправка применяется ровно один раз — только здесь.
    private static readonly Matrix4x4 DepthToOpenGl = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);

    public static Matrix4x4 ToOpenGl(in Matrix4x4 projection) => projection * DepthToOpenGl;
}
