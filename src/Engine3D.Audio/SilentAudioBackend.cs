using System.Diagnostics;

namespace Engine3D.Audio;

/// <summary>Те же времена жизни без нативных ресурсов; clock подменяется только в тестах.</summary>
internal sealed class SilentAudioBackend : IAudioBackend
{
    private readonly Func<double> now;
    private readonly Dictionary<int, double> buffers = [];
    private readonly Dictionary<int, Voice> voices = [];
    private int nextId;

    internal SilentAudioBackend(Func<double>? clock = null) =>
        now = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

    public int LoadBuffer(WaveData wave)
    {
        int id = ++nextId;
        buffers.Add(id, wave.DurationSeconds);
        return id;
    }

    public void DeleteBuffer(int buffer) => buffers.Remove(buffer);

    public int Play(int buffer, bool loop, float volume)
    {
        int id = ++nextId;
        voices.Add(id, new Voice(buffers[buffer], loop, now()));
        return id;
    }

    public bool IsFinished(int source)
    {
        var voice = voices[source];
        return voice.Stopped || (!voice.Loop && !voice.Paused && now() - voice.Start >= voice.Remaining);
    }

    public void Pause(int source)
    {
        var voice = voices[source];
        if (voice.Paused || IsFinished(source)) return;
        voice.Remaining -= now() - voice.Start;
        voice.Paused = true;
    }

    public void Resume(int source)
    {
        var voice = voices[source];
        if (!voice.Paused || voice.Stopped) return;
        voice.Start = now();
        voice.Paused = false;
    }

    public void Stop(int source) => voices[source].Stopped = true;
    public void DeleteSource(int source) => voices.Remove(source);
    public void SetMasterVolume(float volume) { }
    public void Dispose() { voices.Clear(); buffers.Clear(); }

    private sealed class Voice(double duration, bool loop, double start)
    {
        internal double Remaining = duration;
        internal double Start = start;
        internal readonly bool Loop = loop;
        internal bool Paused;
        internal bool Stopped;
    }
}
