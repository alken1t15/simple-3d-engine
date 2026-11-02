using Engine3D.Audio;

namespace Engine3D.Tests;

internal sealed class AudioFile : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"engine-audio-{Guid.NewGuid():N}.wav");

    internal AudioFile(byte[]? bytes = null) => File.WriteAllBytes(Path, bytes ?? Wave());
    public void Dispose() => File.Delete(Path);

    internal static byte[] Wave(ushort channels = 1, uint rate = 24000, ushort bits = 16,
        ushort encoding = 1, byte[]? samples = null, bool dataFirst = false, bool unknownChunk = false)
    {
        samples ??= new byte[4800 * channels];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(0u);
        writer.Write("WAVE"u8);
        if (unknownChunk)
        {
            writer.Write("JUNK"u8);
            writer.Write(3u);
            writer.Write(new byte[] { 1, 2, 3, 0 });
        }
        void Data()
        {
            writer.Write("data"u8);
            writer.Write((uint)samples.Length);
            writer.Write(samples);
            if (samples.Length % 2 != 0) writer.Write((byte)0);
        }
        if (dataFirst) Data();
        writer.Write("fmt "u8);
        writer.Write(16u);
        writer.Write(encoding);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8));
        writer.Write(bits);
        if (!dataFirst) Data();
        long end = stream.Length;
        stream.Position = 4;
        writer.Write((uint)(end - 8));
        return stream.ToArray();
    }
}

internal sealed class RecordingAudioBackend : IAudioBackend
{
    internal readonly Dictionary<int, float> Sources = [];
    internal readonly HashSet<int> Buffers = [];
    internal readonly HashSet<int> Completed = [];
    internal readonly List<string> Calls = [];
    internal float Master = 1f;
    internal string? FailOn;
    internal bool FailOnce;
    private int id;

    private void Call(string name)
    {
        Calls.Add(name);
        if (FailOn == name)
        {
            if (FailOnce) FailOn = null;
            throw new InvalidOperationException($"injected {name}");
        }
    }

    public int LoadBuffer(WaveData wave) { Call("load"); int buffer = ++id; Buffers.Add(buffer); return buffer; }
    public void DeleteBuffer(int buffer) { Call("delete-buffer"); Buffers.Remove(buffer); }
    public int Play(int buffer, bool loop, float volume) { Call("play"); int source = ++id; Sources.Add(source, volume); return source; }
    public bool IsFinished(int source) { Call("finished"); return Completed.Contains(source); }
    public void Pause(int source) => Call("pause");
    public void Resume(int source) => Call("resume");
    public void Stop(int source) { Call("stop"); Completed.Add(source); }
    public void DeleteSource(int source) { Call("delete-source"); Sources.Remove(source); }
    public void SetMasterVolume(float volume) { Call("gain"); Master = volume; }
    public void Dispose() { Call("dispose"); Sources.Clear(); Buffers.Clear(); }
}
