using System.Numerics;

namespace Engine3D.OpenGL;

/// <summary>Неизменяемое значение указателя за один update. default пустой; сохранённое значение не меняется.</summary>
public readonly struct PointerSnapshot
{
    private readonly uint _down;
    private readonly uint _pressed;
    private readonly uint _released;

    internal PointerSnapshot(Vector2 position, Vector2 delta, bool isInsideClient, uint down, uint pressed, uint released)
    {
        Position = position;
        Delta = delta;
        IsInsideClient = isInsideClient;
        _down = down;
        _pressed = pressed;
        _released = released;
    }

    /// <summary>От левого верхнего угла client area, X вправо/Y вниз, в логических пикселях. В capture — виртуальная позиция.</summary>
    public Vector2 Position { get; }

    /// <summary>Сумма относительных перемещений за update в тех же единицах; не умножена на время кадра.</summary>
    public Vector2 Delta { get; }

    /// <summary>Position находится внутри текущих логических границ client area; при отсутствии фокуса false.</summary>
    public bool IsInsideClient { get; }

    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемая кнопка.</exception>
    public bool IsDown(MouseButton button) => (_down & Mask(button)) != 0;

    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемая кнопка.</exception>
    public bool WasPressed(MouseButton button) => (_pressed & Mask(button)) != 0;

    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемая кнопка.</exception>
    public bool WasReleased(MouseButton button) => (_released & Mask(button)) != 0;

    internal const uint AllButtons = (1u << 3) - 1;

    internal static uint Mask(MouseButton button)
    {
        if ((uint)button > (uint)MouseButton.Middle)
            throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.");
        return 1u << (int)button;
    }
}
