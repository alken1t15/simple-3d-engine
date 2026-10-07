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

    /// <summary>Масштаб по осям; отрицательные значения допустимы, нулевые — нет (матрица станет необратимой).</summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <exception cref="InvalidOperationException">
    /// Компонента не конечна, поворот нулевой или масштаб по какой-либо оси равен нулю.
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

        return rotation;
    }
}
