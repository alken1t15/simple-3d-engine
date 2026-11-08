using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using NativeMonitor = OpenTK.Windowing.GraphicsLibraryFramework.Monitor;

namespace Engine3D.OpenGL;

/// <summary>Нативные hints и страховка для WM, игнорирующих size hints.</summary>
internal sealed class WindowSizeController(NativeWindow window, EngineOptions options)
{
    private ClientSizeLimits? applied;
    private (int Width, int Height) windowedSize = (options.Width, options.Height);
    private (int X, int Y) windowedPosition;
    private (int Width, int Height)? pendingRestore;
    private bool restoreConfigured;

    internal void OnResize(ResizeEventArgs args)
    {
        if (pendingRestore is not { } target) return;
        if ((args.Width, args.Height) == target) pendingRestore = null;
        else restoreConfigured = true;
    }
    internal ClientSizeLimits WindowedLimits { get; private set; } = WindowSizePolicy.Calculate(
        options.MinimumClientSize, int.MaxValue, int.MaxValue);
    internal ClientSizeLimits Limits { get; private set; } = WindowSizePolicy.Calculate(
        options.MinimumClientSize, int.MaxValue, int.MaxValue);

    internal unsafe bool IsFullscreen => GLFW.GetWindowMonitor(window.WindowPtr) != null;

    private unsafe NativeMonitor* CurrentMonitor()
    {
        var attached = GLFW.GetWindowMonitor(window.WindowPtr);
        if (attached != null) return attached;
        return GLFW.GetPlatform() == Platform.Wayland ? GLFW.GetPrimaryMonitor()
            : Monitors.GetMonitorFromWindow(window).Handle.ToUnsafePtr<NativeMonitor>();
    }

    internal unsafe (int Width, int Height) FullscreenSize
    {
        get
        {
            var monitor = CurrentMonitor();
            if (monitor == null) throw new InvalidOperationException("No monitor is available for fullscreen mode.");
            var mode = GLFW.GetVideoMode(monitor);
            if (mode == null) throw new InvalidOperationException("No desktop video mode is available.");
            float sx = 1, sy = 1;
            if (GLFW.GetPlatform() == Platform.Wayland)
                GLFW.GetMonitorContentScale(monitor, out sx, out sy);
            return (Math.Max(1, (int)(mode->Width / Math.Max(1, sx))),
                Math.Max(1, (int)(mode->Height / Math.Max(1, sy))));
        }
    }

    internal unsafe void SetFullscreen(bool fullscreen)
    {
        if (fullscreen == IsFullscreen) return;
        if (fullscreen)
        {
            var monitor = CurrentMonitor();
            if (monitor == null) throw new InvalidOperationException("No monitor is available for fullscreen mode.");
            var mode = GLFW.GetVideoMode(monitor);
            if (mode == null) throw new InvalidOperationException("No desktop video mode is available.");
            var size = window.ClientSize;
            windowedSize = size.X > 0 && size.Y > 0 ? (size.X, size.Y) : windowedSize;
            if (GLFW.GetPlatform() != Platform.Wayland)
                GLFW.GetWindowPos(window.WindowPtr, out windowedPosition.X, out windowedPosition.Y);
            // Текущий desktop video mode: не выбираем произвольную частоту/другое разрешение монитора.
            GLFW.SetWindowMonitor(window.WindowPtr, monitor, 0, 0, mode->Width, mode->Height, mode->RefreshRate);
        }
        else
        {
            pendingRestore = windowedSize;
            restoreConfigured = false;
            GLFW.SetWindowMonitor(window.WindowPtr, null, windowedPosition.X, windowedPosition.Y,
                windowedSize.Width, windowedSize.Height, GLFW.DontCare);
        }
        applied = null;
        Refresh();
    }

    internal unsafe void Refresh()
    {
        if (IsFullscreen)
        {
            var fullscreenSize = FullscreenSize;
            Limits = new(fullscreenSize.Width, fullscreenSize.Height, fullscreenSize.Width, fullscreenSize.Height);
            return; // Рамка/панели/оконный minimum не ограничивают полноэкранную поверхность.
        }
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
        WindowedLimits = Limits;
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
        if (pendingRestore is { } restored && restoreConfigured)
        {
            restoreConfigured = false;
            var restoreTarget = WindowSizePolicy.Clamp(Limits, restored.Width, restored.Height);
            pendingRestore = restoreTarget;
            window.ClientSize = new Vector2i(restoreTarget.Width, restoreTarget.Height);
        }
        var size = window.ClientSize;
        if (size.X <= 0 || size.Y <= 0) return;
        var target = WindowSizePolicy.Clamp(Limits, size.X, size.Y);
        if (target != (size.X, size.Y)) window.ClientSize = new Vector2i(target.Width, target.Height);
    }

    internal unsafe void SetSize(int width, int height)
    {
        if (IsFullscreen) throw new InvalidOperationException("SetClientSize requires windowed mode; call SetFullscreen(false) first.");
        pendingRestore = null; // Явный новый размер имеет приоритет над восстановлением.
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
        IsFullscreen || GLFW.GetWindowAttrib(window.WindowPtr, WindowAttributeGetBool.Maximized)
        || (width >= Limits.MinimumWidth && width <= Limits.MaximumWidth
            && height >= Limits.MinimumHeight && height <= Limits.MaximumHeight);
}
