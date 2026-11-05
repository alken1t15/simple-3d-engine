using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>Сохраняет именно состояния, которые меняет overlay, включая первый ленивый upload.</summary>
internal sealed class OverlayGlState : IDisposable
{
    private readonly bool _depth = GL.IsEnabled(EnableCap.DepthTest);
    private readonly bool _blend = GL.IsEnabled(EnableCap.Blend);
    private readonly bool _cull = GL.IsEnabled(EnableCap.CullFace);
    private readonly bool _scissor = GL.IsEnabled(EnableCap.ScissorTest);
    private readonly bool _stencil = GL.IsEnabled(EnableCap.StencilTest);
    private readonly bool _depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
    private readonly bool[] _colorWrite = new bool[4];
    private readonly int[] _viewport = new int[4];
    private readonly int _srcRgb = GL.GetInteger(GetPName.BlendSrcRgb);
    private readonly int _dstRgb = GL.GetInteger(GetPName.BlendDstRgb);
    private readonly int _srcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha);
    private readonly int _dstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
    private readonly int _equationRgb = GL.GetInteger(GetPName.BlendEquationRgb);
    private readonly int _equationAlpha = GL.GetInteger(GetPName.BlendEquationAlpha);
    private readonly int _program = GL.GetInteger(GetPName.CurrentProgram);
    private readonly int _vao = GL.GetInteger(GetPName.VertexArrayBinding);
    private readonly int _buffer = GL.GetInteger(GetPName.ArrayBufferBinding);
    private readonly int _activeTexture = GL.GetInteger(GetPName.ActiveTexture);
    private readonly int _unpackBuffer = GL.GetInteger(GetPName.PixelUnpackBufferBinding);
    private readonly int _unpackAlignment = GL.GetInteger(GetPName.UnpackAlignment);
    private readonly int _unpackRowLength = GL.GetInteger(GetPName.UnpackRowLength);
    private readonly int _unpackSkipRows = GL.GetInteger(GetPName.UnpackSkipRows);
    private readonly int _unpackSkipPixels = GL.GetInteger(GetPName.UnpackSkipPixels);
    private readonly int _texture0;
    private readonly int _sampler0;

    public OverlayGlState()
    {
        GL.GetInteger(GetPName.Viewport, _viewport);
        GL.GetBoolean(GetPName.ColorWritemask, _colorWrite);
        GL.ActiveTexture(TextureUnit.Texture0);
        _texture0 = GL.GetInteger(GetPName.TextureBinding2D);
        _sampler0 = GL.GetInteger(GetPName.SamplerBinding);
        GL.ActiveTexture((TextureUnit)_activeTexture);
    }

    public void Dispose()
    {
        Set(EnableCap.DepthTest, _depth);
        Set(EnableCap.Blend, _blend);
        Set(EnableCap.CullFace, _cull);
        Set(EnableCap.ScissorTest, _scissor);
        Set(EnableCap.StencilTest, _stencil);
        GL.DepthMask(_depthWrite);
        GL.ColorMask(_colorWrite[0], _colorWrite[1], _colorWrite[2], _colorWrite[3]);
        GL.BlendFuncSeparate((BlendingFactorSrc)_srcRgb, (BlendingFactorDest)_dstRgb,
            (BlendingFactorSrc)_srcAlpha, (BlendingFactorDest)_dstAlpha);
        GL.BlendEquationSeparate((BlendEquationMode)_equationRgb, (BlendEquationMode)_equationAlpha);
        GL.Viewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
        GL.UseProgram(_program);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _buffer);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _unpackBuffer);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, _unpackAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, _unpackRowLength);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, _unpackSkipRows);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, _unpackSkipPixels);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture0);
        GL.BindSampler(0, _sampler0);
        GL.ActiveTexture((TextureUnit)_activeTexture);
    }

    private static void Set(EnableCap cap, bool enabled)
    {
        if (enabled) GL.Enable(cap);
        else GL.Disable(cap);
    }
}
