namespace Engine3D.Audio;

/// <summary>Загруженный звук, принадлежащий одному AudioDevice.</summary>
public sealed class AudioClip : IDisposable
{
    internal AudioDevice Owner { get; }
    internal int Buffer { get; }
    private bool disposed;
    internal bool IsDisposed => disposed;

    internal AudioClip(AudioDevice owner, int buffer) { Owner = owner; Buffer = buffer; }
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    /// <summary>Останавливает связанные voices и освобождает buffer; повтор безопасен.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Owner.CheckAccess();
        DisposeCore();
    }

    internal void DisposeCore()
    {
        if (disposed) return;
        disposed = true;
        Owner.Release(this);
    }
}
