namespace Engine3D.Core;

/// <summary>Цвет RGBA; допустимые значения компонент — конечные числа в диапазоне [0, 1].</summary>
public readonly record struct ColorRgba(float R, float G, float B, float A = 1f)
{
    internal bool IsValid => IsUnit(R) && IsUnit(G) && IsUnit(B) && IsUnit(A);

    // Для NaN оба сравнения ложны, поэтому NaN тоже не проходит проверку.
    private static bool IsUnit(float value) => value >= 0f && value <= 1f;
}
