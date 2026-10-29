namespace Engine3D.OpenGL;

/// <summary>CPU-состояние событий. Не хранит окно и не обращается к GLFW.</summary>
internal sealed class KeyboardInput
{
    private bool _focused;
    private uint _down;
    private uint _pressed;
    private uint _released;
    private uint _blocked;

    internal InputSnapshot Snapshot => new(_focused, _down, _pressed, _released);

    internal void BeginFrame() => _pressed = _released = 0;

    internal void SetFocus(bool focused)
    {
        if (_focused == focused)
            return;
        _focused = focused;
        _down = _pressed = _released = 0;
        // Не принимаем удержания из другого окна или из первого пакета событий активации.
        // После обработки пакета ReconcileHeld разблокирует уже отпущенные клавиши.
        _blocked = focused ? InputSnapshot.AllKeys : 0;
    }

    internal void ReconcileHeld(uint held) => _blocked &= held;

    internal void KeyDown(InputKey key, bool isRepeat)
    {
        uint mask = InputSnapshot.Mask(key);
        if (!_focused || isRepeat || (_blocked & mask) != 0 || (_down & mask) != 0)
            return;
        _down |= mask;
        _pressed |= mask;
    }

    internal void KeyUp(InputKey key)
    {
        uint mask = InputSnapshot.Mask(key);
        _blocked &= ~mask;
        if (!_focused || (_down & mask) == 0)
            return;
        _down &= ~mask;
        _released |= mask;
    }
}
