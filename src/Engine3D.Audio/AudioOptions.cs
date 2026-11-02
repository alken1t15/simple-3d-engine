namespace Engine3D.Audio;

/// <summary>Политика запуска отдельного аудиосервиса.</summary>
public sealed class AudioOptions
{
    /// <summary>При недоступности устройства продолжать без звука с диагностикой.</summary>
    public bool AllowSilentFallback { get; init; } = true;
}
