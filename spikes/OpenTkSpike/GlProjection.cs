using System.Numerics;

namespace OpenTkSpike;

internal static class GlProjection
{
    // Matrix4x4.CreatePerspectiveFieldOfView отображает глубину видимого объема в NDC z ∈ [0, 1] (соглашение Direct3D),
    // а OpenGL по умолчанию ожидает z ∈ [-1, 1]. Без поправки половина диапазона буфера глубины пропадает,
    // а геометрия ближе near-плоскости не отсекается. Поправка z' = 2z - w переводит [0, 1] в [-1, 1].
    private static readonly Matrix4x4 ZeroToOneToMinusOneToOne = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 2f, 0f,
        0f, 0f, -1f, 1f);

    public static Matrix4x4 CreatePerspective(float fieldOfViewRadians, float aspectRatio, float nearPlane, float farPlane) =>
        Matrix4x4.CreatePerspectiveFieldOfView(fieldOfViewRadians, aspectRatio, nearPlane, farPlane) * ZeroToOneToMinusOneToOne;
}
