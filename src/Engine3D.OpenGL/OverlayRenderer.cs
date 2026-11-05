using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>Один экранный проход после 3D. Ресурсы создаются лениво и живут до Engine.Dispose.</summary>
internal sealed class OverlayRenderer
{
    // GLSL ASCII: OpenTK передаёт длину исходника в UTF-16, драйвер читает UTF-8.
    private const string VertexSource = """
        #version 330 core
        layout(location = 0) in vec2 a_position;
        layout(location = 1) in vec2 a_uv;
        out vec2 v_uv;
        void main() { v_uv = a_uv; gl_Position = vec4(a_position, 0.0, 1.0); }
        """;
    private const string FragmentSource = """
        #version 330 core
        in vec2 v_uv;
        uniform sampler2D u_texture;
        uniform vec4 u_color;
        out vec4 o_color;
        void main() { o_color = texture(u_texture, v_uv) * u_color; }
        """;

    private readonly List<OverlayQuad> _quads = [];
    private readonly float[] _vertices = new float[16];
    private readonly GpuResourceCache<TextureData, GpuTexture> _images = new(t => new GpuTexture(t, useMipmaps: false));
    private ShaderProgram? _shader;
    private GpuTexture? _white;
    private GpuTexture? _font;
    private int _vao;
    private int _buffer;
    private int _colorLocation;

    internal int ImageTextureCount => _images.Count;
    internal int VertexArray => _vao;
    internal int VertexBuffer => _buffer;
    internal int FontTexture => _font?.Handle ?? 0;
    internal int WhiteTexture => _white?.Handle ?? 0;

    public void Render(OverlayFrame frame, int logicalWidth, int logicalHeight, int framebufferWidth, int framebufferHeight)
    {
        _quads.Clear();
        if (framebufferWidth <= 0 || framebufferHeight <= 0) return;
        OverlayGeometry.Build(frame, logicalWidth, logicalHeight, _quads);
        if (_quads.Count == 0) return;

        using var state = new OverlayGlState();
        // В том числе для lazy TexImage2D: клиентские pixel-store/PBO не должны влиять на upload.
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.BindSampler(0, 0);
        EnsureResources();
        GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.ScissorTest);
        GL.Disable(EnableCap.StencilTest);
        GL.ColorMask(true, true, true, true);
        GL.Enable(EnableCap.Blend);
        GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
        _shader!.Use();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _buffer);
        foreach (var quad in _quads)
        {
            if (quad.Texture is null) _white!.Bind();
            else if (ReferenceEquals(quad.Texture, BitmapFont.Default.Texture))
            {
                _font ??= new GpuTexture(quad.Texture, useMipmaps: false);
                _font.Bind();
            }
            else _images.GetOrCreate(quad.Texture).Bind();
            var color = quad.Color;
            GL.Uniform4(_colorLocation, color.R, color.G, color.B, color.A);
            WriteVertex(0, quad.Left, quad.Top, quad.ULeft, quad.VTop);
            WriteVertex(4, quad.Left, quad.Bottom, quad.ULeft, quad.VBottom);
            WriteVertex(8, quad.Right, quad.Top, quad.URight, quad.VTop);
            WriteVertex(12, quad.Right, quad.Bottom, quad.URight, quad.VBottom);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertices.Length * sizeof(float), _vertices);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }
        GlErrors.ThrowIfAny("rendering overlay");
    }

    private void WriteVertex(int offset, float x, float y, float u, float v)
    {
        _vertices[offset] = x;
        _vertices[offset + 1] = y;
        _vertices[offset + 2] = u;
        _vertices[offset + 3] = v;
    }

    private void EnsureResources()
    {
        if (_shader is not null) return;
        try
        {
            _shader = new ShaderProgram(VertexSource, FragmentSource);
            _colorLocation = _shader.GetUniformLocation("u_color");
            _shader.Use();
            GL.Uniform1(_shader.GetUniformLocation("u_texture"), 0);
            _white = new GpuTexture(new TextureData(1, 1, [255, 255, 255, 255]), useMipmaps: false);
            _vao = GL.GenVertexArray();
            _buffer = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _buffer);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GlErrors.ThrowIfAny("creating overlay resources");
        }
        catch
        {
            Release([]); // cleanup не заменяет исходную ошибку создания.
            throw;
        }
    }

    public void Release(List<Exception> errors)
    {
        _images.DisposeAll(errors);
        ReleaseOne(() => _font?.Dispose(), errors);
        ReleaseOne(() => _white?.Dispose(), errors);
        ReleaseOne(() => { if (_buffer != 0) GL.DeleteBuffer(_buffer); }, errors);
        ReleaseOne(() => { if (_vao != 0) GL.DeleteVertexArray(_vao); }, errors);
        ReleaseOne(() => _shader?.Dispose(), errors);
        _font = _white = null;
        _shader = null;
        _vao = _buffer = 0;
        _quads.Clear();
    }

    private static void ReleaseOne(Action release, List<Exception> errors)
    {
        try { release(); }
        catch (Exception error) { errors.Add(error); }
    }
}
