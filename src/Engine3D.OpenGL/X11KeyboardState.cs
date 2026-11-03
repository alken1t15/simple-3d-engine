using System.Runtime.InteropServices;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Engine3D.OpenGL;

/// <summary>Физический keymap X11 для rearm после focus gain. Не владеет Display и не создаёт второе соединение.</summary>
internal static class X11KeyboardState
{
    internal static unsafe bool TryReadHeldKeys(ReadOnlySpan<(Keys Native, InputKey Key)> keys, out uint held)
    {
        held = 0;
        if (!OperatingSystem.IsLinux() || GLFW.GetPlatform() != Platform.X11) return false;

        nint display = GLFW.GetX11Display();
        if (display == 0) throw new InvalidOperationException("GLFW X11 display is unavailable for keyboard rearm.");
        byte* keymap = stackalloc byte[32];
        XQueryKeymap(display, keymap);
        foreach (var (native, key) in keys)
        {
            int code = GLFW.GetKeyScancode(native);
            if ((uint)code < 256 && (keymap[code >> 3] & (1 << (code & 7))) != 0)
                held |= InputSnapshot.Mask(key);
        }
        return true;
    }

    [DllImport("libX11.so.6", ExactSpelling = true)]
    private static extern unsafe int XQueryKeymap(nint display, byte* keysReturn);
}
