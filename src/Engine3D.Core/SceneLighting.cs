using System.Numerics;

namespace Engine3D.Core;

/// <summary>
/// Базовое освещение сцены: фоновый свет и один направленный источник с рассеянной составляющей.
/// Значения можно менять на потоке onUpdate; проверяются перед кадром и сохранением сцены.
/// </summary>
public sealed class SceneLighting
{
    /// <summary>RGB фонового света, компоненты в [0, 1]; нулевой цвет выключает составляющую.</summary>
    public Vector3 AmbientColor { get; set; } = new(0.2f);

    /// <summary>RGB направленного света, компоненты в [0, 1]; нулевой цвет выключает составляющую.</summary>
    public Vector3 DirectionalColor { get; set; } = Vector3.One;

    /// <summary>Направление распространения лучей в мировых координатах; не обязано быть единичным.</summary>
    public Vector3 Direction { get; set; } = new(-1f, -1f, -1f);

    /// <exception cref="InvalidOperationException">
    /// Компонента цвета вне [0, 1] или не конечна; направление нулевое или не конечно.
    /// </exception>
    public void Validate() => GetNormalizedDirection();

    /// <summary>Проверяет освещение и возвращает единичное направление лучей.</summary>
    /// <exception cref="InvalidOperationException">Состояние не проходит <see cref="Validate"/>.</exception>
    public Vector3 GetNormalizedDirection()
    {
        if (!IsUnitColor(AmbientColor))
            throw new InvalidOperationException($"Ambient color {AmbientColor} must have components in [0, 1].");
        if (!IsUnitColor(DirectionalColor))
            throw new InvalidOperationException($"Directional color {DirectionalColor} must have components in [0, 1].");
        if (!Finite.TryNormalize(Direction, out var direction))
            throw new InvalidOperationException($"Light direction {Direction} must be finite and non-zero.");

        return direction;
    }

    private static bool IsUnitColor(Vector3 color) =>
        new ColorRgba(color.X, color.Y, color.Z).IsValid;
}
