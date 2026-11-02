namespace Engine3D.Audio;

/// <summary>Одно воспроизведение клипа. Resume продолжает паузу, не запускает остановленную voice.</summary>
public sealed class AudioVoice : IDisposable
{
    private readonly AudioDevice owner;
    private readonly int source;
    private bool paused;
    private bool stopped;
    private bool disposed;
    internal AudioClip Clip { get; }
    internal bool Loop { get; }
    internal bool Finished => stopped || owner.Backend.IsFinished(source);

    internal AudioVoice(AudioDevice owner, AudioClip clip, int source, bool loop)
    {
        this.owner = owner;
        Clip = clip;
        this.source = source;
        Loop = loop;
    }

    /// <summary>Приостанавливает активное воспроизведение; повторный вызов безопасен.</summary>
    public void Pause()
    {
        CheckAccess();
        if (paused || Finished) return;
        owner.Backend.Pause(source);
        paused = true;
    }

    /// <summary>Продолжает только приостановленную voice.</summary>
    public void Resume()
    {
        CheckAccess();
        if (!paused || stopped) return;
        owner.Backend.Resume(source);
        paused = false;
    }

    /// <summary>Останавливает voice; для нового воспроизведения нужен Play.</summary>
    public void Stop()
    {
        CheckAccess();
        if (stopped) return;
        owner.Backend.Stop(source);
        stopped = true;
        paused = false;
    }

    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        owner.CheckAccess();
    }

    /// <summary>Останавливает и удаляет source; повторный вызов безопасен.</summary>
    public void Dispose()
    {
        if (disposed) return;
        owner.CheckAccess();
        DisposeCore();
    }

    internal void DisposeCore()
    {
        if (disposed) return;
        disposed = true;
        var errors = new List<Exception>();
        AudioCleanup.Attempt(() => owner.Backend.Stop(source), errors);
        AudioCleanup.Attempt(() => owner.Backend.DeleteSource(source), errors);
        owner.Remove(this);
        AudioCleanup.ThrowIfFailed(errors);
    }
}
