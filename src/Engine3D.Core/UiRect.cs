namespace Engine3D.Core;

/// <summary>Прямоугольник в логических пикселях client area: начало слева сверху, Y вниз.</summary>
public readonly record struct UiRect(float X, float Y, float Width, float Height);
