using OpenTK.Graphics.OpenGL4;
using StbImageSharp;

namespace OpenTkSpike;

internal sealed class GpuTexture : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    private GpuTexture(int width, int height, byte[] rgbaBottomUp)
    {
        _handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _handle);

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, rgbaBottomUp);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    // PNG (и декодер StbImageSharp) хранит строки сверху вниз, а glTexImage2D считает первую строку нижней (t = 0).
    // Чтобы UV (0, 0) соответствовал левому нижнему углу картинки, строки переворачиваются здесь, в OpenGL-слое.
    public static GpuTexture Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Texture file not found: {path}", path);

        ImageResult image;
        using (var stream = File.OpenRead(path))
            image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        ImageRows.FlipInPlace(image.Data, image.Width * 4, image.Height);
        return new GpuTexture(image.Width, image.Height, image.Data);
    }

    public void Bind(TextureUnit unit)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteTexture(_handle);
        _disposed = true;
    }
}
