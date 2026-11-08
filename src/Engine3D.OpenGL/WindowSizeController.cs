using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Engine3D.OpenGL;

/// <summary>Нативные hints и страховка для WM, игнорирующих size hints.</summary>
internal sealed class WindowSizeController(NativeWindow window, EngineOptions options)
{
    private ClientSizeLimits? applied;
    internal ClientSizeLimits Limits { get; private set; } = WindowSizePolicy.Calculate(
        options.MinimumClientSize, int.MaxValue, int.MaxValue);

    internal unsafe void Refresh()
    {
        int width = int.MaxValue, height = int.MaxValue;
        if (options.LimitToMonitorWorkArea)
        {
            bool wayland = GLFW.GetPlatform() == Platform.Wayland;
            // Wayland запрещает запрос глобального положения окна. Не вызываем GetMonitorFromWindow.
            var monitors = wayland ? Monitors.GetMonitors() : [Monitors.GetMonitorFromWindow(window)];
            foreach (var monitor in monitors)
            {
                var area = monitor.WorkArea;
                // GLFW 3.4 Wayland возвращает physical video mode, остальные backend — screen units.
                float sx = wayland ? Math.Max(1f, monitor.HorizontalScale) : 1f;
                float sy = wayland ? Math.Max(1f, monitor.VerticalScale) : 1f;
                width = Math.Min(width, (int)(area.Size.X / sx));
                height = Math.Min(height, (int)(area.Size.Y / sy));
            }
            GLFW.GetWindowFrameSize(window.WindowPtr, out int left, out int top, out int right, out int bottom);
            width = Math.Max(1, width - left - right);
            height = Math.Max(1, height - top - bottom);
        }
        Limits = WindowSizePolicy.Calculate(options.MinimumClientSize, width, height);
        if (applied != Limits)
        {
            GLFW.SetWindowSizeLimits(window.WindowPtr, Limits.MinimumWidth, Limits.MinimumHeight,
                Limits.MaximumWidth == int.MaxValue ? GLFW.DontCare : Limits.MaximumWidth,
                Limits.MaximumHeight == int.MaxValue ? GLFW.DontCare : Limits.MaximumHeight);
            applied = Limits;
        }
        // Не восстанавливаем свёрнутое окно и не спорим с WM о maximize/fullscreen.
        if (GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Iconified)
            || GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Maximized)) return;
        var size = window.ClientSize;
        if (size.X <= 0 || size.Y <= 0) return;
        var target = WindowSizePolicy.Clamp(Limits, size.X, size.Y);
        if (target != (size.X, size.Y)) window.ClientSize = new Vector2i(target.Width, target.Height);
    }

    internal unsafe void SetSize(int width, int height)
    {
        // NativeWindow.WindowState хранит последнее программное значение, а не состояние кнопок WM.
        if (GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Iconified)
            || GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Maximized))
        {
            window.WindowState = WindowState.Normal;
            // Windows восстанавливает минимизированное maximized-окно сначала в maximized.
            if (GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Maximized))
                window.WindowState = WindowState.Normal;
        }
        Refresh();
        var target = WindowSizePolicy.Clamp(Limits, width, height);
        window.ClientSize = new Vector2i(target.Width, target.Height);
    }

    // Refresh может прийти из native resize callback до возврата в RunLoop.
    // Не рисуем промежуточный размер, вышедший за hints (например XResizeWindow без WM).
    internal unsafe bool CanRender(int width, int height) =>
        GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Maximized)
        || (width >= Limits.MinimumWidth && width <= Limits.MaximumWidth
            && height >= Limits.MinimumHeight && height <= Limits.MaximumHeight);
}
