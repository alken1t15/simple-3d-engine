namespace OpenTkSpike;

// Параметры командной строки: dotnet run -- [--frames N] [--cycles N] [--screenshot file.png] [--depth on|off]
//   --frames N        закрыть окно после N показанных кадров (для автоматических прогонов);
//   --cycles N        N раз подряд создать, показать и закрыть окно в одном процессе (по умолчанию 1);
//   --screenshot PATH сохранить последний кадр перед закрытием в PNG (требует --frames);
//   --depth on|off    начальное состояние depth test (по умолчанию on; в окне переключается клавишей D).
internal sealed record SpikeOptions(int FrameLimit, int Cycles, string? ScreenshotPath, bool DepthTest)
{
    public static SpikeOptions Parse(string[] args)
    {
        int frameLimit = 0;
        int cycles = 1;
        string? screenshotPath = null;
        bool depthTest = true;

        for (int i = 0; i < args.Length; i++)
        {
            string value = i + 1 < args.Length
                ? args[i + 1]
                : throw new ArgumentException($"Missing value for '{args[i]}'.");

            switch (args[i])
            {
                case "--frames":
                    frameLimit = int.Parse(value);
                    break;
                case "--cycles":
                    cycles = int.Parse(value);
                    if (cycles < 1)
                        throw new ArgumentException("--cycles must be at least 1.");
                    break;
                case "--screenshot":
                    screenshotPath = value;
                    break;
                case "--depth":
                    depthTest = value switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => throw new ArgumentException($"Expected 'on' or 'off' for --depth, got '{value}'."),
                    };
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }

            i++;
        }

        if (screenshotPath is not null && frameLimit <= 0)
            throw new ArgumentException("--screenshot requires --frames N.");

        return new SpikeOptions(frameLimit, cycles, screenshotPath, depthTest);
    }
}
