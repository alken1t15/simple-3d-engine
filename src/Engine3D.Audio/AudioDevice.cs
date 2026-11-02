namespace Engine3D.Audio;

/// <summary>Отдельное аудиоустройство; все операции выполняются на потоке Create.</summary>
public sealed class AudioDevice : IDisposable
{
    private const int VoiceLimit = 32;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly IAudioBackend backend;
    private readonly List<AudioVoice> voices = [];
    private readonly HashSet<AudioClip> clips = [];
    private readonly bool silent;
    private readonly string? diagnostic;
    private bool disposed;
    private float masterVolume = 1f;

    /// <summary>Создаёт устройство или диагностируемый silent fallback по options.</summary>
    public static AudioDevice Create(AudioOptions options) => Create(options, OpenAlBackend.Open);

    internal static AudioDevice Create(AudioOptions options, Func<IAudioBackend> open)
    {
        ArgumentNullException.ThrowIfNull(options);
        try { return new AudioDevice(open()); }
        catch (Exception exception) when (Unavailable(exception))
        {
            if (!options.AllowSilentFallback)
                throw new InvalidOperationException("Не удалось создать AudioDevice без silent fallback", exception);
            return new AudioDevice(new SilentAudioBackend(), true, $"Silent audio: {exception.GetBaseException().Message}");
        }
    }

    internal AudioDevice(IAudioBackend backend, bool silent = false, string? diagnostic = null)
    {
        this.backend = backend;
        this.silent = silent;
        this.diagnostic = diagnostic;
    }

    /// <summary>True, если воспроизведение работает без устройства и без звука.</summary>
    public bool IsSilent { get { CheckAccess(); return silent; } }
    /// <summary>Причина silent fallback; null при работающем backend.</summary>
    public string? Diagnostic { get { CheckAccess(); return diagnostic; } }
    /// <summary>Конечный master gain [0,1], немедленно действует на существующие voices.</summary>
    public float MasterVolume
    {
        get { CheckAccess(); return masterVolume; }
        set
        {
            CheckAccess();
            ValidateVolume(value, nameof(value));
            backend.SetMasterVolume(value);
            masterVolume = value;
        }
    }

    /// <summary>Загружает PCM16 WAV mono/stereo 8–48 kHz, в том числе в silent режиме.</summary>
    public AudioClip LoadWave(string path)
    {
        CheckAccess();
        var wave = WaveData.Load(path);
        int buffer;
        try { buffer = backend.LoadBuffer(wave); }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Не удалось загрузить WAV '{path}' в аудиоустройство", exception);
        }
        var clip = new AudioClip(this, buffer);
        clips.Add(clip);
        return clip;
    }

    /// <summary>Создаёт независимую voice, при лимите вытесняет по контракту v1.</summary>
    public AudioVoice Play(AudioClip clip, bool loop = false, float volume = 1f)
    {
        CheckAccess();
        ArgumentNullException.ThrowIfNull(clip);
        ValidateVolume(volume, nameof(volume));
        if (!ReferenceEquals(clip.Owner, this)) throw new ArgumentException("AudioClip принадлежит другому устройству", nameof(clip));
        clip.ThrowIfDisposed();
        // Пока новый source не создан, существующие voices остаются нетронутыми.
        var finished = voices.Where(voice => voice.Finished).ToArray();
        AudioVoice? victim = null;
        if (voices.Count - finished.Length >= VoiceLimit)
        {
            victim = voices.FirstOrDefault(voice => !voice.Loop);
            if (victim is null) throw new InvalidOperationException("Лимит 32 активных loop voices исчерпан");
        }
        int source = backend.Play(clip.Buffer, loop, volume);
        var voice = new AudioVoice(this, clip, source, loop);
        var errors = new List<Exception>();
        foreach (var completed in finished) AudioCleanup.Attempt(completed.DisposeCore, errors);
        if (victim is not null) AudioCleanup.Attempt(victim.DisposeCore, errors);
        if (errors.Count > 0)
        {
            // Не возвращаем и не теряем новую voice, если освобождение старых не удалось.
            AudioCleanup.Attempt(voice.DisposeCore, errors);
            AudioCleanup.ThrowIfFailed(errors);
        }
        voices.Add(voice);
        return voice;
    }

    /// <summary>Останавливает все voices, не освобождая клипы.</summary>
    public void StopAll()
    {
        CheckAccess();
        var errors = new List<Exception>();
        foreach (var voice in voices.ToArray()) AudioCleanup.Attempt(voice.Stop, errors);
        AudioCleanup.ThrowIfFailed(errors);
    }

    internal IAudioBackend Backend => backend;
    internal void Remove(AudioVoice voice) => voices.Remove(voice);
    internal void Release(AudioClip clip)
    {
        var errors = new List<Exception>();
        foreach (var voice in voices.Where(voice => ReferenceEquals(voice.Clip, clip)).ToArray())
            AudioCleanup.Attempt(voice.DisposeCore, errors);
        AudioCleanup.Attempt(() => backend.DeleteBuffer(clip.Buffer), errors);
        clips.Remove(clip);
        AudioCleanup.ThrowIfFailed(errors);
    }

    internal void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("AudioDevice доступен только на потоке создания");
    }

    internal static void ValidateVolume(float volume, string name)
    {
        if (!float.IsFinite(volume) || volume < 0f || volume > 1f)
            throw new ArgumentOutOfRangeException(name, "Громкость должна быть конечной и в [0,1]");
    }

    /// <summary>Освобождает voices, clips, затем backend; повторный вызов безопасен.</summary>
    public void Dispose()
    {
        if (disposed) return;
        CheckAccess();
        disposed = true;
        var errors = new List<Exception>();
        foreach (var voice in voices.ToArray()) AudioCleanup.Attempt(voice.DisposeCore, errors);
        foreach (var clip in clips.ToArray()) AudioCleanup.Attempt(clip.DisposeCore, errors);
        AudioCleanup.Attempt(backend.Dispose, errors);
        AudioCleanup.ThrowIfFailed(errors);
    }

    private static bool Unavailable(Exception exception) => exception is InvalidOperationException
        or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
        || exception is TypeInitializationException && exception.InnerException is not null && Unavailable(exception.InnerException);
}
