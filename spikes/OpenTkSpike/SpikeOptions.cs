using System.Globalization;

namespace OpenTkSpike;

// Параметры командной строки:
//   dotnet run -- [--frames N] [--cycles N] [--screenshot file.png] [--depth on|off] [--cull on|off|front]
//                 [--texture on|off] [--angle RADIANS] [--fail-at-frame N]
//   --frames N         закрыть окно после N показанных кадров (для автоматических прогонов);
//   --cycles N         N раз подряд создать, показать и закрыть окно в одном процессе (по умолчанию 1);
//   --screenshot PATH  сохранить последний кадр перед закрытием в PNG (требует --frames);
//   --depth on|off     начальное состояние depth test (по умолчанию on; в окне — клавиша D);
//   --cull on|off|front отбрасывать задние грани (on), ничего (off, по умолчанию) или лицевые (front — контрольный
//                      режим, имитирует неверный порядок обхода); в окне клавиша C переключает off/on;
//   --texture on|off   off — грани куба окрашены базовыми цветами вместо текстуры (по умолчанию on; в окне — клавиша T);
//   --angle RADIANS    зафиксировать угол поворота куба (вращение на паузе) — для воспроизводимых кадров;
//   --fail-at-frame N  намеренно вызвать ошибку OpenGL на кадре N, чтобы проверить обработку ошибок.
internal enum CullMode
{
    Off,
    Back,
    Front,
}

internal sealed record SpikeOptions(
    int FrameLimit,
    int Cycles,
    string? ScreenshotPath,
    bool DepthTest,
    CullMode Culling,
    bool Texture,
    float? Angle,
    int FailAtFrame)
{
    public static SpikeOptions Parse(string[] args)
    {
        int frameLimit = 0;
        int cycles = 1;
        string? screenshotPath = null;
        bool depthTest = true;
        var culling = CullMode.Off;
        bool texture = true;
        float? angle = null;
        int failAtFrame = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            string value = i + 1 < args.Length
                ? args[i + 1]
                : throw new ArgumentException($"Missing value for '{name}'.");

            switch (name)
            {
                case "--frames":
                    frameLimit = ParsePositive(name, value);
                    break;
                case "--cycles":
                    cycles = ParsePositive(name, value);
                    break;
                case "--screenshot":
                    screenshotPath = value;
                    break;
                case "--depth":
                    depthTest = ParseSwitch(name, value);
                    break;
                case "--cull":
                    culling = value switch
                    {
                        "on" => CullMode.Back,
                        "off" => CullMode.Off,
                        "front" => CullMode.Front,
                        _ => throw new ArgumentException($"Expected 'on', 'off' or 'front' for {name}, got '{value}'."),
                    };
                    break;
                case "--texture":
                    texture = ParseSwitch(name, value);
                    break;
                case "--angle":
                    angle = float.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "--fail-at-frame":
                    failAtFrame = ParsePositive(name, value);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{name}'.");
            }

            i++;
        }

        if (screenshotPath is not null && frameLimit <= 0)
            throw new ArgumentException("--screenshot requires --frames N.");

        return new SpikeOptions(frameLimit, cycles, screenshotPath, depthTest, culling, texture, angle, failAtFrame);
    }

    private static int ParsePositive(string name, string value)
    {
        int result = int.Parse(value, CultureInfo.InvariantCulture);
        return result >= 1 ? result : throw new ArgumentException($"{name} must be at least 1, got {result}.");
    }

    private static bool ParseSwitch(string name, string value) => value switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"Expected 'on' or 'off' for {name}, got '{value}'."),
    };
}
