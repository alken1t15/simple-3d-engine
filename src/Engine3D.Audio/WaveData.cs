using System.Buffers.Binary;

namespace Engine3D.Audio;

internal sealed record WaveData(byte[] Samples, int SampleRate, int Channels)
{
    internal double DurationSeconds => Samples.Length / (double)(SampleRate * Channels * 2);

    internal static WaveData Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        try
        {
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x46464952) throw Invalid(path, "ожидается RIFF");
            long end = 8L + reader.ReadUInt32();
            if (reader.ReadUInt32() != 0x45564157 || end != stream.Length || end < 12)
                throw Invalid(path, "неверная граница RIFF/WAVE");
            int channels = 0, sampleRate = 0;
            long dataOffset = -1;
            uint dataSize = 0;
            bool formatSeen = false;
            while (stream.Position < end)
            {
                if (end - stream.Position < 8) throw Invalid(path, "обрезан заголовок chunk");
                uint kind = reader.ReadUInt32(), size = reader.ReadUInt32();
                long chunkEnd = stream.Position + size;
                long paddedEnd = chunkEnd + (size & 1);
                if (paddedEnd > end) throw Invalid(path, "chunk выходит за RIFF");
                if (kind == 0x20746d66) // fmt
                {
                    if (formatSeen || size < 16 || size == 17) throw Invalid(path, "неверный fmt chunk");
                    formatSeen = true;
                    ushort encoding = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    uint rate = reader.ReadUInt32(), bytesPerSecond = reader.ReadUInt32();
                    ushort alignment = reader.ReadUInt16(), bits = reader.ReadUInt16();
                    if (encoding != 1 || channels is not (1 or 2) || bits != 16 || rate is < 8000 or > 48000
                        || alignment != channels * 2 || bytesPerSecond != rate * alignment)
                        throw Invalid(path, "поддерживается PCM16 mono/stereo 8–48 kHz с согласованными blockAlign/byteRate");
                    if (size >= 18 && reader.ReadUInt16() != size - 18)
                        throw Invalid(path, "неверная длина fmt extension");
                    sampleRate = (int)rate;
                }
                else if (kind == 0x61746164) // data
                {
                    if (dataOffset >= 0) throw Invalid(path, "повторный data chunk");
                    dataOffset = stream.Position;
                    dataSize = size;
                }
                stream.Position = paddedEnd;
            }
            if (!formatSeen || dataOffset < 0 || dataSize == 0 || dataSize > int.MaxValue || dataSize % (channels * 2) != 0)
                throw Invalid(path, "нет корректных fmt/data или данные не выровнены по sample frame");
            stream.Position = dataOffset;
            byte[] data = new byte[(int)dataSize];
            stream.ReadExactly(data);
            // OpenAL принимает PCM в native byte order, файл всегда little-endian.
            if (!BitConverter.IsLittleEndian)
                for (int i = 0; i < data.Length; i += 2)
                    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i, 2), BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i, 2)));
            return new WaveData(data, sampleRate, channels);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"WAV '{path}': файл обрезан", exception);
        }
    }

    private static InvalidDataException Invalid(string path, string reason) => new($"WAV '{path}': {reason}");
}
