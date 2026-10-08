namespace Engine3D.Core;

/// <summary>
/// Неизменяемые CPU-данные растровой текстуры в формате RGBA8 (4 байта на пиксель).
/// Строки идут сверху вниз, как в файле изображения; переворот под соглашение OpenGL выполняет renderer.
/// Один экземпляр может использоваться многими материалами.
/// </summary>
public sealed class TextureData
{
    private const ulong BytesPerPixel = 4;

    private readonly byte[] _pixels;

    /// <summary>Копирует пиксели; <paramref name="sourcePath"/>, если задан, приводится к полному пути.</summary>
    /// <exception cref="ArgumentException">
    /// Ширина или высота не положительны; размер буфера не равен width*height*4; путь пустой или некорректный.
    /// </exception>
    public TextureData(int width, int height, ReadOnlySpan<byte> rgba, string? sourcePath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        // ulong: при width = height = int.MaxValue произведение 2^64 − 2^34 + 4 не помещается ни в int, ни в long.
        ulong expectedLength = (ulong)width * (ulong)height * BytesPerPixel;
        if ((ulong)rgba.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} bytes of RGBA8 data for a {width}x{height} texture, got {rgba.Length}.",
                nameof(rgba));
        }

        if (sourcePath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            SourcePath = Path.GetFullPath(sourcePath);
        }

        Width = width;
        Height = height;
        _pixels = rgba.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Пиксели RGBA8 только для чтения, строки сверху вниз.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Полный путь к исходному файлу изображения или null, если текстура создана не из файла.</summary>
    public string? SourcePath { get; }
}
