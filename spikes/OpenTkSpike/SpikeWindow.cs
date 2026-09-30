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
        VSync = VSyncMode.On;
    }

    public long RenderedFrames { get; private set; }

    protected override void OnLoad()
    {
        base.OnLoad();

        Console.WriteLine($"GL_VENDOR: {GL.GetString(StringName.Vendor)}");
        Console.WriteLine($"GL_RENDERER: {GL.GetString(StringName.Renderer)}");
        Console.WriteLine($"GL_VERSION: {GL.GetString(StringName.Version)}");
        Console.WriteLine($"GLSL: {GL.GetString(StringName.ShadingLanguageVersion)}");

        GL.ClearColor(0.10f, 0.20f, 0.30f, 1f);
        ApplyDepthTest();
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
            ApplyDepthTest();
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
        DrawObject(_cube!, rotatingCube, _texturedMaterial);

        var distantCube = Matrix4x4.CreateScale(0.6f) * Matrix4x4.CreateTranslation(-0.9f, 0.3f, -1.4f);
        DrawObject(_cube!, distantCube, _tintedMaterial);

        DrawObject(_plane!, Matrix4x4.Identity, _floorMaterial);

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
        ThrowOnGlErrors("rendering");

        _controlTexture?.Dispose();
        _plane?.Dispose();
        _cube?.Dispose();
        _shader?.Dispose();

        ThrowOnGlErrors("releasing resources");
        base.OnUnload();
    }

    private void DrawObject(GpuMesh mesh, Matrix4x4 model, Material material)
    {
        ShaderProgram.SetMatrix(_modelLocation, model);
        GL.Uniform4(_baseColorLocation, material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z, material.BaseColor.W);
        GL.Uniform1(_hasTextureLocation, material.Texture is null ? 0 : 1);
        mesh.Draw();
    }

    // glGetError хранит флаги ошибок до чтения; проверка раз на этап (а не после каждого вызова) не тормозит кадр.
    private static void ThrowOnGlErrors(string stage)
    {
        var errors = new List<OpenTK.Graphics.OpenGL4.ErrorCode>();
        for (var error = GL.GetError(); error != OpenTK.Graphics.OpenGL4.ErrorCode.NoError; error = GL.GetError())
            errors.Add(error);

        if (errors.Count > 0)
            throw new InvalidOperationException($"OpenGL errors while {stage}: {string.Join(", ", errors)}");
    }

    private void ApplyDepthTest()
    {
        if (_depthTest)
            GL.Enable(EnableCap.DepthTest);
        else
            GL.Disable(EnableCap.DepthTest);
    }

    private void UpdateTitle() =>
        Title = $"{BaseTitle} — depth: {(_depthTest ? "ON" : "OFF")}, вращение: {(_rotationPaused ? "пауза" : "ON")}"
            + " | ЛКМ — камера, колесо — зум, Space — пауза, D — depth, Esc — выход";
}
