using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using GlfwErrorCode = OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode;
using NativeMouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
using NumericsVector2 = System.Numerics.Vector2;

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
    private readonly WindowSizeController _windowSize;
    private readonly SceneRenderer _renderer;
    private OverlayRenderer? _overlayRenderer;
    private OverlayFrame? _overlay;
    private readonly FramePreparation _frame = new();
    private readonly KeyboardInput _input = new();
    private readonly PointerInput _pointer = new();
    private InputSnapshot _snapshot;
    private CursorMode _cursorMode;
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

    private Engine(NativeWindow window, EngineOptions options)
    {
        _window = window;
        _windowSize = new WindowSizeController(window, options);
        _windowSize.Refresh();
        ThrowIfGlfwError("applying window size limits");
        _window.Resize += _windowSize.OnResize;
        _renderer = new SceneRenderer(); // шейдер собирается при создании: ошибка — до возврата Engine
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _window.Refresh += OnRefresh;
        _window.KeyDown += OnKeyDown;
        _window.KeyUp += OnKeyUp;
        _window.FocusedChanged += OnFocusedChanged;
        _window.MouseMove += OnMouseMove;
        _window.MouseDown += OnMouseDown;
        _window.MouseUp += OnMouseUp;
    }

    /// <summary>
    /// Число показанных кадров последнего <see cref="Run"/>: обнуляется перед ним, увеличивается после каждого
    /// показанного кадра (включая перерисовку по запросу системы) и доступно после возврата и после Dispose.
    /// Не равно числу вызовов onUpdate.
    /// </summary>
    public long RenderedFrames { get; private set; }

    /// <summary>
    /// Снимок ввода после оконных событий. Читается только из onUpdate на потоке Create;
    /// возвращённое значение можно сохранить, оно не меняется при следующем обновлении.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Engine освобождён.</exception>
    /// <exception cref="InvalidOperationException">Не поток Create или вызов вне onUpdate.</exception>
    public InputSnapshot Input
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            if (!_inUpdate)
                throw new InvalidOperationException("Input is available only inside onUpdate.");
            return _snapshot;
        }
    }

    /// <summary>
    /// Режим курсора на потоке Create. Set допустим до Run или внутри onUpdate; после Run запрещён.
    /// Потеря фокуса сбрасывает в Normal; возврат фокуса сам не включает capture.
    /// Попытка захвата во время Run без фокуса оставляет Normal.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Engine освобождён.</exception>
    /// <exception cref="InvalidOperationException">Неверный поток или момент изменения.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Неизвестный режим.</exception>
    public CursorMode CursorMode
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            return _cursorMode;
        }
        set
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            if (_state != State.Created && !(_state == State.Running && _inUpdate))
                throw new InvalidOperationException("CursorMode can be changed only before Run or inside onUpdate.");
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_state == State.Running && !_window.IsFocused) value = CursorMode.Normal;
            SetCursorMode(value);
            ThrowIfGlfwError("changing cursor mode");
        }
    }

    /// <summary>
    /// Текущий размер кадрового буфера в физических пикселях (на HiDPI отличается от WindowBounds).
    /// Читается на потоке Create до Dispose; нулевой размер означает отсутствие доступного буфера.
    /// </summary>
    public (int Width, int Height) FramebufferSize
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            var size = _window.FramebufferSize;
            return (size.X, size.Y);
        }
    }

    /// <summary>Размер клиентской области в логических пикселях без запроса положения окна.
    /// Доступен также на Wayland. Только поток Create, до Dispose.</summary>
    public (int Width, int Height) ClientSize
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            var size = _window.ClientSize;
            return (size.X, size.Y);
        }
    }

    /// <summary>Эффективные ограничения размера окна; обновляются в каждом кадре.
    /// Только поток Create, до Dispose. Максимумы без ограничения равны int.MaxValue.</summary>
    public ClientSizeLimits ClientSizeLimits
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            return _windowSize.Limits;
        }
    }

    /// <summary>Последние ограничения обычного окна, в том числе для выбора размера перед выходом из fullscreen.</summary>
    public ClientSizeLimits WindowedClientSizeLimits
    {
        get { ThrowIfDisposed(); ThrowIfWrongThread(); return _windowSize.WindowedLimits; }
    }

    /// <summary>Фактическая привязка окна к монитору. Только поток Create, до Dispose.</summary>
    public bool IsFullscreen
    {
        get { ThrowIfDisposed(); ThrowIfWrongThread(); return _windowSize.IsFullscreen; }
    }

    /// <summary>Полный размер desktop video mode в логических пикселях, без вычитания рамки и панелей.
    /// На Wayland без глобальной позиции окна используется основной монитор.</summary>
    public (int Width, int Height) FullscreenSize
    {
        get { ThrowIfDisposed(); ThrowIfWrongThread(); return _windowSize.FullscreenSize; }
    }

    /// <summary>Переключает текущее окно в fullscreen с текущим desktop video mode либо обратно.
    /// Сохраняет нормальный размер/позицию для восстановления. Только до Run или внутри onUpdate.
    /// На Wayland позицию восстановления выбирает compositor. SetClientSize доступен только в оконном режиме.</summary>
    public void SetFullscreen(bool fullscreen)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state != State.Created && !(_state == State.Running && _inUpdate))
            throw new InvalidOperationException("SetFullscreen is allowed only before Run or inside onUpdate.");
        _windowSize.SetFullscreen(fullscreen);
        ThrowIfGlfwError("switching fullscreen mode");
    }

    /// <summary>Запрашивает размер клиентской области в логических пикселях, ограниченный ClientSizeLimits.
    /// Только до Run или внутри onUpdate на потоке Create. Фактический результат ОС читается
    /// через ClientSize в последующих кадрах; на Wayland изменение асинхронное.</summary>
    public void SetClientSize(int width, int height)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state != State.Created && !(_state == State.Running && _inUpdate))
            throw new InvalidOperationException("SetClientSize is allowed only before Run or inside onUpdate.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _windowSize.SetSize(width, height);
        ThrowIfGlfwError("resizing the window");
    }

    /// <summary>
    /// Текущее положение левого верхнего угла окна вместе с рамкой и размер клиентской области в тех же единицах,
    /// что <see cref="EngineOptions.Position"/>, <see cref="EngineOptions.Width"/> и <see cref="EngineOptions.Height"/>.
    /// Читается с потока Create, например из onUpdate: приложение может запомнить положение, заданное пользователем
    /// перетаскиванием или ресайзом. Положение недоступно на Wayland; для layout используйте ClientSize.
    /// </summary>
    public (int X, int Y, int Width, int Height) WindowBounds
    {
        get
        {
            ThrowIfDisposed();
            ThrowIfWrongThread();
            var location = _window.Location;
            var size = _window.ClientSize;
            return (location.X, location.Y, size.X, size.Y);
        }
    }

    /// <summary>Число GPU-мешей в кэше: проверка того, что общий MeshData загружен на GPU один раз.</summary>
    internal int GpuMeshCount => _renderer.MeshCount;

    /// <summary>Число GPU-текстур: одна общая TextureData загружается только один раз.</summary>
    internal int GpuTextureCount => _renderer.TextureCount;

    internal OverlayRenderer? OverlayRenderer => _overlayRenderer;

    /// <summary>
    /// Задаёт immutable overlay в логических пикселях client area; null очищает слой.
    /// Разрешён до Run и внутри onUpdate на потоке Create. GPU-upload выполняется лениво при рендере.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Engine освобождён.</exception>
    /// <exception cref="InvalidOperationException">Неверный поток или вызов вне разрешённого lifecycle.</exception>
    public void SetOverlay(OverlayFrame? frame)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state != State.Created && !(_state == State.Running && _inUpdate))
            throw new InvalidOperationException("SetOverlay is valid only before Run or inside onUpdate.");
        _overlay = frame;
    }

    /// <summary>Декодирует PNG в неизменяемую CPU TextureData. GPU-upload выполняется лениво при показе.</summary>
    /// <exception cref="ObjectDisposedException">Engine уже освобожден.</exception>
    /// <exception cref="InvalidOperationException">Вызов не с потока Create или Run уже начался.</exception>
    /// <exception cref="ArgumentException">Путь пустой или некорректный.</exception>
    /// <exception cref="FileNotFoundException">Файл не существует.</exception>
    /// <exception cref="UnauthorizedAccessException">Нет доступа к файлу.</exception>
    /// <exception cref="IOException">Ошибка чтения файла.</exception>
    /// <exception cref="InvalidDataException">Поврежденный или неподдерживаемый PNG; сообщение содержит путь и причину.</exception>
    public TextureData LoadTexture(string path)
    {
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state != State.Created)
            throw new InvalidOperationException("LoadTexture is valid only after Create and before Run.");
        return TextureDecoder.Load(path);
    }

    /// <summary>Задаёт RGBA8-значок существующего окна в Windows/X11. Строки идут сверху вниз.
    /// Только до Run или внутри onUpdate, на потоке Create. Буфер копируется.
    /// На Wayland/macOS возвращает false: значок там задаётся desktop entry/app bundle.</summary>
    public bool SetWindowIcon(TextureData icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ThrowIfDisposed();
        ThrowIfWrongThread();
        if (_state != State.Created && !(_state == State.Running && _inUpdate))
            throw new InvalidOperationException("SetWindowIcon is allowed only before Run or inside onUpdate.");
        if (GLFW.GetPlatform() is not (Platform.Win32 or Platform.X11)) return false;
        _window.Icon = new OpenTK.Windowing.Common.Input.WindowIcon(
            new OpenTK.Windowing.Common.Input.Image(icon.Width, icon.Height, icon.Pixels.ToArray()));
        ThrowIfGlfwError("setting the window icon");
        return true;
    }

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
        if (options.MinimumClientSize is { } minimum)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum.Width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum.Height);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        TakeGlfwError(); // ошибка, оставшаяся от предыдущего Engine, к этому вызову не относится
        NativeWindow? window = null;
        try
        {
            // GLFWProvider.EnsureInitialized (OpenTK 4.9.4) игнорирует неудачу glfwInit и все равно считает GLFW
            // инициализированным: следующий Create получил бы лишь NotInitialized. Поэтому успех проверяется явно:
            // glfwInit сразу возвращает true, если GLFW уже инициализирован, а после неудачи оставляет его
            // неинициализированным, и следующий Create повторяет попытку (например, когда дисплей стал доступен).
            GLFWProvider.EnsureInitialized();
            if (!GLFW.Init())
                throw new InvalidOperationException("GLFW initialization failed.");

            // Окно создается скрытым (StartVisible = false) и показывается в RunLoop. OpenTK не выставляет
            // Focused-хинт при показе, поэтому фокус при показе задается отдельным GLFW-хинтом до создания окна.
            GLFW.WindowHint(WindowHintBool.FocusOnShow, options.FocusOnShow);

            window = new NativeWindow(new NativeWindowSettings
            {
                ClientSize = new Vector2i(options.Width, options.Height),
                WindowBorder = options.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
                Location = options.Position is { } position ? new Vector2i(position.X, position.Y) : null,
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

            return new Engine(window, options);
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
            SetCursorMode(CursorMode.Normal);
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
        _overlayRenderer?.Release(errors);
        _overlay = null;
        _renderer.Release(errors);
        try
        {
            _window.Refresh -= OnRefresh;
            _window.KeyDown -= OnKeyDown;
            _window.KeyUp -= OnKeyUp;
            _window.FocusedChanged -= OnFocusedChanged;
            _window.MouseMove -= OnMouseMove;
            _window.MouseDown -= OnMouseDown;
            _window.MouseUp -= OnMouseUp;
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
        _input.SetFocus(_window.IsFocused);
        _pointer.SetFocus(_window.IsFocused);
        if (!_window.IsFocused) SetCursorMode(CursorMode.Normal);
        var clock = Stopwatch.StartNew();
        double? previousUpdate = null;

        while (true)
        {
            _input.BeginFrame();
            _pointer.BeginFrame();
            _window.NewInputFrame();
            NativeWindow.ProcessWindowEvents(waitForEvents: false);
            ThrowIfGlfwError("processing window events");
            _callbackFailure?.Throw();
            if (_closeRequested || _window.IsExiting)
                break;

            _windowSize.Refresh();
            ThrowIfGlfwError("applying window size limits");

            if (_input.NeedsRearm) _input.ReconcileHeld(ReadHeldKeys());
            _pointer.ReconcileHeld(ReadHeldButtons());
            _pointer.SynchronizePosition(ReadPointerPosition());
            var clientSize = _window.ClientSize;
            _snapshot = _input.WithPointer(_pointer.Snapshot(clientSize.X, clientSize.Y));
            ThrowIfGlfwError("reading input state");

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
        var client = _window.ClientSize;
        if (IsMinimized() || size.X <= 0 || size.Y <= 0 || client.X <= 0 || client.Y <= 0)
            return;
        if (!_windowSize.CanRender(client.X, client.Y)) return;

        // Сначала CPU: проверка состояния после onUpdate и матрицы — до любых GL-вызовов кадра.
        _frame.Prepare(_scene!, _camera!, size.X, size.Y);
        _renderer.Render(_scene!, _frame, size.X, size.Y);
        if (_overlay is { } overlay)
        {
            _overlayRenderer ??= new OverlayRenderer();
            _overlayRenderer.Render(overlay, client.X, client.Y, size.X, size.Y);
        }
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

    // На X11 минимизация не обязана обнулять framebuffer. Проверяем iconified state
    // существующего окна через GLFW; второй контекст/публичный native handle не нужен.
    private unsafe bool IsMinimized() => GLFW.GetWindowAttrib(_window.WindowPtr, WindowAttributeGetBool.Iconified);

    private void OnKeyDown(KeyboardKeyEventArgs args)
    {
        if (MapKey(args.Key) is { } key)
            _input.KeyDown(key, args.IsRepeat);
    }

    private void OnKeyUp(KeyboardKeyEventArgs args)
    {
        if (MapKey(args.Key) is { } key)
            _input.KeyUp(key);
    }

    private void OnFocusedChanged(FocusedChangedEventArgs args)
    {
        _input.SetFocus(args.IsFocused);
        _pointer.SetFocus(args.IsFocused);
        if (!args.IsFocused) SetCursorMode(CursorMode.Normal);
    }

    private void SetCursorMode(CursorMode mode)
    {
        if (_cursorMode == mode) return;
        _pointer.ResetMotion();
        _window.CursorState = mode == CursorMode.Captured ? CursorState.Grabbed : CursorState.Normal;
        _cursorMode = mode;
        // SetInputMode может синхронно вызвать MouseMove; следующий отложенный delta тоже подавляется.
        _pointer.ResetMotion();
    }

    private void OnMouseMove(MouseMoveEventArgs args) => _pointer.Move(
        new NumericsVector2(args.Position.X, args.Position.Y), new NumericsVector2(args.Delta.X, args.Delta.Y));

    private unsafe NumericsVector2 ReadPointerPosition()
    {
        // NativeWindow.MousePosition — событийный кэш: после CursorLeave он может остаться внутри.
        // GLFW запрашивает backend в Normal и возвращает виртуальную позицию в Captured.
        GLFW.GetCursorPos(_window.WindowPtr, out double x, out double y);
        return new NumericsVector2((float)x, (float)y);
    }

    private void OnMouseDown(MouseButtonEventArgs args)
    {
        if (MapButton(args.Button) is { } button) _pointer.ButtonDown(button);
    }

    private void OnMouseUp(MouseButtonEventArgs args)
    {
        if (MapButton(args.Button) is { } button) _pointer.ButtonUp(button);
    }

    private uint ReadHeldButtons()
    {
        uint result = 0;
        foreach (var (native, button) in s_buttons)
            if (_window.MouseState.IsButtonDown(native)) result |= PointerSnapshot.Mask(button);
        return result;
    }

    private static readonly (NativeMouseButton Native, MouseButton Button)[] s_buttons =
    [
        (NativeMouseButton.Left, MouseButton.Left), (NativeMouseButton.Right, MouseButton.Right),
        (NativeMouseButton.Middle, MouseButton.Middle),
    ];

    private static MouseButton? MapButton(NativeMouseButton button) => button switch
    {
        NativeMouseButton.Left => MouseButton.Left,
        NativeMouseButton.Right => MouseButton.Right,
        NativeMouseButton.Middle => MouseButton.Middle,
        _ => null,
    };

    private uint ReadHeldKeys()
    {
        // X11 очищает GLFW/OpenTK cache при focus loss. Его первый autorepeat после
        // focus gain выглядит как новый press, поэтому rearm использует XQueryKeymap.
        if (X11KeyboardState.TryReadHeldKeys(s_keys, out uint physical)) return physical;
        uint result = 0;
        foreach (var (native, key) in s_keys)
        {
            if (_window.KeyboardState.IsKeyDown(native))
                result |= InputSnapshot.Mask(key);
        }
        return result;
    }

    private static readonly (Keys Native, InputKey Key)[] s_keys =
    [
        (Keys.A, InputKey.A), (Keys.D, InputKey.D), (Keys.Left, InputKey.Left),
        (Keys.Right, InputKey.Right), (Keys.Space, InputKey.Space), (Keys.R, InputKey.R),
        (Keys.Escape, InputKey.Escape),
        (Keys.W, InputKey.W), (Keys.S, InputKey.S), (Keys.LeftShift, InputKey.LeftShift),
        (Keys.RightShift, InputKey.RightShift), (Keys.Enter, InputKey.Enter), (Keys.Up, InputKey.Up),
        (Keys.Down, InputKey.Down), (Keys.Tab, InputKey.Tab),
    ];

    private static InputKey? MapKey(Keys key) => key switch
    {
        Keys.A => InputKey.A,
        Keys.D => InputKey.D,
        Keys.Left => InputKey.Left,
        Keys.Right => InputKey.Right,
        Keys.Space => InputKey.Space,
        Keys.R => InputKey.R,
        Keys.Escape => InputKey.Escape,
        Keys.W => InputKey.W,
        Keys.S => InputKey.S,
        Keys.LeftShift => InputKey.LeftShift,
        Keys.RightShift => InputKey.RightShift,
        Keys.Enter => InputKey.Enter,
        Keys.Up => InputKey.Up,
        Keys.Down => InputKey.Down,
        Keys.Tab => InputKey.Tab,
        _ => null,
    };

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
