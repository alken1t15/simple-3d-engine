namespace Engine3D.Audio;

// Только внутренний стык с нативными ресурсами и детерминированными тестами.
internal interface IAudioBackend : IDisposable
{
    int LoadBuffer(WaveData wave);
    void DeleteBuffer(int buffer);
    int Play(int buffer, bool loop, float volume);
    bool IsFinished(int source);
    void Pause(int source);
    void Resume(int source);
    void Stop(int source);
    void DeleteSource(int source);
    void SetMasterVolume(float volume);
}

internal static class AudioCleanup
{
    internal static void Attempt(Action action, List<Exception> errors)
    {
        try { action(); }
        catch (Exception exception) { errors.Add(exception); }
    }

    internal static void ThrowIfFailed(List<Exception> errors)
    {
        if (errors.Count > 0)
            throw new InvalidOperationException("Не все операции освобождения аудиоресурсов завершились успешно",
                errors.Count == 1 ? errors[0] : new AggregateException(errors));
    }
}
