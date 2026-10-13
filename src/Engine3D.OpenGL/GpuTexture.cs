using Engine3D.Core;
using OpenTK.Graphics.OpenGL4;

namespace Engine3D.OpenGL;

/// <summary>Одно GPU-представление неизменяемой TextureData; владелец — кэш Engine.</summary>
internal sealed class GpuTexture : IDisposable
{
    internal int Handle { get; }
    private bool _disposed;

    public GpuTexture(TextureData texture)
    {
        GL.GetInteger(GetPName.MaxTextureSize, out int maxSize);
        if (texture.Width > maxSize || texture.Height > maxSize)
            throw new InvalidOperationException($"Texture '{texture.SourcePath ?? "<in-memory>"}' {texture.Width}x{texture.Height} exceeds GPU limit {maxSize}.");

        Handle = GL.GenTexture();
        try
        {
            Bind();
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, BottomUpPixels(texture));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GlErrors.ThrowIfAny("uploading a texture to the GPU");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    // Переворот ровно один раз при upload. Приватные пиксели Core не изменяются.
    internal static byte[] BottomUpPixels(TextureData texture)
    {
        var pixels = new byte[texture.Pixels.Length];
        int stride = checked(texture.Width * 4);
        for (int row = 0; row < texture.Height; row++)
            texture.Pixels.Slice(row * stride, stride).CopyTo(pixels.AsSpan((texture.Height - 1 - row) * stride, stride));
        return pixels;
    }

    public void Bind()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, Handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GL.DeleteTexture(Handle);
    }
}
