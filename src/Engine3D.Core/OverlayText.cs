using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Одна строка ASCII. Position — левый верх строки, PixelHeight — высота строки вместе с полями atlas
/// в логических пикселях. Непечатные и неподдерживаемые Unicode-символы заменяются вопросительным знаком.
/// </summary>
public readonly record struct OverlayText(Vector2 Position, string Text, float PixelHeight, ColorRgba Color);
