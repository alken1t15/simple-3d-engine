using Engine3D.Audio;

if (args.Length != 1 || args[0] is not ("--interactive" or "--probe" or "--expect-silent"))
{
    Console.WriteLine("AudioSample: --interactive | --probe | --expect-silent");
    Console.WriteLine("--interactive: ручная проверка звука. Остальные режимы звук не воспроизводят.");
    return args is ["--help"] ? 0 : 2;
}

string directory = Path.Combine(Path.GetTempPath(), $"engine-audio-sample-{Guid.NewGuid():N}");
Directory.CreateDirectory(directory);
try
{
    using var device = AudioDevice.Create(new AudioOptions { AllowSilentFallback = args[0] == "--expect-silent" });
    Console.WriteLine($"IsSilent={device.IsSilent}; {device.Diagnostic ?? "OpenAL device ready"}");
    if (args[0] == "--expect-silent" && !device.IsSilent)
        throw new InvalidOperationException("Ожидался silent mode. Запустите с ALSOFT_DRIVERS=unavailable.");
    string musicPath = Path.Combine(directory, "tone-loop.wav");
    WriteTone(musicPath, 220d, stereo: true);
    using var music = device.LoadWave(musicPath);
    using var loop = device.Play(music, loop: true, volume: args[0] == "--interactive" ? 0.6f : 0f);

    if (args[0] == "--interactive")
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Нужен интерактивный терминал");
        Console.WriteLine("Фон — негромкий низкий тон. Включите звук на ноутбуке.");
        Continue("Слышен повторяющийся низкий тон?");
        for (int i = 0; i < 8; i++)
        {
            string effectPath = Path.Combine(directory, $"effect-{i}.wav");
            WriteTone(effectPath, 440d + 55d * i, stereo: false);
            var effect = device.LoadWave(effectPath);
            device.Play(effect, volume: 0.15f);
        }
        Continue("Прозвучали восемь эффектов поверх продолжающегося фона?");
        loop.Pause();
        Continue("Фон на паузе. После окончания коротких эффектов должна быть тишина.");
        loop.Resume();
        Continue("Фон возобновился без запуска второго loop?");
        device.MasterVolume = 0.25f;
        Continue("Фон стал тише?");
        device.MasterVolume = 0f;
        Continue("Mute: наступила тишина?");
        device.MasterVolume = 1f;
        Continue("Фон снова слышен с исходной громкостью?");
        device.StopAll();
        Continue("StopAll: тишина. Далее освобождаются клипы и устройство.");
        Console.WriteLine("Запишите фактические результаты; Enter подтверждает только переход, а не PASS.");
    }
    else
    {
        for (int i = 0; i < 8; i++) using (device.Play(music, volume: 0f)) { }
        loop.Pause(); loop.Resume();
        device.MasterVolume = 0f;
        device.MasterVolume = 1f;
        device.StopAll();
        Console.WriteLine("PASS: публичный API без воспроизведения; это не проверка слышимости.");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally { Directory.Delete(directory, recursive: true); }

static void Continue(string message)
{
    Console.WriteLine(message);
    Console.WriteLine("Enter — следующий этап; Ctrl+C — выход.");
    if (Console.ReadLine() is null) throw new EndOfStreamException("Терминал закрыт");
}

static void WriteTone(string path, double frequency, bool stereo)
{
    const int rate = 24000, frames = rate;
    int channels = stereo ? 2 : 1;
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write("RIFF"u8); writer.Write(36 + frames * channels * 2); writer.Write("WAVEfmt "u8);
    writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)channels);
    writer.Write(rate); writer.Write(rate * channels * 2); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
    writer.Write("data"u8); writer.Write(frames * channels * 2);
    for (int i = 0; i < frames; i++)
    {
        double envelope = Math.Min(1d, Math.Min(i, frames - 1 - i) / 240d);
        short sample = (short)(6500d * envelope * Math.Sin(2d * Math.PI * frequency * i / rate));
        for (int channel = 0; channel < channels; channel++) writer.Write(sample);
    }
}
