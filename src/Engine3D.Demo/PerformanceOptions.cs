using System.Globalization;

namespace Engine3D.Demo;

/// <summary>Отдельный режим: параметры MVP/JSON не могут незаметно изменить нагрузку.</summary>
internal sealed record PerformanceOptions
{
    internal const string Usage = """
        Сценарий B #14: 1000 текстурированных кубов, общий mesh/material/texture, обязательный свет.
        dotnet run --project src/Engine3D.Demo -c Release -- --performance [параметры]

          --output <каталог>    Новый каталог результатов (не должен существовать).
                                По умолчанию artifacts/performance/<UTC timestamp>.
          --size WxH            Размер клиентской области; по умолчанию 1280x720.
          --vsync on|off        По умолчанию off; драйвер/композитор могут ограничить частоту.
          --warmup <секунды>    Не менее 10; по умолчанию 10.
          --duration <секунды>  Не менее 60; по умолчанию 60.
          --hardware <описание> CPU, RAM, GPU, драйвер, дисплей, питание и режим нагрузки.
          --help               Справка без окна и записи файлов.

        Не меняйте размер/масштаб окна и не сворачивайте его. Досрочное закрытие — неполный замер.
        Для финальной приёмки нужен целевой Linux-ПК; локальный запуск её не заменяет.
        """;

    internal int Width { get; private init; } = 1280;
    internal int Height { get; private init; } = 720;
    internal bool VSync { get; private init; }
    internal double WarmupSeconds { get; private init; } = 10;
    internal double DurationSeconds { get; private init; } = 60;
    internal string Hardware { get; private init; } = "не указано — требуется для приёмки";
    internal string OutputDirectory { get; private init; } = Path.Combine("artifacts", "performance", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
    internal bool Help { get; private init; }

    internal static PerformanceOptions Parse(string[] args)
    {
        var options = new PerformanceOptions();
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (!supplied.Add(name))
                throw new ArgumentException($"Параметр {name} указан несколько раз.");
            switch (name)
            {
                case "--help": options = options with { Help = true }; break;
                case "--output": options = options with { OutputDirectory = Path.GetFullPath(Value()) }; break;
                case "--hardware": options = options with { Hardware = Value() }; break;
                case "--vsync":
                    options = options with
                    {
                        VSync = Value() switch
                        {
                            "on" => true,
                            "off" => false,
                            _ => throw new ArgumentException("--vsync требует on или off."),
                        }
                    };
                    break;
                case "--warmup": options = options with { WarmupSeconds = Seconds(10) }; break;
                case "--duration": options = options with { DurationSeconds = Seconds(60) }; break;
                case "--size":
                    string[] size = Value().Split('x', 'X');
                    if (size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height)
                        || width <= 0 || height <= 0)
                        throw new ArgumentException("--size требует положительные WxH.");
                    options = options with { Width = width, Height = height };
                    break;
                default: throw new ArgumentException($"Неизвестный параметр performance '{name}'. Используйте --performance --help.");
            }

            string Value()
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"После {name} требуется значение.");
                return args[i];
            }

            double Seconds(double minimum)
            {
                if (!double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                    || !double.IsFinite(seconds) || seconds < minimum || seconds > 86400)
                    throw new ArgumentException($"{name}: ожидаются секунды от {minimum} до 86400 (точка как разделитель).");
                return seconds;
            }
        }
        return options;
    }
}
