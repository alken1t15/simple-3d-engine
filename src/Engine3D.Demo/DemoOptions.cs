using System.Globalization;

namespace Engine3D.Demo;

internal enum DemoScenario { Mvp, Lighting, RoundTrip }

internal sealed class DemoOptions
{
    internal const string Usage = """
        Engine3D Demo #11 — публичный API, без прямых вызовов OpenGL.
        Запуск: dotnet run --project src/Engine3D.Demo -c Release -- [параметры]

          --scenario mvp|lighting|roundtrip  MVP (по умолчанию), свет C или JSON D.
          --texture <PNG>                   Своя текстура; по умолчанию Assets/control.png рядом с программой.
          --scene <JSON>                    Файл для roundtrip; по умолчанию artifacts/demo/scene.json.
          --static                          Неподвижный MVP для сравнения с загруженной сценой.
          --save <JSON>                     Сохранить исходную сцену; требует --static.
          --load <JSON>                     Загрузить и показать неподвижную сцену вместо встроенной.
          --frames <число>                  Завершиться после указанного числа показанных кадров (> 0).
          --position X,Y                    Начальное положение окна в экранных пунктах.
          --size WxH                        Начальный размер клиентской области окна.
          --bounds-out <файл>               Записывать текущие X,Y,W,H при изменении положения/размера.
          --no-focus                        Не забирать фокус окном (для ручного харнесса).
          --help                            Эта справка; окно не создаётся.

        Нагрузочный сценарий B #14: --performance --help (отдельный набор параметров).

        --load совместим с --frames и параметрами окна. --static/--save применяются только к mvp.
        Закройте окно для штатного завершения. Изображения в JSON не встраиваются.
        """;

    internal DemoScenario Scenario { get; private set; } = DemoScenario.Mvp;
    internal string TexturePath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "Assets", "control.png");
    internal string ScenePath { get; private set; } = Path.Combine("artifacts", "demo", "scene.json");
    internal string? LoadPath { get; private set; }
    internal string? SavePath { get; private set; }
    internal bool Static { get; private set; }
    internal bool NoFocus { get; private set; }
    internal (int X, int Y)? Position { get; private set; }
    internal (int Width, int Height)? Size { get; private set; }
    internal string? BoundsOut { get; private set; }
    internal long? FrameLimit { get; private set; }
    internal bool Help { get; private set; }

    internal static DemoOptions Parse(string[] args)
    {
        var options = new DemoOptions();
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (!supplied.Add(option))
                throw new ArgumentException($"Параметр {option} указан несколько раз.");

            switch (option)
            {
                case "--help": options.Help = true; break;
                case "--static": options.Static = true; break;
                case "--no-focus": options.NoFocus = true; break;
                case "--position":
                    options.Position = ParsePosition(Value());
                    break;
                case "--size":
                    options.Size = ParseSize(Value());
                    break;
                case "--bounds-out":
                    options.BoundsOut = FilePath();
                    break;
                case "--scenario":
                    options.Scenario = Value() switch
                    {
                        "mvp" => DemoScenario.Mvp,
                        "lighting" => DemoScenario.Lighting,
                        "roundtrip" => DemoScenario.RoundTrip,
                        var value => throw new ArgumentException($"Неизвестный сценарий '{value}': ожидается mvp, lighting или roundtrip."),
                    };
                    break;
                case "--texture": options.TexturePath = FilePath(); break;
                case "--scene": options.ScenePath = FilePath(); break;
                case "--save": options.SavePath = FilePath(); break;
                case "--load": options.LoadPath = FilePath(); break;
                case "--frames":
                    string frames = Value();
                    if (!long.TryParse(frames, NumberStyles.None, CultureInfo.InvariantCulture, out long count) || count <= 0)
                        throw new ArgumentException("--frames требует целое положительное число показанных кадров.");
                    options.FrameLimit = count;
                    break;
                default: throw new ArgumentException($"Неизвестный параметр '{option}'. Используйте --help.");
            }

            string Value()
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"После {option} требуется значение.");
                return args[i];
            }

            string FilePath()
            {
                string value = Value();
                if (Path.EndsInDirectorySeparator(value))
                    throw new ArgumentException($"{option} требует путь файла, а не каталога.");
                _ = Path.GetFullPath(value); // Проверяем путь до создания графического контекста.
                return value;
            }
        }

        if (options.Help)
            return options;
        if (options.LoadPath is not null && supplied.Any(option =>
                option is not "--load" and not "--frames" and not "--no-focus"
                    and not "--position" and not "--size" and not "--bounds-out"))
        {
            throw new ArgumentException(
                "--load совместим только с --frames, --no-focus, --position, --size и --bounds-out: загруженная сцена показывается без движения.");
        }
        if (supplied.Contains("--scene") && options.Scenario != DemoScenario.RoundTrip)
            throw new ArgumentException("--scene применяется только к --scenario roundtrip.");
        if ((options.Static || options.SavePath is not null) && options.Scenario != DemoScenario.Mvp)
            throw new ArgumentException("--static и --save применяются только к сценарию mvp.");
        if (options.SavePath is not null && !options.Static)
            throw new ArgumentException("Для --save укажите --static: исходный и восстановленный кадры должны быть неподвижны.");
        return options;
    }

    // Разбирает --position X,Y.
    private static (int X, int Y) ParsePosition(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            return (x, y);
        }

        throw new ArgumentException("--position требует X,Y (целые числа).");
    }

    // Разбирает --size WxH.
    private static (int Width, int Height) ParseSize(string value)
    {
        string[] parts = value.Split('x', 'X');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            return (width, height);
        }

        throw new ArgumentException("--size требует WxH с положительными целыми W и H.");
    }
}
