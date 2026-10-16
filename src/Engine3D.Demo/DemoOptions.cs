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
          --help                            Эта справка; окно не создаётся.

        --load совместим только с --frames. --static/--save применяются только к mvp.
        Закройте окно для штатного завершения. Изображения в JSON не встраиваются.
        """;

    internal DemoScenario Scenario { get; private set; } = DemoScenario.Mvp;
    internal string TexturePath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "Assets", "control.png");
    internal string ScenePath { get; private set; } = Path.Combine("artifacts", "demo", "scene.json");
    internal string? LoadPath { get; private set; }
    internal string? SavePath { get; private set; }
    internal bool Static { get; private set; }
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
        if (options.LoadPath is not null && supplied.Any(option => option is not "--load" and not "--frames"))
            throw new ArgumentException("--load совместим только с --frames: загруженная сцена показывается без движения.");
        if (supplied.Contains("--scene") && options.Scenario != DemoScenario.RoundTrip)
            throw new ArgumentException("--scene применяется только к --scenario roundtrip.");
        if ((options.Static || options.SavePath is not null) && options.Scenario != DemoScenario.Mvp)
            throw new ArgumentException("--static и --save применяются только к сценарию mvp.");
        if (options.SavePath is not null && !options.Static)
            throw new ArgumentException("Для --save укажите --static: исходный и восстановленный кадры должны быть неподвижны.");
        return options;
    }
}
