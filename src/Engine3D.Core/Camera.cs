using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Перспективная камера. Параметры проверяются не при присваивании, а при вычислении матриц
/// и в <see cref="Validate"/>: так near/far можно менять в любом порядке.
/// </summary>
/// <remarks>
/// Матрицы — в соглашении System.Numerics: правая система координат, камера смотрит вдоль −Z пространства вида,
/// глубина проекции в NDC — [0, 1]. Перевод в соглашение OpenGL ([−1, 1]) выполняет renderer.
/// Длина <see cref="Up"/> и расстояние до <see cref="Target"/> на ориентацию камеры не влияют.
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

    /// <summary>
    /// Проверяет камеру теми же вычислениями, что и матричные методы: после успешной проверки
    /// <see cref="GetViewMatrix"/> возвращает конечную матрицу, а <see cref="GetProjectionMatrix"/> — конечную матрицу
    /// для любого соотношения сторон, при котором масштаб по ширине представим во float.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Значения не конечны; Position совпадает с Target; Up нулевой или параллелен направлению взгляда;
    /// угол обзора вне (0, 180); не выполнено 0 &lt; NearPlane &lt; FarPlane; матрица вида или коэффициенты
    /// проекции непредставимы во float.
    /// </exception>
    public void Validate()
    {
        BuildViewMatrix();
        GetProjectionScales();
    }

    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Matrix4x4 GetViewMatrix()
    {
        var view = BuildViewMatrix();
        GetProjectionScales(); // камера проверяется целиком, как в Validate
        return view;
    }

    /// <param name="aspectRatio">Отношение ширины области вывода к высоте.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Соотношение сторон не положительно, не конечно или так мало либо велико, что масштаб по ширине непредставим.
    /// </exception>
    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        if (!(aspectRatio > 0f && float.IsFinite(aspectRatio)))
            throw new ArgumentOutOfRangeException(nameof(aspectRatio), aspectRatio, "Aspect ratio must be positive and finite.");

        BuildViewMatrix(); // камера проверяется целиком, как в Validate
        var (height, range) = GetProjectionScales();

        float width = height / aspectRatio;
        if (!float.IsNormal(width))
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio), aspectRatio,
                $"Aspect ratio gives a projection width scale {width} that is not representable.");
        }

        // Те же коэффициенты, что у Matrix4x4.CreatePerspectiveFieldOfView: правая система, глубина NDC в [0, 1].
        return new Matrix4x4(
            width, 0f, 0f, 0f,
            0f, height, 0f, 0f,
            0f, 0f, range, -1f,
            0f, 0f, range * NearPlane, 0f);
    }

    // Поворот строится из устойчиво нормализованных направлений, сдвиг — отдельно. Поэтому длина Up и расстояние
    // до Target не влияют на результат, а нормализация внутри CreateLookAt работает уже с единичными векторами.
    private Matrix4x4 BuildViewMatrix()
    {
        if (!Finite.IsFinite(Position) || !Finite.IsFinite(Target) || !Finite.IsFinite(Up))
            throw new InvalidOperationException($"Camera position {Position}, target {Target} and up {Up} must be finite.");
        if (!Finite.TryDirection(Position, Target, out var forward))
            throw new InvalidOperationException($"Camera position and target must differ, both are {Position}.");
        if (!Finite.TryNormalize(Up, out var up))
            throw new InvalidOperationException("Camera up vector must be non-zero.");
        if (Vector3.Cross(forward, up).Length() < MinUpSine)
            throw new InvalidOperationException($"Camera up vector {Up} must not be parallel to the view direction.");

        var view = Matrix4x4.CreateTranslation(-Position) * Matrix4x4.CreateLookAt(Vector3.Zero, forward, up);
        if (!Finite.IsFinite(view))
        {
            throw new InvalidOperationException(
                $"Camera view matrix is not representable in float: position {Position} is too far from the origin.");
        }

        return view;
    }

    // Масштаб по высоте и коэффициент глубины — как в Matrix4x4.CreatePerspectiveFieldOfView, но с проверкой результата:
    // крошечный угол обзора обращается в 0 радиан или дает бесконечный масштаб, а очень близкие near/far —
    // бесконечный коэффициент глубины.
    private (float Height, float Range) GetProjectionScales()
    {
        if (!(FieldOfViewDegrees > 0f && FieldOfViewDegrees < 180f))
            throw new InvalidOperationException($"Camera field of view must be in (0, 180) degrees, got {FieldOfViewDegrees}.");
        if (!(NearPlane > 0f && float.IsFinite(NearPlane)))
            throw new InvalidOperationException($"Camera near plane must be positive and finite, got {NearPlane}.");
        if (!(FarPlane > NearPlane && float.IsFinite(FarPlane)))
            throw new InvalidOperationException($"Camera far plane must be finite and greater than near plane {NearPlane}, got {FarPlane}.");

        float fieldOfView = FieldOfViewDegrees * (MathF.PI / 180f);
        float height = 1f / MathF.Tan(fieldOfView * 0.5f);
        if (!(fieldOfView > 0f && fieldOfView < MathF.PI && height > 0f && float.IsNormal(height)))
        {
            throw new InvalidOperationException(
                $"Camera field of view {FieldOfViewDegrees} degrees is not representable in a projection matrix.");
        }

        float range = FarPlane / (NearPlane - FarPlane);
        if (!float.IsFinite(range) || !float.IsFinite(range * NearPlane))
        {
            throw new InvalidOperationException(
                $"Camera near plane {NearPlane} and far plane {FarPlane} are too close to build a depth range.");
        }

        return (height, range);
    }
}
