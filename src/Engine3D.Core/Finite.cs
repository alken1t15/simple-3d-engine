using System.Numerics;

namespace Engine3D.Core;

internal static class Finite
{
    public static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    // Нормализация без переполнения и потери точности: сначала делим на наибольшую по модулю компоненту,
    // иначе для очень больших векторов квадрат длины становится бесконечностью, а для очень малых — нулем.
    // Для конечного ненулевого вектора возвращает true и единичный вектор.
    public static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        float scale = MathF.Max(MathF.Abs(value.X), MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));
        if (!float.IsFinite(scale) || scale == 0f)
        {
            normalized = default;
            return false;
        }

        normalized = Vector3.Normalize(value / scale);
        return true;
    }

    // То же для кватерниона поворота: для конечного ненулевого — true и единичный кватернион.
    public static bool TryNormalize(Quaternion value, out Quaternion normalized)
    {
        float scale = MathF.Max(
            MathF.Max(MathF.Abs(value.X), MathF.Abs(value.Y)),
            MathF.Max(MathF.Abs(value.Z), MathF.Abs(value.W)));
        if (!float.IsFinite(scale) || scale == 0f)
        {
            normalized = default;
            return false;
        }

        // Покомпонентно: у Quaternion оператор / — деление кватернионов, а умножение на 1/scale переполнится при малом scale.
        normalized = Quaternion.Normalize(new Quaternion(value.X / scale, value.Y / scale, value.Z / scale, value.W / scale));
        return true;
    }
}
