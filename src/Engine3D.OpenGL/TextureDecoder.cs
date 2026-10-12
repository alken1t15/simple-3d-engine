using Engine3D.Core;
using StbImageSharp;

namespace Engine3D.OpenGL;

/// <summary>CPU-декодирование PNG без контекста OpenGL; строки остаются сверху вниз.</summary>
internal static class TextureDecoder
{
    public static TextureData Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        // I/O вне блока декодирования: сохраняются FileNotFoundException, UnauthorizedAccessException и IOException.
        byte[] encoded = File.ReadAllBytes(fullPath);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!encoded.AsSpan().StartsWith(signature))
            throw new InvalidDataException($"Texture '{fullPath}' is not a supported PNG image (invalid PNG signature).");

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return new TextureData(image.Width, image.Height, image.Data, fullPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Cannot decode PNG texture '{fullPath}': {exception.Message}", exception);
        }
    }
}
