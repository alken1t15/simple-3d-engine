using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using GlfwErrorCode = OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode;

namespace Engine3D.OpenGL;

/// <summary>
/// Окно с контекстом OpenGL 3.3 core и цикл показа сцены. Единственный владелец GPU-ресурсов: освобождает их,
/// окно и контекст в <see cref="Dispose"/>. Все методы вызываются из потока, на котором вызван <see cref="Create"/>;
/// сцену и камеру изменяют на том же потоке — в callback onUpdate.
/// </summary>
public sealed class Engine : IDisposable
{
    private enum State
    {
        Created,
        Running,
        Finished,
        Disposed,
    }

    private static readonly Lock s_glfwErrorLock = new();
    private static readonly GLFWCallbacks.ErrorCallback s_glfwErrorCallback = OnGlfwError;
    private static string? s_glfwError;

    private readonly NativeWindow _window;
    private readonly SceneRenderer _renderer;
    private readonly FramePreparation _frame = new();
    private readonly int _ownerThreadId;
    private Scene? _scene;
    private Camera? _camera;
    private State _state;
    private bool _closeRequested;
    private bool _inUpdate;
    private bool _runFailed;
    private ExceptionDispatchInfo? _callbackFailure;

    static Engine()
    {
        // По умолчанию OpenTK бросает исключение прямо из нативного колбэка ошибок GLFW: через нативные кадры
        // оно не проходит и роняет процесс. Ошибка запоминается и сообщается из управляемого кода.
        GLFWProvider.SetErrorCallback(s_glfwErrorCallback);
    }

    private Engine(NativeWindow window)
    {
        _window = window;
        _renderer = new SceneRenderer(); // шейдер собирается при создании: ошибка — до возврата Engine
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _window.Refresh += OnRefresh;
    }

    /// <summary>
    /// Число показанных кадров последнего <see cref="Run"/>: обнуляется перед ним, увеличивается после каждого
    /// показанного кадра (включая перерисовку по запросу системы) и доступно после возврата и после Dispose.
    /// Не равно числу вызовов onUpdate.
    /// </summary>
    public long RenderedFrames { get; private set; }

    /// <summary>Число GPU-мешей в кэше: проверка того, что общий MeshData загружен на GPU один раз.</summary>
    internal int GpuMeshCount => _renderer.MeshCount;

    /// <summary>
    /// Только для проверок: вызывается после отрисовки кадра и до его показа с размером кадрового буфера и пикселями
    /// RGBA8 (строки снизу вверх, как отдает OpenGL).
    /// </summary>
    internal Action<int, int, byte[]>? FrameRendered { get; set; }

    /// <summary>Создает скрытое окно с контекстом OpenGL 3.3 core на вызывающем потоке.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> равен null.</exception>
    /// <exception cref="ArgumentException">Ширина или высота не положительны либо заголовок пустой.</exception>
    /// <exception cref="InvalidOperationException">
    /// Окно или контекст создать не удалось; частично созданные ресурсы освобождаются.
    /// </exception>
    public static Engine Create(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        TakeGlfwError(); // ошибка, оставшаяся от предыдущего Engine, к этому вызову не относится
        NativeWindow? window = null;
        try
        {
            window = new NativeWindow(new NativeWindowSettings
            {
                ClientSize = new Vector2i(options.Width, options.Height),
                Title = options.Title,
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                StartVisible = false,
            });
            window.VSync = options.VSync ? VSyncMode.On : VSyncMode.Off;
            ThrowIfGlfwError("creating the window");
            GlErrors.ThrowIfAny("creating the OpenGL context");

            return new Engine(window);
        }
        catch (Exception exception)
        {
            window?.Dispose();

            // Если GLFW сообщил причину, она первична: исключение OpenTK тогда лишь следствие (например, пустое окно).
            string reason = TakeGlfwError() is { } glfwError ? $"GLFW reported {glfwError}." : exception.Message;
            throw new InvalidOperationException($"Failed to create an OpenGL 3.3 core window: {reason}", exception);
        }
    }

    /// <summary>
    /// Показывает сцену до закрытия окна или <see cref="RequestClose"/>, блокируя вызывающий поток. Каждый кадр:
    /// события окна → <paramref name="onUpdate"/> → проверка сцены и камеры → рендер → показ → счетчик кадров.
    /// </summary>
    /// <param name="onUpdate">
    /// Логика приложения: получает реальное время в секундах с прошлого вызова (в первый раз — 0) и может менять
    /// сцену, камеру и свет. Исключение из callback прерывает Run и выходит наружу без изменений.
    /// </param>
    /// <exception cref="ObjectDisposedException">Engine уже освобожден.</exception>
    /// <exception cref="ArgumentNullException">Сцена или камера равна null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Run уже вызывался для этого Engine; вызов не с потока Create; ошибка OpenGL или GLFW во время показа.
    /// </exception>
    public void Run(Scene scene, Camera camera, Action<float>? onUpdate = null)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        if (_state != State.Created)
            throw new InvalidOperationException("Run can be called only once per Engine; create a new Engine to show a scene again.");

        _state = State.Running;
        RenderedFrames = 0;
        _scene = scene;
        _camera = camera;
        try
        {
            RunLoop(onUpdate);
        }
        catch
        {
            _runFailed = true;
            throw;
        }
        finally
        {
            _state = State.Finished;
            _scene = null;
            _camera = null;
            _window.IsVisible = false;
        }
    }

    /// <summary>
    /// Просит завершить <see cref="Run"/> при ближайшей обработке событий; если вызван из onUpdate, следующий кадр
    /// уже не показывается. Ресурсы не освобождает. Повторный вызов и вызов после завершения Run безопасны.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Engine уже освобожден.</exception>
    /// <exception cref="InvalidOperationException">Run еще не вызывался или вызов не с потока Create.</exception>
    public void RequestClose()
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state == State.Created)
            throw new InvalidOperationException("RequestClose is valid only while Run is executing.");

        if (_state == State.Running)
            _closeRequested = true;
    }

    /// <summary>
    /// Освобождает GPU-ресурсы, окно и контекст. Повторный вызов безопасен. Ошибка освобождения сообщается,
    /// только если Run не завершился ошибкой раньше: исходная ошибка Run не заменяется.
    /// </summary>
    /// <exception cref="InvalidOperationException">Вызов из onUpdate или не с потока Create.</exception>
    public void Dispose()
    {
        if (_state == State.Disposed)
            return;

        ThrowIfWrongThread();
        if (_inUpdate)
            throw new InvalidOperationException("Dispose cannot be called from onUpdate; call RequestClose and dispose the Engine after Run returns.");

        // GPU-ресурсы удаляются, пока контекст окна еще жив; затем уничтожаются окно и контекст.
        var errors = new List<Exception>();
        _renderer.Release(errors);
        try
        {
            _window.Refresh -= OnRefresh;
            _window.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        _state = State.Disposed;
        if (errors.Count > 0 && !_runFailed)
            throw new InvalidOperationException("Failed to release the window and OpenGL resources.", new AggregateException(errors));
    }

    private void RunLoop(Action<float>? onUpdate)
    {
        _window.IsVisible = true;
        var clock = Stopwatch.StartNew();
        double? previousUpdate = null;

        while (true)
        {
            _window.NewInputFrame();
            NativeWindow.ProcessWindowEvents(waitForEvents: false);
            ThrowIfGlfwError("processing window events");
            _callbackFailure?.Throw();
            if (_closeRequested || _window.IsExiting)
                break;

            double now = clock.Elapsed.TotalSeconds;
            float deltaSeconds = previousUpdate is { } previous ? (float)(now - previous) : 0f;
            previousUpdate = now;

            if (onUpdate is not null)
            {
                _inUpdate = true;
                try
                {
                    onUpdate(deltaSeconds);
                }
                finally
                {
                    _inUpdate = false;
                }
            }

            if (_closeRequested)
                break; // закрытие запрошено из onUpdate: новый кадр не показывается

            DrawFrame();
        }
    }

    private void DrawFrame()
    {
        // Размер в пикселях кадрового буфера, а не логический размер окна: на HiDPI они различаются.
        // У свернутого окна буфер нулевой — кадр не рисуется и не считается.
        var size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        // Сначала CPU: проверка состояния после onUpdate и матрицы — до любых GL-вызовов кадра.
        _frame.Prepare(_scene!, _camera!, size.X, size.Y);
        _renderer.Render(_scene!, _frame, size.X, size.Y);
        if (FrameRendered is { } frameRendered)
            frameRendered(size.X, size.Y, ReadFramebuffer(size.X, size.Y));
        GlErrors.ThrowIfAny($"rendering frame {RenderedFrames + 1}");

        _window.Context.SwapBuffers();
        RenderedFrames++;
    }

    // На macOS и Windows, пока пользователь тянет край окна, система держит свой цикл событий внутри
    // ProcessWindowEvents, и основной цикл стоит; перерисовку она запрашивает через этот обработчик
    // (последнее состояние, без onUpdate). Обработчик вызывается из нативного кода GLFW: исключение нельзя
    // пропускать через нативные кадры — оно запоминается, окно закрывается, а ошибка бросается после цикла.
    private void OnRefresh()
    {
        if (_state != State.Running || _inUpdate || _callbackFailure is not null)
            return;

        try
        {
            DrawFrame();
        }
        catch (Exception exception)
        {
            _callbackFailure = ExceptionDispatchInfo.Capture(exception);
            _window.Close();
        }
    }

    private static byte[] ReadFramebuffer(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        return pixels;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_state == State.Disposed, this);

    private void ThrowIfWrongThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Engine methods must be called on the thread that created the Engine.");
    }

    private static void OnGlfwError(GlfwErrorCode code, string description)
    {
        lock (s_glfwErrorLock)
            s_glfwError ??= $"{code}: {description}";
    }

    private static string? TakeGlfwError()
    {
        lock (s_glfwErrorLock)
        {
            string? error = s_glfwError;
            s_glfwError = null;
            return error;
        }
    }

    private static void ThrowIfGlfwError(string operation)
    {
        if (TakeGlfwError() is { } error)
            throw new InvalidOperationException($"GLFW error while {operation}: {error}.");
    }
}
