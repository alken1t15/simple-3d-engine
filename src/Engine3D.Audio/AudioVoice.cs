namespace Engine3D.Audio;

/// <summary>Одно воспроизведение клипа. Resume продолжает паузу, не запускает остановленную voice.</summary>
public sealed class AudioVoice : IDisposable
{
    private readonly AudioDevice owner;
    private readonly int source;
    private bool paused;
    private bool stopped;
    private bool sourceReleased;
    private bool disposed;
    internal AudioClip Clip { get; }
    internal bool Loop { get; }
    internal bool Finished => stopped || !Loop && !paused && owner.Backend.IsFinished(source);

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
        if (sourceReleased || paused || Finished) return;
        owner.Backend.Pause(source);
        paused = true;
    }

    /// <summary>Продолжает только приостановленную voice.</summary>
    public void Resume()
    {
        CheckAccess();
        if (sourceReleased || !paused || stopped) return;
        owner.Backend.Resume(source);
        paused = false;
    }

    /// <summary>Останавливает voice; для нового воспроизведения нужен Play.</summary>
    public void Stop()
    {
        CheckAccess();
        if (sourceReleased || stopped) return;
        owner.Backend.Stop(source);
        stopped = true;
        paused = false;
    }

    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        owner.CheckAccess();
        Clip.ThrowIfDisposed();
    }

    /// <summary>Останавливает и удаляет source; повторный вызов безопасен.</summary>
    public void Dispose()
    {
        if (disposed) return;
        // Убранный source уже не принадлежит списку устройства. Его handle всё
        // равно становится недействителен после Dispose клипа/устройства.
        if (sourceReleased && (owner.IsDisposed || Clip.IsDisposed))
        {
            disposed = true;
            return;
        }
        owner.CheckAccess();
        DisposeCore();
    }

    internal void DisposeCore()
    {
        if (disposed) return;
        disposed = true;
        ReleaseSource();
    }

    // Автоматическая уборка освобождает native ресурс, но оставляет handle клиента
    // пригодным для повторных Stop/Pause/Resume до явного Dispose его владельцев.
    internal void ReleaseSource()
    {
        if (sourceReleased) return;
        sourceReleased = true;
        stopped = true;
        paused = false;
        var errors = new List<Exception>();
        AudioCleanup.Attempt(() => owner.Backend.Stop(source), errors);
        AudioCleanup.Attempt(() => owner.Backend.DeleteSource(source), errors);
        owner.Remove(this);
        AudioCleanup.ThrowIfFailed(errors);
    }
}
