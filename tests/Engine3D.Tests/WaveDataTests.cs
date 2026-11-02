using System.Buffers.Binary;
using Engine3D.Audio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class WaveDataTests
{
    [TestMethod]
    [DataRow((ushort)1, 8000u)]
    [DataRow((ushort)2, 48000u)]
    public void Pcm16AndRiffChunkOrderAreAccepted(ushort channels, uint rate)
    {
        using var file = new AudioFile(AudioFile.Wave(channels, rate, dataFirst: true, unknownChunk: true));
        var data = WaveData.Load(file.Path);
        Assert.AreEqual(channels, data.Channels);
        Assert.AreEqual((int)rate, data.SampleRate);
        Assert.AreEqual(2400d / rate, data.DurationSeconds, 1e-12d);
        Assert.HasCount(4800 * channels, data.Samples);
    }

    [TestMethod]
    public void UnsupportedFormatsAreRejectedWithPath()
    {
        byte[][] invalid =
        [
            AudioFile.Wave(channels: 0), AudioFile.Wave(channels: 3),
            AudioFile.Wave(rate: 7999), AudioFile.Wave(rate: 48001),
            AudioFile.Wave(bits: 8), AudioFile.Wave(bits: 24), AudioFile.Wave(encoding: 3),
            AudioFile.Wave(samples: []), AudioFile.Wave(samples: [0, 1, 2]),
        ];
        foreach (byte[] bytes in invalid)
        {
            using var file = new AudioFile(bytes);
            StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => WaveData.Load(file.Path)).Message, file.Path);
        }
    }

    [TestMethod]
    public void TruncationAndRiffChunkOverflowAreRejected()
    {
        var original = AudioFile.Wave();
        foreach (int length in new[] { 0, 1, 7, 11, 20, original.Length - 1 })
        {
            using var file = new AudioFile(original[..length]);
            Assert.ThrowsExactly<InvalidDataException>(() => WaveData.Load(file.Path));
        }
        foreach (int offset in new[] { 4, 16, 40 })
        {
            byte[] changed = (byte[])original.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(offset), uint.MaxValue);
            using var file = new AudioFile(changed);
            Assert.ThrowsExactly<InvalidDataException>(() => WaveData.Load(file.Path));
        }
    }

    [TestMethod]
    public void MissingChunksWrongAlignmentAndDuplicateChunksAreRejected()
    {
        byte[][] variants = Enumerable.Range(0, 6).Select(_ => AudioFile.Wave()).ToArray();
        variants[0][0] = (byte)'X';
        variants[1][8] = (byte)'X';
        variants[2][12] = (byte)'X'; // fmt becomes unknown
        variants[3][36] = (byte)'X'; // data becomes unknown
        variants[4][28] = 1; // byteRate inconsistent
        variants[5][32] = 1; // blockAlign inconsistent
        foreach (var bytes in variants)
        {
            using var file = new AudioFile(bytes);
            Assert.ThrowsExactly<InvalidDataException>(() => WaveData.Load(file.Path));
        }
        var original = AudioFile.Wave();
        byte[] duplicate = [.. original, .. original[12..36]];
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate.AsSpan(4), (uint)duplicate.Length - 8);
        using var twice = new AudioFile(duplicate);
        Assert.ThrowsExactly<InvalidDataException>(() => WaveData.Load(twice.Path));
        using var missing = new AudioFile();
        File.Delete(missing.Path);
        Assert.ThrowsExactly<FileNotFoundException>(() => WaveData.Load(missing.Path));
        Assert.ThrowsExactly<ArgumentNullException>(() => WaveData.Load(null!));
    }
}
