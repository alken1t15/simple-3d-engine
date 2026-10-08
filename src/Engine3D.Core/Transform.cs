using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Положение, поворот и масштаб одного объекта. Свойства можно менять в каждом кадре на потоке onUpdate;
/// матрица вычисляется из текущих значений.
/// </summary>
public sealed class Transform
{
    public Vector3 Position { get; set; }

    /// <summary>Поворот; не обязан быть единичным — нормализуется при вычислении матрицы.</summary>
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>
    /// Масштаб по осям; отрицательные значения допустимы. Нулевые, слишком малые и слишком большие значения отклоняются
    /// проверкой: матрица нормалей (обратная к линейной части model) должна быть представима во float.
    /// </summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <exception cref="InvalidOperationException">
    /// Компонента не конечна; поворот нулевой; масштаб по какой-либо оси равен нулю или матрица нормалей непредставима.
    /// </exception>
    public void Validate() => GetNormalizedRotation();

    /// <summary>
    /// Model-матрица в соглашении System.Numerics (вектор-строка): scale * rotation * translation —
    /// точка сначала масштабируется, затем поворачивается, затем сдвигается.
    /// </summary>
    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Matrix4x4 GetModelMatrix() =>
        Matrix4x4.CreateScale(Scale)
        * Matrix4x4.CreateFromQuaternion(GetNormalizedRotation())
        * Matrix4x4.CreateTranslation(Position);

    // Проверяет состояние и возвращает единичный поворот.
    private Quaternion GetNormalizedRotation()
    {
        if (!Finite.IsFinite(Position))
            throw new InvalidOperationException($"Transform position {Position} must be finite.");
        if (!Finite.TryNormalize(Rotation, out var rotation))
            throw new InvalidOperationException($"Transform rotation {Rotation} must be a finite non-zero quaternion.");
        if (!Finite.IsFinite(Scale) || Scale.X == 0f || Scale.Y == 0f || Scale.Z == 0f)
            throw new InvalidOperationException($"Transform scale {Scale} must be finite and non-zero on every axis.");

        // Шейдер получает матрицу нормалей как inverse(mat3(model)) во float32 — через определитель, равный
        // произведению масштабов. Он должен быть нормальным числом (не 0, не денормал, не бесконечность),
        // а сама обратная матрица — конечной; иначе освещение объекта превратится в NaN.
        var linear = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(rotation);
        if (!float.IsNormal(Scale.X * Scale.Y * Scale.Z)
            || !Matrix4x4.Invert(linear, out var inverse)
            || !Finite.IsFinite(inverse))
        {
            throw new InvalidOperationException(
                $"Transform scale {Scale} is too small or too large: the normal matrix is not representable in float.");
        }

        return rotation;
    }
}
