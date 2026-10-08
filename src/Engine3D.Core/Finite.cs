using System.Numerics;

namespace Engine3D.Core;

internal static class Finite
{
    public static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static bool IsFinite(in Matrix4x4 value)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (!float.IsFinite(value[row, column]))
                    return false;
            }
        }

        return true;
    }

    // Нормализация без переполнения и потери точности: сначала делим на наибольшую по модулю компоненту,
    // иначе для очень больших векторов квадрат длины становится бесконечностью, а для очень малых — нулем.
    // Для конечного ненулевого вектора возвращает true и единичный вектор.
    public static bool TryNormalize(Vector3 value, out Vector3 normalized)
    {
        float scale = MaxAbs(value);
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

    // Единичное направление от конечной точки from к конечной точке to; false — точки совпадают.
    // Разность считается напрямую (для конечных float она равна нулю только у равных точек), а если она
    // переполняется — точки по разные стороны от начала координат у границы float, — после деления обеих
    // точек на наибольшую компоненту.
    public static bool TryDirection(Vector3 from, Vector3 to, out Vector3 direction)
    {
        var difference = to - from;
        if (IsFinite(difference))
            return TryNormalize(difference, out direction);

        float scale = MathF.Max(MaxAbs(from), MaxAbs(to));
        return TryNormalize(to / scale - from / scale, out direction);
    }

    private static float MaxAbs(Vector3 value) =>
        MathF.Max(MathF.Abs(value.X), MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));
}
