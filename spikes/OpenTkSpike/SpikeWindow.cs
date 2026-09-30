using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace OpenTkSpike;

// Материал spike повторяет проектный MaterialData: базовый цвет и необязательная текстура.
// При наличии текстуры итоговый цвет = цвет текстуры * базовый цвет.
internal readonly record struct Material(Vector4 BaseColor, GpuTexture? Texture);

internal sealed class SpikeWindow : GameWindow
{
    private const string BaseTitle = "OpenTK spike";

    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 a_position;
        layout(location = 1) in vec2 a_uv;

        uniform mat4 u_model;
        uniform mat4 u_view;
        uniform mat4 u_projection;

        out vec2 v_uv;

        void main()
        {
            v_uv = a_uv;
            gl_Position = u_projection * u_view * u_model * vec4(a_position, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 v_uv;

        uniform vec4 u_baseColor;
        uniform bool u_hasTexture;
        uniform sampler2D u_texture;

        out vec4 o_color;

        void main()
        {
            vec4 color = u_baseColor;
            if (u_hasTexture)
                color *= texture(u_texture, v_uv);
            o_color = color;
        }
        """;

    private const float MouseSensitivity = 0.008f; // радиан на пиксель

    private static readonly Vector3 RotationAxis = Vector3.Normalize(new Vector3(0.3f, 1f, 0.2f));

    // Цвета граней в режиме без текстуры, в порядке граней Primitives.CreateCube:
    // +X красный, -X голубой, +Y зеленый, -Y пурпурный, +Z синий, -Z желтый.
    private static readonly Vector4[] FaceColors =
    [
        new(1f, 0f, 0f, 1f), new(0f, 1f, 1f, 1f),
        new(0f, 1f, 0f, 1f), new(1f, 0f, 1f, 1f),
        new(0f, 0f, 1f, 1f), new(1f, 1f, 0f, 1f),
    ];

    private static bool s_glInfoPrinted; // версии GL печатаются один раз на процесс, а не в каждом цикле --cycles

    private readonly SpikeOptions _options;
    private readonly OrbitCamera _camera = new(Vector3.Zero, new Vector3(2.5f, 2f, 4f));

    private ShaderProgram? _shader;
    private GpuMesh? _cube;
    private GpuMesh? _plane;
    private GpuTexture? _controlTexture;
    private Material _texturedMaterial;
    private Material _tintedMaterial;
    private Material _floorMaterial;
    private int _modelLocation;
    private int _viewLocation;
    private int _projectionLocation;
    private int _baseColorLocation;
    private int _hasTextureLocation;
    private float _angle;
    private bool _depthTest;
    private CullMode _culling;
    private bool _texture;
    private bool _rotationPaused;

    public SpikeWindow(SpikeOptions options)
        : base(GameWindowSettings.Default, new NativeWindowSettings
        {
            ClientSize = new OpenTK.Mathematics.Vector2i(800, 600),
            Title = BaseTitle,
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Flags = ContextFlags.ForwardCompatible,
        })
    {
        _options = options;
        _depthTest = options.DepthTest;
        _culling = options.Culling;
        _texture = options.Texture;
        _angle = options.Angle ?? 0f;
        _rotationPaused = options.Angle is not null;
        VSync = VSyncMode.On;
    }

    public long RenderedFrames { get; private set; }

    protected override void OnLoad()
    {
        base.OnLoad();

        if (!s_glInfoPrinted)
        {
            Console.WriteLine($"GL_VENDOR: {GL.GetString(StringName.Vendor)}");
            Console.WriteLine($"GL_RENDERER: {GL.GetString(StringName.Renderer)}");
            Console.WriteLine($"GL_VERSION: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GLSL: {GL.GetString(StringName.ShadingLanguageVersion)}");
            s_glInfoPrinted = true;
        }

        GL.ClearColor(0.10f, 0.20f, 0.30f, 1f);
        GL.FrontFace(FrontFaceDirection.Ccw); // лицевая сторона — обход против часовой стрелки (значение OpenGL по умолчанию)
        ApplyRenderState();
        UpdateTitle();

        _shader = new ShaderProgram(VertexShaderSource, FragmentShaderSource);
        _modelLocation = _shader.GetUniformLocation("u_model");
        _viewLocation = _shader.GetUniformLocation("u_view");
        _projectionLocation = _shader.GetUniformLocation("u_projection");
        _baseColorLocation = _shader.GetUniformLocation("u_baseColor");
        _hasTextureLocation = _shader.GetUniformLocation("u_hasTexture");

        var (cubeVertices, cubeIndices) = Primitives.CreateCube();
        _cube = new GpuMesh(cubeVertices, cubeIndices);

        var (planeVertices, planeIndices) = Primitives.CreatePlane(3f);
        _plane = new GpuMesh(planeVertices, planeIndices);

        // Сэмплер u_texture по умолчанию читает текстурный блок 0; других текстур в spike нет.
        _controlTexture = GpuTexture.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "control.png"));
        _controlTexture.Bind(TextureUnit.Texture0);

        // Одна текстура в двух материалах: как в API, TextureData может разделяться многими объектами.
        _texturedMaterial = new Material(Vector4.One, _controlTexture);
        _tintedMaterial = new Material(new Vector4(1f, 0.6f, 0.2f, 1f), _controlTexture);
        _floorMaterial = new Material(new Vector4(0.55f, 0.55f, 0.55f, 1f), null);

        ThrowOnGlErrors("loading resources");
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        if (KeyboardState.IsKeyDown(Keys.Escape))
            Close();

        if (KeyboardState.IsKeyPressed(Keys.D))
        {
            _depthTest = !_depthTest;
            ApplyRenderState();
            UpdateTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.C))
        {
            _culling = _culling == CullMode.Off ? CullMode.Back : CullMode.Off;
            ApplyRenderState();
            UpdateTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.T))
        {
            _texture = !_texture;
            UpdateTitle();
        }

        if (KeyboardState.IsKeyPressed(Keys.Space))
        {
            _rotationPaused = !_rotationPaused;
            UpdateTitle();
        }

        if (MouseState.IsButtonDown(MouseButton.Left))
            _camera.Rotate(MouseState.Delta.X * MouseSensitivity, MouseState.Delta.Y * MouseSensitivity);

        if (MouseState.ScrollDelta.Y != 0f)
            _camera.Zoom(MouseState.ScrollDelta.Y);

        if (!_rotationPaused)
            _angle += (float)args.Time;
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Соглашение System.Numerics: вектор-строка, поэтому model-матрица = scale * rotation * translation,
        // а полное преобразование — model * view * projection.
        var view = _camera.ViewMatrix;
        float aspect = FramebufferSize.X / (float)Math.Max(FramebufferSize.Y, 1);
        var projection = GlProjection.CreatePerspective(MathF.PI / 3f, aspect, 0.1f, 100f);

        _shader!.Use();
        ShaderProgram.SetMatrix(_viewLocation, view);
        ShaderProgram.SetMatrix(_projectionLocation, projection);

        // Порядок рисования намеренно «неудобный»: дальние объекты после ближних.
        // С depth test картинка от порядка не зависит; без него дальний куб и плоскость закрывают ближний куб.
        var rotatingCube = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(RotationAxis, _angle));
        DrawCube(rotatingCube, _texturedMaterial);

        var distantCube = Matrix4x4.CreateScale(0.6f) * Matrix4x4.CreateTranslation(-0.9f, 0.3f, -1.4f);
        DrawCube(distantCube, _tintedMaterial);

        DrawObject(_plane!, Matrix4x4.Identity, _floorMaterial);

        long frame = RenderedFrames + 1;
        if (frame == _options.FailAtFrame)
        {
            Console.WriteLine($"Injecting OpenGL error on frame {frame}.");
            GL.Enable((EnableCap)0x7FFFFFFF); // заведомо неверный enum → GL_INVALID_ENUM
        }

        // Одна проверка на кадр: ошибка обнаруживается на том кадре, где случилась, а не при закрытии окна.
        ThrowOnGlErrors($"rendering frame {frame}");

        bool lastFrame = _options.FrameLimit > 0 && RenderedFrames + 1 >= _options.FrameLimit;
        if (lastFrame && _options.ScreenshotPath is not null)
            Screenshot.SaveBackBuffer(_options.ScreenshotPath, FramebufferSize.X, FramebufferSize.Y);

        SwapBuffers();
        RenderedFrames++;

        if (lastFrame)
            Close();
    }

    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
    }

    protected override void OnUnload()
    {
        ReleaseResources();
        base.OnUnload();
        ThrowOnGlErrors("releasing resources");
    }

    // Если Run прерван исключением (например, ошибкой GL в кадре), OnUnload не вызывается —
    // тогда ресурсы освобождает Dispose окна, пока его контекст еще существует.
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseResources();

        base.Dispose(disposing);
    }

    private void ReleaseResources()
    {
        if (_shader is null)
            return;

        _controlTexture?.Dispose();
        _plane?.Dispose();
        _cube?.Dispose();
        _shader.Dispose();

        _controlTexture = null;
        _plane = null;
        _cube = null;
        _shader = null;
        Console.WriteLine("GPU resources released.");
    }

    // Без текстуры каждая грань рисуется своим базовым цветом (шесть вызовов по 6 индексов) —
    // «цветной куб» получается средствами материала, без цвета в вершинах.
    private void DrawCube(Matrix4x4 model, Material material)
    {
        if (_texture)
        {
            DrawObject(_cube!, model, material);
            return;
        }

        ShaderProgram.SetMatrix(_modelLocation, model);
        GL.Uniform1(_hasTextureLocation, 0);
        for (int face = 0; face < FaceColors.Length; face++)
        {
            var color = FaceColors[face];
            GL.Uniform4(_baseColorLocation, color.X, color.Y, color.Z, color.W);
            _cube!.DrawRange(face * 6, 6);
        }
    }

    private void DrawObject(GpuMesh mesh, Matrix4x4 model, Material material)
    {
        ShaderProgram.SetMatrix(_modelLocation, model);
        GL.Uniform4(_baseColorLocation, material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.BaseColor.W);
        GL.Uniform1(_hasTextureLocation, material.Texture is null ? 0 : 1);
        mesh.Draw();
    }

    // glGetError хранит флаги ошибок до чтения, поэтому одной проверки на кадр достаточно, чтобы ничего не пропустить.
    private static void ThrowOnGlErrors(string stage)
    {
        var errors = new List<OpenTK.Graphics.OpenGL4.ErrorCode>();
        for (var error = GL.GetError(); error != OpenTK.Graphics.OpenGL4.ErrorCode.NoError; error = GL.GetError())
            errors.Add(error);

        if (errors.Count > 0)
            throw new InvalidOperationException($"OpenGL errors while {stage}: {string.Join(", ", errors)}");
    }

    private void ApplyRenderState()
    {
        SetCapability(EnableCap.DepthTest, _depthTest);
        SetCapability(EnableCap.CullFace, _culling != CullMode.Off);
        if (_culling != CullMode.Off)
            GL.CullFace(_culling == CullMode.Back ? TriangleFace.Back : TriangleFace.Front);
    }

    private static void SetCapability(EnableCap capability, bool enabled)
    {
        if (enabled)
            GL.Enable(capability);
        else
            GL.Disable(capability);
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private void UpdateTitle() =>
        Title = $"{BaseTitle} — depth: {OnOff(_depthTest)}, culling: {_culling}, текстура: {OnOff(_texture)}, "
            + $"вращение: {(_rotationPaused ? "пауза" : "ON")}"
            + " | ЛКМ — камера, колесо — зум, Space — пауза, D — depth, C — culling, T — текстура, Esc — выход";
}
