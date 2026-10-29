namespace Engine3D.OpenGL;

/// <summary>
/// Неизменяемый снимок клавиатуры после обработки событий перед onUpdate.
/// Сохранённый снимок не меняется в следующих кадрах; default означает отсутствие фокуса и ввода.
/// </summary>
public readonly struct InputSnapshot
{
    private readonly uint _down;
    private readonly uint _pressed;
    private readonly uint _released;

    internal InputSnapshot(bool isFocused, uint down, uint pressed, uint released)
    {
        IsFocused = isFocused;
        _down = down;
        _pressed = pressed;
        _released = released;
    }

    public bool IsFocused { get; }

    /// <summary>Клавиша удерживается; при потере фокуса всегда false.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемое значение InputKey.</exception>
    public bool IsDown(InputKey key) => (_down & Mask(key)) != 0;

    /// <summary>Хотя бы одно новое нажатие в текущем интервале событий; автоповтор не учитывается.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемое значение InputKey.</exception>
    public bool WasPressed(InputKey key) => (_pressed & Mask(key)) != 0;

    /// <summary>Хотя бы одно отпускание принятой клавиши в текущем интервале событий.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Неподдерживаемое значение InputKey.</exception>
    public bool WasReleased(InputKey key) => (_released & Mask(key)) != 0;

    internal const uint AllKeys = (1u << 7) - 1;

    internal static uint Mask(InputKey key)
    {
        if ((uint)key > (uint)InputKey.Escape)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported input key.");
        return 1u << (int)key;
    }
}
