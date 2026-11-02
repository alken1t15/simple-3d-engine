using OpenTK.Audio.OpenAL;

if (args.Length != 1 || args[0] is not ("--probe" or "--audible" or "--expect-no-device"))
{
    Console.WriteLine("OpenAL spike: --probe | --audible | --expect-no-device");
    return 2;
}

var device = ALC.OpenDevice(null);
if (device == ALDevice.Null)
{
    Console.WriteLine("NoDevice: OpenAL не открыл устройство.");
    return args[0] == "--expect-no-device" ? 0 : 1;
}
if (args[0] == "--expect-no-device")
{
    ALC.CloseDevice(device);
    Console.Error.WriteLine("Ожидалось недоступное устройство; проверьте настройки процесса.");
    return 1;
}

var previous = ALC.GetCurrentContext();
var context = ALContext.Null;
var sources = new List<int>();
var buffers = new List<int>();
try
{
    context = ALC.CreateContext(device, new ALContextAttributes { MonoSources = 32, StereoSources = 8 });
    if (context == ALContext.Null || !ALC.MakeContextCurrent(context))
        throw new InvalidOperationException($"Ошибка context: {ALC.GetError(device)}");
    for (int i = 0; i < 32; i++)
    {
        sources.Add(AL.GenSource());
        Check();
    }
    Console.WriteLine("PASS: device/context, 32 sources.");
    if (args[0] == "--audible")
    {
        for (int i = 0; i < 9; i++)
        {
            short[] samples = new short[24000];
            double frequency = i == 0 ? 220d : 440d + 55d * i;
            for (int sample = 0; sample < samples.Length; sample++)
            {
                double envelope = Math.Min(1d, Math.Min(sample, samples.Length - 1 - sample) / 240d);
                samples[sample] = (short)(4000d * envelope * Math.Sin(2d * Math.PI * frequency * sample / 24000d));
            }
            int buffer = AL.GenBuffer();
            buffers.Add(buffer);
            AL.BufferData(buffer, ALFormat.Mono16, samples, 24000);
            AL.Source(sources[i], ALSourcei.Buffer, buffer);
            AL.Source(sources[i], ALSourcef.Gain, i == 0 ? 0.15f : 0.08f);
            AL.Source(sources[i], ALSourceb.Looping, i == 0);
            AL.SourcePlay(sources[i]);
            Check();
        }
        Console.WriteLine("8 effects + quiet loop (2 seconds)");
        Thread.Sleep(2000);
        AL.SourcePause(sources[0]);
        Check();
        if ((ALSourceState)AL.GetSource(sources[0], ALGetSourcei.SourceState) != ALSourceState.Paused)
            throw new InvalidOperationException("Pause не изменил состояние");
        Console.WriteLine("Pause (1 second)");
        Thread.Sleep(1000);
        AL.SourcePlay(sources[0]);
        AL.Listener(ALListenerf.Gain, 0.25f);
        Check();
        Console.WriteLine("Resume, quarter master (1 second)");
        Thread.Sleep(1000);
        AL.Listener(ALListenerf.Gain, 0f);
        Check();
        Console.WriteLine("Mute (1 second)");
        Thread.Sleep(1000);
        AL.Listener(ALListenerf.Gain, 1f);
        Check();
        Console.WriteLine("Unmute (1 second)");
        Thread.Sleep(1000);
    }
    Console.WriteLine("PASS: spike; слышимость подтверждается человеком отдельно.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
finally
{
    foreach (int source in sources)
    {
        AL.SourceStop(source);
        AL.DeleteSource(source);
    }
    foreach (int buffer in buffers) AL.DeleteBuffer(buffer);
    if (context != ALContext.Null)
    {
        ALC.MakeContextCurrent(previous);
        ALC.DestroyContext(context);
    }
    ALC.CloseDevice(device);
}

static void Check()
{
    var error = AL.GetError();
    if (error != ALError.NoError) throw new InvalidOperationException($"OpenAL: {error}");
}
