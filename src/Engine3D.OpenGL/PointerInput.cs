using System.Numerics;

namespace Engine3D.OpenGL;

/// <summary>CPU-накопитель указателя; события поступают только с потока окна.</summary>
internal sealed class PointerInput
{
    private bool _focused;
    private bool _suppressNextMotion = true;
    private Vector2 _position;
    private Vector2 _delta;
    private uint _down;
    private uint _pressed;
    private uint _released;
    private uint _blocked;

    internal PointerSnapshot Snapshot(int clientWidth, int clientHeight) => !_focused ? default
        : new(_position, _delta, _position.X >= 0f && _position.Y >= 0f
            && _position.X < clientWidth && _position.Y < clientHeight, _down, _pressed, _released);

    internal void BeginFrame()
    {
        _pressed = _released = 0;
        _delta = Vector2.Zero;
    }

    internal void SetFocus(bool focused)
    {
        if (_focused == focused) return;
        _focused = focused;
        _down = _pressed = _released = 0;
        _blocked = focused ? PointerSnapshot.AllButtons : 0;
        ResetMotion();
    }

    internal void ResetMotion()
    {
        _delta = Vector2.Zero;
        _suppressNextMotion = true;
    }

    // GLFW уже отдаёт client logical coordinates, без умножения/деления на framebuffer DPI.
    internal void SynchronizePosition(Vector2 position) => _position = position;

    internal void Move(Vector2 position, Vector2 delta)
    {
        _position = position;
        if (!_focused) return;
        if (!_suppressNextMotion) _delta += delta;
        _suppressNextMotion = false;
    }

    internal void ReconcileHeld(uint held) => _blocked &= held;

    internal void ButtonDown(MouseButton button)
    {
        uint mask = PointerSnapshot.Mask(button);
        if (!_focused || (_blocked & mask) != 0 || (_down & mask) != 0) return;
        _down |= mask;
        _pressed |= mask;
    }

    internal void ButtonUp(MouseButton button)
    {
        uint mask = PointerSnapshot.Mask(button);
        _blocked &= ~mask;
        if (!_focused || (_down & mask) == 0) return;
        _down &= ~mask;
        _released |= mask;
    }
}
