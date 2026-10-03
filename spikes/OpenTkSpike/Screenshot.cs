using OpenTK.Graphics.OpenGL4;
using StbImageWriteSharp;

namespace OpenTkSpike;

internal static class Screenshot
{
    // Читает текущий (еще не показанный) back buffer и сохраняет его в PNG.
    public static void SaveBackBuffer(string path, int width, int height)
    {
        const int bytesPerPixel = 4;
        int rowSize = width * bytesPerPixel;
        var pixels = new byte[rowSize * height];

        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadBuffer(ReadBufferMode.Back);
        GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        ImageRows.FlipInPlace(pixels, rowSize, height);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
