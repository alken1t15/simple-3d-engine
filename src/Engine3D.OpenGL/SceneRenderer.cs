using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>
/// Отрисовка подготовленного кадра: шейдер, GPU-кэш мешей, depth test и базовый свет (P0-10): фоновый плюс один
/// направленный источник с рассеянной составляющей. RGB = baseColor * texture * (ambient + light * max(dot(n, −dir), 0)),
/// ограниченный [0, 1]; без текстуры множитель (1, 1, 1). Загрузку текстур на GPU подключает #10.
/// </summary>
internal sealed class SceneRenderer
{
    // Исходники GLSL — только ASCII, в том числе комментарии: OpenTK передает драйверу длину строки в символах
    // UTF-16, а драйвер читает байты UTF-8, поэтому кириллица обрезает исходник («unexpected end of file»).
    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 a_position;
        layout(location = 1) in vec2 a_uv;
        layout(location = 2) in vec3 a_normal;

        uniform mat4 u_model;
        uniform mat4 u_view;
        uniform mat4 u_projection;
        uniform mat4 u_normalTransform; // see FramePreparation.GetNormalTransform; GLSL sees its rows as columns

        out vec2 v_uv;
        out vec3 v_normal;

        void main()
        {
            // World-space normal: inverse-transpose of model = scale * rotation, i.e. (n / scale) * rotation.
            // The division by the scale is done in log2 space and the vector is rescaled so its largest component
            // is 1: valid extreme scales (about 1e-38 to 1e38 per axis) neither overflow nor vanish before normalize.
            vec3 magnitude = abs(a_normal);
            vec3 exponent = log2(max(magnitude, vec3(1.17549435e-38))) + u_normalTransform[3].xyz;
            exponent = mix(vec3(-1.0e30), exponent, greaterThan(magnitude, vec3(0.0))); // zero components stay zero
            float largest = max(exponent.x, max(exponent.y, exponent.z));
            v_normal = mat3(u_normalTransform) * (sign(a_normal) * exp2(exponent - largest));
            v_uv = a_uv;
            gl_Position = u_projection * u_view * u_model * vec4(a_position, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 v_uv;
        in vec3 v_normal;

        uniform vec4 u_baseColor;
        uniform bool u_hasTexture;
        uniform sampler2D u_texture;
        uniform vec3 u_ambientColor;
        uniform vec3 u_lightColor;
        uniform vec3 u_lightDirection; // unit vector, direction the light travels (world space)

        out vec4 o_color;

        void main()
        {
            vec3 albedo = u_baseColor.rgb;
            if (u_hasTexture)
                albedo *= texture(u_texture, v_uv).rgb;

            float diffuse = max(dot(normalize(v_normal), -u_lightDirection), 0.0);
            vec3 rgb = albedo * (u_ambientColor + u_lightColor * diffuse);
            o_color = vec4(clamp(rgb, 0.0, 1.0), 1.0); // opaque material: alpha is not blended
        }
        """;

    private readonly ShaderProgram _shader;
    private readonly GpuResourceCache<MeshData, GpuMesh> _meshes = new(mesh => new GpuMesh(mesh));
    private readonly int _modelLocation;
    private readonly int _normalTransformLocation;
    private readonly int _viewLocation;
    private readonly int _projectionLocation;
    private readonly int _baseColorLocation;
    private readonly int _hasTextureLocation;
    private readonly int _ambientColorLocation;
    private readonly int _lightColorLocation;
    private readonly int _lightDirectionLocation;

    /// <exception cref="InvalidOperationException">Шейдер не собрался или OpenGL сообщил об ошибке.</exception>
    public SceneRenderer()
    {
        _shader = new ShaderProgram(VertexShaderSource, FragmentShaderSource);
        try
        {
            _modelLocation = _shader.GetUniformLocation("u_model");
            _normalTransformLocation = _shader.GetUniformLocation("u_normalTransform");
            _viewLocation = _shader.GetUniformLocation("u_view");
            _projectionLocation = _shader.GetUniformLocation("u_projection");
            _baseColorLocation = _shader.GetUniformLocation("u_baseColor");
            _hasTextureLocation = _shader.GetUniformLocation("u_hasTexture");
            _ambientColorLocation = _shader.GetUniformLocation("u_ambientColor");
            _lightColorLocation = _shader.GetUniformLocation("u_lightColor");
            _lightDirectionLocation = _shader.GetUniformLocation("u_lightDirection");

            // Непрозрачные объекты, корректное перекрытие по глубине. Back-face culling в базовой версии выключен.
            GL.Enable(EnableCap.DepthTest);
            GlErrors.ThrowIfAny("creating the renderer");
        }
        catch
        {
            _shader.Dispose();
            throw;
        }
    }

    /// <summary>Число GPU-мешей в кэше: у многих объектов с общим MeshData оно равно 1.</summary>
    public int MeshCount => _meshes.Count;

    public void Render(Scene scene, FramePreparation frame, int framebufferWidth, int framebufferHeight)
    {
        GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
        GL.ClearColor(0.10f, 0.20f, 0.30f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();
        ShaderProgram.SetMatrix(_viewLocation, frame.View);
        ShaderProgram.SetMatrix(_projectionLocation, frame.Projection);

        // Свет общий для кадра; направление уже проверено и нормализовано в FramePreparation.
        var lighting = scene.Lighting;
        GL.Uniform3(_ambientColorLocation, lighting.AmbientColor.X, lighting.AmbientColor.Y, lighting.AmbientColor.Z);
        GL.Uniform3(_lightColorLocation, lighting.DirectionalColor.X, lighting.DirectionalColor.Y, lighting.DirectionalColor.Z);
        GL.Uniform3(_lightDirectionLocation, frame.LightDirection.X, frame.LightDirection.Y, frame.LightDirection.Z);
        GL.Uniform1(_hasTextureLocation, 0); // GPU-текстуры подключает #10; до этого материал рисуется цветом

        var objects = scene.Objects;
        var models = frame.Models;
        var normalTransforms = frame.NormalTransforms;
        for (int i = 0; i < objects.Count; i++)
        {
            var item = objects[i];
            var mesh = _meshes.GetOrCreate(item.Mesh);
            var color = item.Material.BaseColor;

            ShaderProgram.SetMatrix(_modelLocation, models[i]);
            ShaderProgram.SetMatrix(_normalTransformLocation, normalTransforms[i]);
            GL.Uniform4(_baseColorLocation, color.R, color.G, color.B, color.A);
            mesh.Draw();
        }

        GL.BindVertexArray(0);
    }

    /// <summary>Удаляет GPU-меши и шейдер; ошибки собираются, удаление продолжается.</summary>
    public void Release(List<Exception> errors)
    {
        _meshes.DisposeAll(errors);
        try
        {
            _shader.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }
}
