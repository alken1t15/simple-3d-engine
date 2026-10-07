using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Перспективная камера. Параметры проверяются не при присваивании, а при вычислении матриц
/// и в <see cref="Validate"/>: так near/far можно менять в любом порядке.
/// </summary>
/// <remarks>
/// Матрицы — в соглашении System.Numerics: правая система координат, камера смотрит вдоль −Z пространства вида,
/// глубина проекции в NDC — [0, 1]. Перевод в соглашение OpenGL ([−1, 1]) выполняет renderer.
/// </remarks>
public sealed class Camera
{
    // Синус минимального угла между Up и направлением взгляда: при меньшем угле базис камеры вырождается.
    private const float MinUpSine = 1e-6f;

    public Vector3 Position { get; set; }

    /// <summary>Точка, на которую смотрит камера; по умолчанию камера в начале координат смотрит вдоль −Z.</summary>
    public Vector3 Target { get; set; } = -Vector3.UnitZ;

    public Vector3 Up { get; set; } = Vector3.UnitY;

    public float FieldOfViewDegrees { get; set; } = 60f;

    public float NearPlane { get; set; } = 0.1f;

    public float FarPlane { get; set; } = 100f;

    /// <exception cref="InvalidOperationException">
    /// Значения не конечны; Position совпадает с Target; Up нулевой или параллелен направлению взгляда;
    /// угол обзора вне (0, 180); не выполнено 0 &lt; NearPlane &lt; FarPlane.
    /// </exception>
    public void Validate()
    {
        if (!Finite.IsFinite(Position) || !Finite.IsFinite(Target) || !Finite.IsFinite(Up))
            throw new InvalidOperationException($"Camera position {Position}, target {Target} and up {Up} must be finite.");
        if (!Finite.TryNormalize(Target - Position, out var forward))
            throw new InvalidOperationException($"Camera position and target must differ, both are {Position}.");
        if (!Finite.TryNormalize(Up, out var up))
            throw new InvalidOperationException("Camera up vector must be non-zero.");
        if (Vector3.Cross(forward, up).Length() < MinUpSine)
            throw new InvalidOperationException($"Camera up vector {Up} must not be parallel to the view direction.");
        if (!(FieldOfViewDegrees > 0f && FieldOfViewDegrees < 180f))
            throw new InvalidOperationException($"Camera field of view must be in (0, 180) degrees, got {FieldOfViewDegrees}.");
        if (!(NearPlane > 0f && float.IsFinite(NearPlane)))
            throw new InvalidOperationException($"Camera near plane must be positive and finite, got {NearPlane}.");
        if (!(FarPlane > NearPlane && float.IsFinite(FarPlane)))
            throw new InvalidOperationException($"Camera far plane must be finite and greater than near plane {NearPlane}, got {FarPlane}.");
    }

    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Matrix4x4 GetViewMatrix()
    {
        Validate();
        return Matrix4x4.CreateLookAt(Position, Target, Up);
    }

    /// <param name="aspectRatio">Отношение ширины области вывода к высоте.</param>
    /// <exception cref="ArgumentOutOfRangeException">Соотношение сторон не положительно или не конечно.</exception>
    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        if (!(aspectRatio > 0f && float.IsFinite(aspectRatio)))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio), aspectRatio, "Aspect ratio must be positive and finite.");

        Validate();
        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfViewDegrees * (MathF.PI / 180f), aspectRatio, NearPlane, FarPlane);
    }
}
