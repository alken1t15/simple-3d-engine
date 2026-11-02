using OpenTK.Audio.OpenAL;

namespace Engine3D.Audio;

internal sealed class OpenAlBackend : IAudioBackend
{
    private static readonly object ContextGate = new();
    private readonly ALDevice device;
    private readonly ALContext context;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly HashSet<int> sources = [];
    private readonly HashSet<int> buffers = [];
    private bool disposed;

    private OpenAlBackend(ALDevice device, ALContext context) { this.device = device; this.context = context; }

    internal static OpenAlBackend Open()
    {
        lock (ContextGate) return OpenCore();
    }

    private static OpenAlBackend OpenCore()
    {
        var device = ALC.OpenDevice(null);
        if (device == ALDevice.Null) throw new InvalidOperationException("OpenAL: NoDevice, устройство вывода недоступно");
        var context = ALContext.Null;
        var previous = ALC.GetCurrentContext();
        try
        {
            context = ALC.CreateContext(device, new ALContextAttributes { MonoSources = 32, StereoSources = 32 });
            if (context == ALContext.Null || !ALC.MakeContextCurrent(context))
                throw new InvalidOperationException($"OpenAL: не создан аудиоконтекст ({ALC.GetError(device)})");
            AL.Listener(ALListenerf.Gain, 1f);
            Check();
            if (!ALC.MakeContextCurrent(previous)) throw new InvalidOperationException("OpenAL: не восстановлен прежний контекст");
            return new OpenAlBackend(device, context);
        }
        catch
        {
            ALC.MakeContextCurrent(previous);
            if (context != ALContext.Null) ALC.DestroyContext(context);
            ALC.CloseDevice(device);
            throw;
        }
    }

    public int LoadBuffer(WaveData wave)
    {
        using var scope = Enter();
        int buffer = AL.GenBuffer();
        try
        {
            Check();
            if (buffer == 0) throw new InvalidOperationException("OpenAL: не выделен buffer");
            buffers.Add(buffer);
            AL.BufferData(buffer, wave.Channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16, wave.Samples, wave.SampleRate);
            Check();
            return buffer;
        }
        catch
        {
            if (buffer != 0)
            {
                AL.DeleteBuffer(buffer);
                if (AL.GetError() == ALError.NoError) buffers.Remove(buffer);
            }
            throw;
        }
    }

    public void DeleteBuffer(int buffer)
    {
        using var scope = Enter();
        AL.DeleteBuffer(buffer);
        Check();
        buffers.Remove(buffer);
    }

    public int Play(int buffer, bool loop, float volume)
    {
        using var scope = Enter();
        int source = AL.GenSource();
        try
        {
            Check();
            if (source == 0) throw new InvalidOperationException("OpenAL: не выделен source");
            sources.Add(source);
            AL.Source(source, ALSourcei.Buffer, buffer);
            AL.Source(source, ALSourceb.SourceRelative, true);
            AL.Source(source, ALSourcef.RolloffFactor, 0f);
            AL.Source(source, ALSourcef.Gain, volume);
            AL.Source(source, ALSourceb.Looping, loop);
            AL.SourcePlay(source);
            Check();
            return source;
        }
        catch
        {
            if (source != 0)
            {
                AL.SourceStop(source);
                AL.GetError();
                AL.DeleteSource(source);
                if (AL.GetError() == ALError.NoError) sources.Remove(source);
            }
            throw;
        }
    }

    public bool IsFinished(int source)
    {
        using var scope = Enter();
        var state = (ALSourceState)AL.GetSource(source, ALGetSourcei.SourceState);
        Check();
        return state == ALSourceState.Stopped;
    }

    public void Pause(int source) { using var scope = Enter(); AL.SourcePause(source); Check(); }
    public void Resume(int source) { using var scope = Enter(); AL.SourcePlay(source); Check(); }
    public void Stop(int source) { using var scope = Enter(); AL.SourceStop(source); Check(); }
    public void SetMasterVolume(float volume) { using var scope = Enter(); AL.Listener(ALListenerf.Gain, volume); Check(); }

    public void DeleteSource(int source)
    {
        using var scope = Enter();
        AL.DeleteSource(source);
        Check();
        sources.Remove(source);
    }

    private ContextScope Enter()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        CheckThread();
        Monitor.Enter(ContextGate);
        try
        {
            var previous = ALC.GetCurrentContext();
            if (previous != context && !ALC.MakeContextCurrent(context))
                throw new InvalidOperationException("OpenAL: не удалось активировать аудиоконтекст");
            return new ContextScope(previous, previous != context);
        }
        catch { Monitor.Exit(ContextGate); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        CheckThread();
        lock (ContextGate) DisposeCore();
    }

    private void DisposeCore()
    {
        var previous = ALC.GetCurrentContext();
        var errors = new List<Exception>();
        disposed = true;
        bool current = ALC.MakeContextCurrent(context);
        if (!current) errors.Add(new InvalidOperationException("OpenAL: не активирован контекст для cleanup"));
        if (current)
        {
            foreach (int source in sources)
            {
                AudioCleanup.Attempt(() => { AL.SourceStop(source); Check(); }, errors);
                AudioCleanup.Attempt(() => { AL.DeleteSource(source); Check(); }, errors);
            }
            foreach (int buffer in buffers)
                AudioCleanup.Attempt(() => { AL.DeleteBuffer(buffer); Check(); }, errors);
        }
        sources.Clear();
        buffers.Clear();
        AudioCleanup.Attempt(() =>
        {
            if (!ALC.MakeContextCurrent(previous == context ? ALContext.Null : previous))
                throw new InvalidOperationException("OpenAL: не восстановлен контекст при cleanup");
        }, errors);
        AudioCleanup.Attempt(() => ALC.DestroyContext(context), errors);
        AudioCleanup.Attempt(() =>
        {
            if (!ALC.CloseDevice(device)) throw new InvalidOperationException("OpenAL: не закрыто устройство");
        }, errors);
        AudioCleanup.ThrowIfFailed(errors);
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("AudioDevice доступен только на потоке создания");
    }

    private static void Check()
    {
        var error = AL.GetError();
        if (error != ALError.NoError) throw new InvalidOperationException($"OpenAL: {error}");
    }

    private readonly struct ContextScope(ALContext previous, bool changed) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (changed && !ALC.MakeContextCurrent(previous))
                    throw new InvalidOperationException("OpenAL: не восстановлен прежний аудиоконтекст");
            }
            finally { Monitor.Exit(ContextGate); }
        }
    }
}
