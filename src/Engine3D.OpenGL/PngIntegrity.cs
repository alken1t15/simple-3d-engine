using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Engine3D.OpenGL;

/// <summary>Проверки целостности, которые StbImageSharp не выполняет: границы чанков, CRC и zlib/Adler-32.</summary>
internal static class PngIntegrity
{
    // CRC-32 PNG: отраженный полином, initial/final xor 0xffffffff.
    // https://www.w3.org/TR/png-3/#5CRC-algorithm — собственная реализация, без нового пакета.
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static MemoryStream ReadImageData(ReadOnlySpan<byte> encoded)
    {
        var compressed = new MemoryStream();
        try
        {
            int offset = 8; // сигнатура уже проверена TextureDecoder
            bool header = false, imageData = false;
            while (offset < encoded.Length)
            {
                if (encoded.Length - offset < 12)
                    throw new InvalidDataException("Truncated PNG chunk header or checksum.");
                uint length = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset, 4));
                var type = encoded.Slice(offset + 4, 4);
                foreach (byte letter in type)
                    if (letter is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                        throw new InvalidDataException("Invalid PNG chunk type.");
                string name = Encoding.ASCII.GetString(type);
                if (length > (uint)(encoded.Length - offset - 12))
                    throw new InvalidDataException($"Truncated PNG chunk {name} data or checksum.");
                int size = (int)length; // ограничено оставшимся размером массива, переполнение невозможно
                uint expected = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset + 8 + size, 4));
                if (Crc(encoded.Slice(offset + 4, size + 4)) != expected)
                    throw new InvalidDataException($"PNG chunk {name} has invalid CRC-32.");
                if (!header && (name != "IHDR" || size != 13))
                    throw new InvalidDataException("PNG must start with a 13-byte IHDR chunk.");
                if (name == "IHDR")
                {
                    if (header)
                        throw new InvalidDataException("Duplicate PNG IHDR chunk.");
                    header = true;
                }
                if (name == "IDAT")
                {
                    imageData = true;
                    compressed.Write(encoded.Slice(offset + 8, size));
                }
                if (name == "IEND")
                {
                    if (size != 0 || !imageData)
                        throw new InvalidDataException("Invalid PNG IEND or missing IDAT chunk.");
                    compressed.Position = 0;
                    return compressed;
                }
                offset += size + 12;
            }
            throw new InvalidDataException("PNG is incomplete: missing IEND chunk.");
        }
        catch
        {
            compressed.Dispose();
            throw;
        }
    }

    public static void ValidateImageData(Stream compressed, int width, int height)
    {
        // Проверяется только после успешного декодирования размеров. Буфер фиксированный, результат inflate
        // не сохраняется. Верхняя граница: RGBA16 (8 байт/пиксель) + строки фильтров всех 7 Adam7-проходов.
        ulong limit = (ulong)width * (ulong)height * 8 + (ulong)height * 7;
        ulong total = 0;
        // ZLibStream принимает EOF исходного потока даже без полного Adler-32. Не отдаём ему такой EOF:
        // корректный inflater возвращает 0 сам, достигнув Z_STREAM_END, без нового чтения исходного потока.
        // Если inflater всё ещё требует вход, отсутствие байтов — повреждение, а не успешное завершение.
        using var input = new RequireCompleteZlibStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
        Span<byte> buffer = stackalloc byte[4096];
        try
        {
            int count;
            while ((count = zlib.Read(buffer)) != 0)
            {
                total += (uint)count;
                if (total > limit)
                    throw new InvalidDataException("PNG IDAT expands beyond the image dimensions.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("PNG IDAT has invalid zlib data or Adler-32 checksum.", exception);
        }
    }

    /// <summary>Запрещает inflater подменять завершение zlib физическим EOF (буферизация не влияет на проверку).</summary>
    private sealed class RequireCompleteZlibStream(Stream source) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
                return 0;
            int count = source.Read(buffer);
            if (count == 0)
                throw new InvalidDataException("PNG IDAT has an incomplete zlib stream or missing Adler-32 trailer.");
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
            crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            table[i] = crc;
        }
        return table;
    }
}
