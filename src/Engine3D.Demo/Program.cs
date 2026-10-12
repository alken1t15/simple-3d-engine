using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Engine3D.Core;
using Engine3D.OpenGL;

// Ручная проверка рендера #9 и текстур #10 (загрузка PNG, ориентация UV, mipmap, странные размеры).
// Программа умеет:
//   (без аргументов)  ручной прогон всех кейсов: печатает описание, открывает окно отдельным
//                     процессом, спрашивает y/n одним символом и сама закрывает окно;
//   --only N [N...]   то же, но только для перечисленных кейсов;
//   --from N          тот же ручной прогон, что и без аргументов, но начиная с кейса N;
//   --nth N           открыть только N-й сценарий в окне (N = 1..scenarios.Length);
//   --all             прогнать все сценарии по очереди без вопросов (окно закрывает человек).
// Кейсы и их порядок совпадают с массивом scenarios. Отдельный сценарий запускается в дочернем
// процессе, чтобы родительский мог параллельно читать y/n из терминала.
// Это не демо-приложение #11: файл предназначен только для ручной проверки.

var scenarios = new (string Name, Action Run)[]
{
    ("освещение и нормали (simple light, P0-10)", Scenario2_Lighting),
    ("поправка глубины проекции и диапазон near/far", Scenario4_DepthRange),
    ("облёт камерой: depth + общий MeshData", Scenario6_Orbit),
    ("полёт сквозь тоннель (камера + transform)", Scenario7_Tunnel),
    ("движущийся свет при движении камеры", Scenario8_MovingLight),
    ("дождь кубов: камера пролетает сквозь объём", Scenario9_CubeField),
    ("панорама камеры по большой сцене", Scenario10_Pan),
    ("smoke: серая фототекстура 1024×1024", Scenario12_SmokeGray),
    ("smoke: цветная фототекстура 1024×1024", Scenario11_SmokeColor),
    ("smoke: серая фототекстура, куб неподвижен, облёт камеры", Scenario20_SmokeGrayOrbit),
    ("smoke: цветная фототекстура, куб неподвижен, облёт камеры", Scenario21_SmokeRgbOrbit),
    ("нестандартные разрешения (67×67, 1×1024, 1×1 и др.)", Scenario13_WeirdSizes),
    ("абстрактное поле кубов (текстуры Assets/random/)", Scenario14_AbstractCubes),
    ("облетающий свет над полем кубов (день/ночь)", Scenario15_MovingLightCubes),
    ("100 кубов с чередующимися текстурами, вращение", Scenario18_RotatingCubeField),
    ("шахматная доска 8×8 с фигурами", Scenario19_Chessboard),
    ("логотип Dota 2 из кубов (упрощённое лого)", Scenario22_Dota2Logo),
};

// Описания параллельны scenarios: descriptions[i] относится к scenarios[i].
string[] descriptions =
{
    """
    Оранжевый куб медленно вращается; направленный свет облетает сцену. Рядом высокий
    узкий светлый столб и серо-синий пол.

    Ожидаем:
      * у куба грани освещены ПО-РАЗНОМУ: обращённая к свету ярче, отвернувшаяся темнее;
      * при вращении куба и движении света яркость граней плавно меняется, без рывков;
      * высокий узкий столб под неравномерным масштабом освещён правдоподобно — его длинные
        боковые грани не «пересвечены» и не чернеют, как было бы при неверном преобразовании нормалей.

    Проверьте также ресайз: потяните край окна — анимация должна продолжаться, картинка
    не должна замирать до отпускания мыши (обработчик перерисовки окна).
    """,
    """
    Камера едет вглубь длинного коридора из разноцветных кубов (шахматный порядок
    слева/справа), затем возвращается в начало.

    Ожидаем:
      * по мере приближения кубы корректно уходят за камеру (ближняя плоскость отсекает их);
      * дальние кубы исчезают только за дальней плоскостью, а не раньше, и не «просвечивают»
        через ближние;
      * перекрытие по глубине корректно на всём пути.

    Это проверяет перевод диапазона глубины System.Numerics [0,1] в диапазон OpenGL [-1,1].
    """,
    """
    Камера медленно облетает по кругу центральный жёлтый куб и пояс из синих и фиолетовых
    кубов, которые колеблются по высоте; внизу пол.

    Ожидаем:
      * при облёте кубы пояса, проходя ПЕРЕД центральным, закрывают его, а проходя ЗА ним —
        скрываются за ним (порядок по глубине верен в любой фазе);
      * движение камеры плавное, без дёрганий;
      * все кубы и центр на одном MeshData и остаются на экране.
    """,
    """
    Камера летит вперёд сквозь изгибающийся тоннель: квадратные кольца из кубов
    (голубые сверху, оранжевые снизу, зелёные слева, розовые справа) уходят вглубь.
    Долетев до конца, камера возвращается в начало и повторяет.

    Ожидаем:
      * камера следует за изгибом тоннеля, оставаясь внутри кольца;
      * дальние кольца корректно перекрываются ближними (видно только несколько колец вперёд);
      * при развороте в начало нет артефактов и рывков.
    """,
    """
    Камера облетает лес вытянутых столбов разной высоты; направленный свет медленно вращается.

    Ожидаем:
      * столбы (неравномерный масштаб по высоте) освещены правдоподобно: яркая сторона
        обращена к источнику, тёмная — от него, без «пересвета» длинных граней;
      * при вращении света яркость на столбах и полу меняется согласованно;
      * при облёте камеры освещение сцены не «дёргается».
    """,
    """
    Камера летит сквозь облако из сотен мелких кубов, проходя вплотную к ближним,
    и по достижении конца возвращается.

    Ожидаем:
      * кубы, оказавшиеся перед камерой, отсекаются ближней плоскостью корректно;
      * на близких расстояниях кубы перекрываются по глубине без мерцания и «наложения»;
      * нет мигающих пикселей у самой камеры.
    """,
    """
    Камера панорамирует вдоль «города» из 625 кубов разной высоты, меняя точку взгляда;
    внизу тёмная плита.

    Ожидаем:
      * при движении камеры перекрытие по глубине остаётся корректным на всей ширине сцены;
      * далёкие башни не «просвечивают» через ближние и не мерцают;
      * движение плавное, без провалов кадров.
    """,
    """
    Серая фототекстура gray_1024x1024 на кубе; пол без текстуры.

    Ожидаем:
      * изображение чёрно-белое (grayscale), как исходный файл, без цветовых остатков;
      * на гранях не перевёрнуто и не зеркально, без артефактов;
      * освещение работает как с цветной текстурой — есть яркая и тёмная грани.
    """,
    """
    Фототекстура rgb_1024x1024 на кубе; пол без текстуры.

    Ожидаем:
      * изображение на гранях куба без артефактов — не перевёрнуто, не зеркально,
        цвета соответствуют исходному файлу;
      * грани освещены по свету (яркая/тёмная стороны куба);
      * пол без текстуры рендерится как раньше (белый fallback), текстура не «протекает» на него.
    """,
    """
    Серая фототекстура gray_1024x1024 на неподвижном кубе; пол без текстуры. Куб не вращается:
    вокруг него по кругу облетает камера, меняется только ракурс.

    Ожидаем:
      * по движению видно, что крутится камера, а не куб;
      * изображение чёрно-белое без цветовых остатков, не перевёрнуто и не зеркально;
      * пол без текстуры рендерится как раньше (белый fallback).
    """,
    """
    Цветная фототекстура rgb_1024x1024 на неподвижном кубе; пол без текстуры. Куб не вращается:
    вокруг него по кругу облетает камера, меняется только ракурс.

    Ожидаем:
      * по движению видно, что крутится камера, а не куб;
      * изображение на гранях без артефактов — не перевёрнуто, не зеркально, цвета соответствуют файлу;
      * грани освещены по свету, тёмные не становятся полностью чёрными.
    """,
    """
    Ряд кубов с текстурами нестандартных размеров: rgb_67x67, rgb_1x1024, rgb_512x1024,
    gray_1x1, gray_1024x1, gray_1024x512, gray_1000x1000.

    Ожидаем:
      * каждая текстура открылась без ошибок (в консоль печатаются имена загруженных файлов);
      * узкие вытянутые текстуры (1x1024, 1024x1) отображаются растянутыми по грани куба,
        а не обрезанными или мусором;
      * текстура 1x1 даёт один сплошной цвет;
      * нет чёрных/белых полос, мерцания и «просвечивания» кубов при облёте камеры.
    """,
    """
    Абстрактное поле кубов (3×3×3) с текстурами metal/mosaic/stone/tiles/velvet/wood из Assets/random/;
    кубы вращаются, камера медленно облетает сцену.

    Ожидаем:
      * каждая текстура загрузилась без ошибок (имена печатаются в консоль);
      * на гранях текстуры видны без артефактов, мерцания и чёрных/белых граней;
      * вращение кубов и облёт камеры плавные, без провалов кадров.
    """,
    """
    Поле текстурированных кубов; направленный свет облетает сцену по кругу, цвет и яркость света
    меняются — «день» сменяется «ночью».

    Ожидаем:
      * при движении света грани плавно светлеют и темнеют, без резких скачков;
      * текстуры читаются и на освещённых, и на затенённых гранях;
      * нет чёрных/белых граней и мерцания.
    """,
    """
    Сто кубов (10×10) с чередующимися текстурами wood/stone — как шахматное поле; каждый куб вращается
    вокруг своей оси (у разных кубов разные оси и скорости). Освещение дефолтное, камера медленно облетает поле.

    Ожидаем:
      * чередование текстур видно как шахматное поле, текстуры не перемешаны;
      * вращение каждого куба ровное и вокруг собственного центра, без «дрожания»;
      * все 100 кубов отрендерены, без мерцания и провалов кадров при облёте.
    """,
    """
    Шахматная доска 8×8: клетки — плоские кубы с чередующимися текстурами tiles/stone (светлая/тёмная);
    фигуры — кубы поменьше (wood и metal) на двух крайних рядах доски. Освещение дефолтное, камера облетает доску.

    Ожидаем:
      * клетки чередуются как на настоящей доске, светлые и тёмные не путаются;
      * фигуры двух текстур стоят на клетках крайних рядов, не «проваливаются» в доску;
      * текстуры не перепутаны между клетками и фигурами.
    """,
    """
    Эмблема Dota 2 (упрощённое лого: красная плита + белые угловые росчерки) из кубов на сетке 24×24;
    Форма растеризована из svg на Wikimedia Commons. Кубы стоят на месте, камера облетает лого,
    фон — текстура velvet с красным оттенком, текстуры на кубах перемешаны случайно.
    Одинаковая эмблема выложена обеими сторонами плиты. Количество кубов печатается в консоль.

    Ожидаем:
      * силуэт лого узнаваем (диагональный росчерк и два рога) с обеих сторон плиты, фон — velvet;
      * камера плавно облетает эмблему, кубы неподвижны;
      * рендер стабилен (количество кубов эмблемы в консоли), без мерцания при облёте.
    """,
};

string outDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "manual-demo");
WindowConfig.OutDir = outDir;

// Начальная позиция окна: без неё ОС при прогоне сценариев подряд каждый раз выбирает место сама.
// --position X,Y переопределяет и значение по умолчанию, и запомненное положение; аргумент вырезается
// из дальнейшего разбора. Без --position окно открывается там, где пользователь оставил предыдущее:
// RunWindow пишет текущее положение в artifacts/manual-demo/window-placement.txt, следующий запуск читает его.
var cliArgs = new List<string>(args);
int positionAt = cliArgs.IndexOf("--position");
if (positionAt >= 0)
{
    if (positionAt + 1 >= cliArgs.Count)
    {
        Console.WriteLine("--position: ожидается X,Y.");
        return 1;
    }

    if (!TryParsePosition(cliArgs[positionAt + 1], out (int X, int Y) parsed))
    {
        Console.WriteLine($"--position: ожидается X,Y, получено «{cliArgs[positionAt + 1]}».");
        return 1;
    }

    WindowConfig.ForcedPosition = parsed;
    cliArgs.RemoveRange(positionAt, 2);
}

// --from N: тот же ручной прогон, что и без аргументов, но начиная с кейса N. Аргумент вырезается,
// чтобы дальше разбор пошёл по ветке guided-режима.
int? from = null;
int fromAt = cliArgs.IndexOf("--from");
if (fromAt >= 0)
{
    if (fromAt + 1 >= cliArgs.Count
        || !int.TryParse(cliArgs[fromAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
        || start < 1
        || start > scenarios.Length)
    {
        Console.WriteLine($"--from: ожидается число от 1 до {scenarios.Length}.");
        return 1;
    }

    from = start;
    cliArgs.RemoveRange(fromAt, 2);
}

args = [.. cliArgs];

if (args.Length == 0)
    return await GuidedAsync(from is { } start ? FromIndex(start, scenarios.Length) : AllIndices(scenarios.Length), scenarios, descriptions, outDir);

if (args[0] is "--help" or "-h" or "help")
{
    PrintUsage(scenarios.Length);
    return 0;
}

if (args[0] == "--only")
{
    if (args.Length < 2)
    {
        PrintUsage(scenarios.Length);
        return 1;
    }

    var indices = new List<int>();
    foreach (string token in args[1..])
    {
        if (!int.TryParse(token, out int value) || value < 1 || value > scenarios.Length)
        {
            Console.WriteLine($"--only: «{token}» не число от 1 до {scenarios.Length}.");
            return 2;
        }

        indices.Add(value);
    }

    return await GuidedAsync([.. indices], scenarios, descriptions, outDir);
}

if (args[0] is "--all" or "all")
{
    for (int i = 0; i < scenarios.Length; i++)
    {
        Console.WriteLine($"\n[{i + 1}/{scenarios.Length}] {scenarios[i].Name}");
        scenarios[i].Run();
    }

    return 0;
}

string? nth = args.Length >= 2 && args[0] is "--nth" or "nth" ? args[1]
    : args.Length == 1 && args[0].StartsWith("--nth=", StringComparison.Ordinal) ? args[0]["--nth=".Length..]
    : null;

if (nth is not null && int.TryParse(nth, out int number) && number >= 1 && number <= scenarios.Length)
{
    Console.WriteLine($"[{number}/{scenarios.Length}] {scenarios[number - 1].Name}");
    scenarios[number - 1].Run();
    return 0;
}

PrintUsage(scenarios.Length);
return 1;

static int[] AllIndices(int count) => [.. Enumerable.Range(1, count)];

static int[] FromIndex(int start, int count) => [.. Enumerable.Range(start, count - start + 1)];

// Полный путь к ассету текстуры в папке Assets рядом с исполняемым файлом.
static string AssetPath(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

static bool TryParsePosition(string value, out (int X, int Y) position)
{
    string[] parts = value.Split(',');
    if (parts.Length == 2
        && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
        && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
    {
        position = (x, y);
        return true;
    }

    position = default;
    return false;
}

static void PrintUsage(int count)
{
    Console.WriteLine("Ручная проверка рендера #9 и текстур #10. Использование:");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release -- --only 1 5");
    Console.WriteLine($"  dotnet run --project src/Engine3D.Demo --configuration Release -- --nth N   (N = 1..{count})");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release -- --all");
    Console.WriteLine("  ... -- --from N   ручной прогон, начиная с кейса N (без флага — с первого)");
    Console.WriteLine("  ... -- --position X,Y   положение окна в пунктах от левого верхнего угла (по умолчанию 0,40)");
    Console.WriteLine("  Без --position окно открывается там, где вы оставили предыдущее (запоминается после каждого кейса).");
}

// Ручной прогон: на каждый кейс печатает описание, открывает окно дочерним процессом,
// ждёт ответа y/n и сам закрывает окно. Любой ответ кроме y — провал и выход с кодом 1.
static async Task<int> GuidedAsync(int[] indices, (string Name, Action Run)[] scenarios, string[] descriptions, string outDir)
{
    if (Console.IsInputRedirected)
    {
        Console.WriteLine("Нужен интерактивный терминал: отвечать y/n придётся вручную.");
        Console.WriteLine("Автоматическую проверку запускают графические тесты: ENGINE3D_GRAPHICS_TESTS=1 dotnet test Engine3D.sln");
        return 1;
    }

    Directory.CreateDirectory(outDir);

    Line('=');
    Console.WriteLine(" Ручная проверка OpenGL-рендера #9 и текстур #10");
    Line('=');
    Console.WriteLine($" {indices.Length} кейсов, вывод пишется в {outDir}/case-N.log.");
    Console.WriteLine(" Отвечайте y, если увиденное совпало с описанием, иначе любой другой символ — выход с ошибкой.");
    Console.WriteLine(" Окно не забирает фокус у терминала: y можно печатать сразу, кликать по окну не нужно.");
    Line('=');

    int passed = 0;
    for (int i = 0; i < indices.Length; i++)
    {
        int n = indices[i];
        string name = scenarios[n - 1].Name;
        string log = Path.Combine(outDir, $"case-{n}.log");

        Console.WriteLine();
        Line('=');
        Console.WriteLine($" Кейс {i + 1} из {indices.Length}: {name}");
        Console.WriteLine($" Ключи запуска: --nth {n}");
        Console.WriteLine($" Вывод приложения: {log}");
        Line('=');
        Console.WriteLine();
        Console.WriteLine(descriptions[n - 1]);
        Console.WriteLine();

        Process child = LaunchScenario(n);
        Task<string> standardOutput = child.StandardOutput.ReadToEndAsync();
        Task<string> standardError = child.StandardError.ReadToEndAsync();

        bool ok;
        try
        {
            Console.WriteLine("Окно открыто, вывод приложения пишется в файл кейса.");
            Console.WriteLine("Посмотрите на окно и нажмите y — окно закроет утилита, Enter жать не нужно.");
            Console.WriteLine();
            Console.Write("Совпало с описанием? [y/N] ");
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            Console.WriteLine(key.KeyChar);
            ok = key.KeyChar is 'y' or 'Y';
        }
        finally
        {
            StopChild(child);
        }

        await Task.WhenAny(Task.WhenAll(standardOutput, standardError), Task.Delay(3000));
        string text = standardOutput.IsCompletedSuccessfully ? standardOutput.Result : "";
        string errors = standardError.IsCompletedSuccessfully ? standardError.Result : "";
        File.WriteAllText(log, errors.Length == 0 ? text : text + "\n" + errors);

        if (!ok)
        {
            Console.WriteLine();
            Line('!');
            Console.WriteLine("ПРОВАЛ: поведение не совпало с описанием");
            Console.WriteLine($"Кейс: {name}");
            Console.WriteLine($"Ключи запуска: --nth {n}");
            Console.WriteLine($"Вывод приложения: {log}");
            Line('!');
            Console.WriteLine($"Пройдено {passed} из {indices.Length} кейсов. Ручная проверка не пройдена.");
            return 1;
        }

        passed++;
    }

    Console.WriteLine();
    Line('=');
    Console.WriteLine($" Пройдено {passed} из {indices.Length} кейсов.");
    Line('=');
    return 0;
}

// Запускает этот же исполняемый файл с ключом --nth N в отдельном процессе, чтобы окно
// не блокировало чтение y/n в родителе. Работает и через apphost, и через `dotnet <dll>`.
static Process LaunchScenario(int number)
{
    string host = Environment.ProcessPath ?? "dotnet";
    string? dll = Assembly.GetEntryAssembly()?.Location;

    var startInfo = new ProcessStartInfo
    {
        FileName = host,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    bool hostIsDotnet = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    if (hostIsDotnet && !string.IsNullOrEmpty(dll))
        startInfo.ArgumentList.Add(dll);

    startInfo.ArgumentList.Add("--nth");
    startInfo.ArgumentList.Add(number.ToString(CultureInfo.InvariantCulture));

    // Дочернее окно само читает запомненное положение; --position передается только при явном форсировании.
    if (WindowConfig.ForcedPosition is { } forced)
    {
        startInfo.ArgumentList.Add("--position");
        startInfo.ArgumentList.Add($"{forced.X.ToString(CultureInfo.InvariantCulture)},{forced.Y.ToString(CultureInfo.InvariantCulture)}");
    }

    Process child = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить сценарий.");
    child.StandardInput.Close();
    return child;
}

// Закрывает окно дочернего процесса: сначала SIGTERM (на Unix), затем при необходимости KILL.
static void StopChild(Process child)
{
    if (child.HasExited)
        return;

    if (!OperatingSystem.IsWindows())
    {
        try
        {
            var killInfo = new ProcessStartInfo("kill") { UseShellExecute = false };
            killInfo.ArgumentList.Add("-TERM");
            killInfo.ArgumentList.Add(child.Id.ToString(CultureInfo.InvariantCulture));
            Process.Start(killInfo)?.WaitForExit(2000);
        }
        catch (Exception)
        {
        }
    }

    if (!child.WaitForExit(3000))
    {
        try
        {
            child.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
        }
    }

    child.WaitForExit();
}

static void Line(char symbol) => Console.WriteLine(new string(symbol, 78));

static Engine CreateEngine(string title)
{
    var (x, y, width, height) = WindowConfig.Resolve();
    return Engine.Create(new EngineOptions
    {
        Width = width,
        Height = height,
        Title = title,
        VSync = true,
        FocusOnShow = false,
        Position = (x, y),
    });
}

static SceneObject Add(Scene scene, MeshData mesh, ColorRgba color, Vector3 position, Vector3? scale = null)
{
    var item = new SceneObject(mesh, new MaterialData { BaseColor = color });
    item.Transform.Position = position;
    if (scale is { } s)
        item.Transform.Scale = s;
    scene.Add(item);
    return item;
}

static Scene NewLitScene()
{
    var scene = new Scene();
    scene.Lighting.AmbientColor = new Vector3(0.25f);
    scene.Lighting.DirectionalColor = Vector3.One;
    scene.Lighting.Direction = new Vector3(-0.5f, -1f, -0.4f);
    return scene;
}

// Показывает сцену в отдельном окне до тех пор, пока окно не закроют, и печатает счетчик кадров.
// onCreated вызывается после создания Engine и до Run — единственное место, где валиден LoadTexture.
// Пока окно открыто, его текущее положение и размер пишутся в файл, чтобы следующее окно (следующий кейс
// или повторный запуск) открылось там же, где пользователь оставил предыдущее — включая ручной перетаскиванием.
static void RunWindow(string title, Scene scene, Camera camera, Action<float>? onUpdate = null, Action<Engine>? onCreated = null)
{
    using var engine = CreateEngine(title);
    onCreated?.Invoke(engine);
    Console.WriteLine("  Окно открыто. Закройте его, чтобы завершить сценарий.");

    (int X, int Y, int Width, int Height)? persisted = null;
    void PersistPlacement()
    {
        var bounds = engine.WindowBounds;
        if (persisted != bounds)
        {
            WindowConfig.WriteSaved(bounds);
            persisted = bounds;
        }
    }

    engine.Run(scene, camera, deltaSeconds =>
    {
        onUpdate?.Invoke(deltaSeconds);
        PersistPlacement();
    });
    PersistPlacement();
    Console.WriteLine($"  Окно закрыто. Показано кадров: {engine.RenderedFrames}.");
}

// Новое: SceneRenderer считает свет RGB = base * (ambient + directional * max(dot(n, -dir), 0)),
// нормали берутся через inverse-transpose модельной матрицы. Проверяем разные яркости граней и свет на вытянутом объекте.
static void Scenario2_Lighting()
{
    Console.WriteLine("Смотрите: у вращающегося куба грани освещены по-разному, источник света медленно облетает сцену.");
    Console.WriteLine("Высокий узкий столб освещен корректно при неравномерном масштабе (нормали через inverse-transpose).");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.12f);
    var cube = PrimitiveFactory.CreateCube();
    var spinner = Add(scene, cube, new ColorRgba(1f, 0.75f, 0.30f), new Vector3(-1.8f, 0f, 0f), new Vector3(1.4f));
    Add(scene, cube, new ColorRgba(0.85f, 0.85f, 0.90f), new Vector3(1.8f, 0.3f, 0f), new Vector3(0.5f, 2.6f, 0.5f));
    Add(scene, PrimitiveFactory.CreatePlane(20f, 20f), new ColorRgba(0.32f, 0.35f, 0.42f), new Vector3(0f, -1.5f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2.2f, 8f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #9 - 2 lighting", scene, camera, deltaSeconds =>
    {
        t += deltaSeconds * 0.7f;
        spinner.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, t);
        scene.Lighting.Direction = new Vector3(-MathF.Cos(t), -1.1f, -MathF.Sin(t));
    });
}

// Новое: GlProjection переводит диапазон глубины System.Numerics [0,1] в OpenGL [-1,1] один раз,
// FramePreparation проверяет сцену/камеру и считает матрицы до GL. Проверяем длинный коридор и движение камеры.
static void Scenario4_DepthRange()
{
    Console.WriteLine("Смотрите: камера едет вглубь коридора из кубов.");
    Console.WriteLine("Перекрытие по глубине корректно по всему диапазону; дальние кубы уходят за дальнюю плоскость и исчезают.");

    var scene = NewLitScene();
    var cube = PrimitiveFactory.CreateCube();
    for (int i = 0; i < 24; i++)
    {
        var color = i % 2 == 0 ? new ColorRgba(0.75f, 0.40f, 0.30f) : new ColorRgba(0.35f, 0.45f, 0.70f);
        Add(scene, cube, color, new Vector3(i % 2 == 0 ? -1.4f : 1.4f, 0f, -i * 3f), new Vector3(1.2f));
    }

    var camera = new Camera
    {
        Position = new Vector3(0f, 1.5f, 6f),
        Target = new Vector3(0f, 0f, -40f),
        NearPlane = 0.1f,
        FarPlane = 160f,
        FieldOfViewDegrees = 60f,
    };

    float z = 6f;
    RunWindow("Engine3D #9 - 4 depth range", scene, camera, deltaSeconds =>
    {
        z -= deltaSeconds * 4f;
        if (z < -40f)
            z = 6f;
        camera.Position = new Vector3(0f, 1.5f, z);
        camera.Target = new Vector3(0f, 0f, z - 40f);
    });
}

// Движение камеры: облет по кругу. Объекты — один центр и пояс из кубов на общем MeshData.
// Проверяем, что view-матрица пересчитывается каждый кадр, а depth test верен, пока объекты проходят
// перед центром и за ним.
static void Scenario6_Orbit()
{
    Console.WriteLine("Смотрите: камера облетает по кругу центральный куб и пояс из кубов.");
    Console.WriteLine("Кубы пояса, проходя перед центром и за ним, должны перекрываться по глубине корректно.");
    Console.WriteLine("Все кубы и центр — на одном MeshData.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.2f);
    var shared = PrimitiveFactory.CreateCube();
    Add(scene, shared, new ColorRgba(1f, 0.8f, 0.3f), Vector3.Zero, new Vector3(1.6f));

    for (int i = 0; i < 24; i++)
    {
        float angle = i / 24f * MathF.Tau;
        var color = i % 2 == 0 ? new ColorRgba(0.3f, 0.7f, 0.95f) : new ColorRgba(0.8f, 0.4f, 0.9f);
        Add(scene, shared, color,
            new Vector3(MathF.Cos(angle) * 3.5f, MathF.Sin(angle * 3f) * 0.9f, MathF.Sin(angle) * 3.5f),
            new Vector3(0.5f));
    }

    Add(scene, PrimitiveFactory.CreatePlane(24f, 24f), new ColorRgba(0.3f, 0.35f, 0.4f), new Vector3(0f, -2.2f, 0f));

    var camera = new Camera { FarPlane = 200f };
    float time = 0f;
    RunWindow("Engine3D #9 - 6 orbit", scene, camera, deltaSeconds =>
    {
        time += deltaSeconds * 0.6f;
        camera.Position = new Vector3(MathF.Cos(time) * 9f, 3.5f + MathF.Sin(time * 0.7f), MathF.Sin(time) * 9f);
        camera.Target = Vector3.Zero;
    });
}

// Движение камеры: полет сквозь изгибающийся тоннель. Тоннель собран из колец кубов (общий MeshData),
// положение колец задает transform объекта. Камера летит вперед и повторяет изгиб.
static void Scenario7_Tunnel()
{
    Console.WriteLine("Смотрите: камера летит сквозь изгибающийся тоннель из колец кубов.");
    Console.WriteLine("Кольца ближние и дальние перекрываются по глубине; камера следует за изгибом.");

    const int rings = 40;
    const int spacing = 4;
    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.3f);
    var shared = PrimitiveFactory.CreateCube();
    const float half = 2.4f;
    const int perSide = 4;

    for (int r = 0; r < rings; r++)
    {
        float z = -r * spacing;
        float bend = MathF.Sin(r * 0.35f) * 2.5f;
        for (int s = 0; s < perSide; s++)
        {
            float f = s / (float)(perSide - 1) * 2f - 1f;
            Add(scene, shared, new ColorRgba(0.40f, 0.80f, 0.90f), new Vector3(bend + f * half, half, z), new Vector3(0.4f));
            Add(scene, shared, new ColorRgba(0.90f, 0.60f, 0.30f), new Vector3(bend + f * half, -half, z), new Vector3(0.4f));
            Add(scene, shared, new ColorRgba(0.60f, 0.90f, 0.40f), new Vector3(bend - half, f * half, z), new Vector3(0.4f));
            Add(scene, shared, new ColorRgba(0.90f, 0.40f, 0.60f), new Vector3(bend + half, f * half, z), new Vector3(0.4f));
        }
    }

    var camera = new Camera { FarPlane = 400f };
    float zCam = 10f;
    RunWindow("Engine3D #9 - 7 tunnel", scene, camera, deltaSeconds =>
    {
        zCam -= deltaSeconds * 12f;
        float traveled = 10f - zCam;
        if (zCam < -(rings - 1) * spacing - 12f)
        {
            zCam = 10f;
            traveled = 0f;
        }

        float ringIndex = traveled / spacing;
        float bendHere = MathF.Sin(ringIndex * 0.35f) * 2.5f;
        float bendAhead = MathF.Sin((ringIndex + 3f) * 0.35f) * 2.5f;
        camera.Position = new Vector3(bendHere, 0f, zCam);
        camera.Target = new Vector3(bendAhead, 0f, zCam - 10f);
    });
}

// Движение камеры и света одновременно. Лес вытянутых столбов (неравномерный масштаб) проверяет
// inverse-transpose нормалей: при облете и смене направления света столбы освещены правдоподобно.
static void Scenario8_MovingLight()
{
    Console.WriteLine("Смотрите: камера облетает лес вытянутых столбов, а направленный свет медленно вращается.");
    Console.WriteLine("Столбы под неравномерным масштабом освещены правдоподобно (нормали через inverse-transpose).");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.1f);
    var shared = PrimitiveFactory.CreateCube();

    for (int x = -4; x <= 4; x++)
    {
        for (int z = -4; z <= 4; z++)
        {
            float height = 1.5f + ((x * 7 + z * 13) % 5 + 5) % 5 * 0.6f;
            var color = new ColorRgba(0.5f, 0.45f + 0.1f * (height / 4f), 0.8f);
            Add(scene, shared, color, new Vector3(x * 2f, height * 0.5f - 0.5f, z * 2f), new Vector3(0.55f, height, 0.55f));
        }
    }

    Add(scene, PrimitiveFactory.CreatePlane(30f, 30f), new ColorRgba(0.30f, 0.34f, 0.30f), new Vector3(0f, -0.5f, 0f));

    var camera = new Camera { FarPlane = 200f };
    float time = 0f;
    RunWindow("Engine3D #9 - 8 moving light", scene, camera, deltaSeconds =>
    {
        time += deltaSeconds * 0.5f;
        camera.Position = new Vector3(MathF.Cos(time) * 16f, 4f + MathF.Sin(time * 0.8f), MathF.Sin(time) * 16f);
        camera.Target = Vector3.Zero;
        scene.Lighting.Direction = new Vector3(-MathF.Cos(time * 1.3f), -1.2f, -MathF.Sin(time * 1.3f));
    });
}

// Движение камеры: пролет сквозь облако из сотен кубов на общем MeshData. Маленький near plane,
// камера проходит вплотную — проверяем глубину на близких расстояниях.
static void Scenario9_CubeField()
{
    Console.WriteLine("Смотрите: камера летит сквозь облако сотен кубов, проходя вплотную к ближним.");
    Console.WriteLine("Кубы перекрываются по глубине без ошибок даже у самой камеры (малый near plane).");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.3f);
    var shared = PrimitiveFactory.CreateCube();

    for (int i = 0; i < 500; i++)
    {
        float angle = i * 2.399963f; // золотой угол: равномерное распределение по кругу
        float radius = 0.4f + i % 20 * 0.35f;
        float y = ((i * 37) % 15 - 7) * 0.5f;
        var color = new ColorRgba(0.3f + (i % 7) * 0.1f, 0.7f, 0.4f + (i % 5) * 0.1f);
        Add(scene, shared, color,
            new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius - i / 20f * 2f),
            new Vector3(0.28f));
    }

    var camera = new Camera { NearPlane = 0.05f, FarPlane = 300f };
    float z = 6f;
    float time = 0f;
    RunWindow("Engine3D #9 - 9 cube field", scene, camera, deltaSeconds =>
    {
        time += deltaSeconds;
        z -= deltaSeconds * 3f;
        if (z < -45f)
            z = 6f;
        camera.Position = new Vector3(MathF.Sin(time * 0.5f) * 1.5f, 0f, z);
        camera.Target = new Vector3(0f, 0f, z - 5f);
    });
}

// Движение камеры: панорама по широкой сцене из 625 кубов разной высоты на общем MeshData.
// Камера едет сбоку и меняет target — проверяем пересчет view и корректную глубину на большой сцене.
static void Scenario10_Pan()
{
    Console.WriteLine("Смотрите: камера панорамирует вдоль города из 625 кубов разной высоты (общий MeshData).");
    Console.WriteLine("При движении перекрытие по глубине остается корректным на всей ширине сцены.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.3f);
    var shared = PrimitiveFactory.CreateCube();

    for (int x = -12; x <= 12; x++)
    {
        for (int z = -12; z <= 12; z++)
        {
            float level = ((x * 3 + z * 5) % 7 + 7) % 7;
            float height = 0.6f + level * 0.7f;
            var color = new ColorRgba(0.30f + 0.06f * level, 0.55f, 0.90f - 0.08f * level);
            Add(scene, shared, color, new Vector3(x * 1.5f, height * 0.5f - 0.5f, z * 1.5f), new Vector3(1f, height, 1f));
        }
    }

    Add(scene, PrimitiveFactory.CreatePlane(60f, 60f), new ColorRgba(0.22f, 0.26f, 0.30f), new Vector3(0f, -0.6f, 0f));

    var camera = new Camera { FarPlane = 400f };
    float time = 0f;
    RunWindow("Engine3D #9 - 10 pan", scene, camera, deltaSeconds =>
    {
        time += deltaSeconds * 0.4f;
        camera.Position = new Vector3(MathF.Sin(time) * 20f, 10f + MathF.Sin(time * 1.7f) * 2f, 22f + MathF.Cos(time * 0.6f) * 5f);
        camera.Target = new Vector3(MathF.Sin(time) * 4f, 0f, 0f);
    });
}

// Smoke-проверка цветной фототекстуры: куб с rgb_1024x1024, пол без текстуры (fallback не портится).
static void Scenario11_SmokeColor()
{
    Console.WriteLine("Смотрите: куб с цветной фототекстурой rgb_1024x1024, пол без текстуры.");
    Console.WriteLine("Картинка без артефактов, не перевёрнута и не зеркальна; пол — как раньше.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.35f);
    var cubeMaterial = new MaterialData();
    var cube = new SceneObject(PrimitiveFactory.CreateCube(), cubeMaterial);
    cube.Transform.Position = new Vector3(0f, 0.3f, 0f);
    scene.Add(cube);
    Add(scene, PrimitiveFactory.CreatePlane(12f, 12f), new ColorRgba(0.3f, 0.35f, 0.42f), new Vector3(0f, -1.2f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2f, 6f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 9 smoke color", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.6f;
        cube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, t);
    }, onCreated: engine => cubeMaterial.Texture = engine.LoadTexture(AssetPath("sizes/rgb_1024x1024.png")));
}

// Smoke-проверка серой фототекстуры: куб с gray_1024x1024 (grayscale), пол без текстуры.
static void Scenario12_SmokeGray()
{
    Console.WriteLine("Смотрите: куб с серой фототекстурой gray_1024x1024, пол без текстуры.");
    Console.WriteLine("Изображение чёрно-белое без цветовых остатков, без артефактов и зеркал.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.35f);
    var cubeMaterial = new MaterialData();
    var cube = new SceneObject(PrimitiveFactory.CreateCube(), cubeMaterial);
    cube.Transform.Position = new Vector3(0f, 0.3f, 0f);
    scene.Add(cube);
    Add(scene, PrimitiveFactory.CreatePlane(12f, 12f), new ColorRgba(0.3f, 0.35f, 0.42f), new Vector3(0f, -1.2f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2f, 6f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 8 smoke gray", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.6f;
        cube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, t);
    }, onCreated: engine => cubeMaterial.Texture = engine.LoadTexture(AssetPath("sizes/gray_1024x1024.png")));
}

// Smoke-проверка серой фототекстуры, но куб неподвижен: вокруг него облетает камера.
static void Scenario20_SmokeGrayOrbit()
{
    Console.WriteLine("Смотрите: неподвижный куб с серой фототекстурой gray_1024x1024; вокруг куба облетает камера.");
    Console.WriteLine("Куб не вращается, меняется только ракурс. Ожидаем: чёрно-белая картинка без цветовых остатков и артефактов.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.35f);
    var cubeMaterial = new MaterialData();
    var cube = new SceneObject(PrimitiveFactory.CreateCube(), cubeMaterial);
    cube.Transform.Position = new Vector3(0f, 0.3f, 0f);
    scene.Add(cube);
    Add(scene, PrimitiveFactory.CreatePlane(12f, 12f), new ColorRgba(0.3f, 0.35f, 0.42f), new Vector3(0f, -1.2f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2f, 6f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 10 smoke gray orbit", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.5f;
        camera.Position = new Vector3(MathF.Sin(t) * 6f, 1.6f + MathF.Sin(t * 0.6f) * 0.6f, MathF.Cos(t) * 6f);
        camera.Target = new Vector3(0f, 0.3f, 0f);
    }, onCreated: engine => cubeMaterial.Texture = engine.LoadTexture(AssetPath("sizes/gray_1024x1024.png")));
}

// Smoke-проверка цветной фототекстуры, но куб неподвижен: вокруг него облетает камера.
static void Scenario21_SmokeRgbOrbit()
{
    Console.WriteLine("Смотрите: неподвижный куб с цветной фототекстурой rgb_1024x1024; вокруг куба облетает камера.");
    Console.WriteLine("Куб не вращается, меняется только ракурс. Ожидаем: картинка не перевёрнута и не зеркальна, без артефактов.");

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.35f);
    var cubeMaterial = new MaterialData();
    var cube = new SceneObject(PrimitiveFactory.CreateCube(), cubeMaterial);
    cube.Transform.Position = new Vector3(0f, 0.3f, 0f);
    scene.Add(cube);
    Add(scene, PrimitiveFactory.CreatePlane(12f, 12f), new ColorRgba(0.3f, 0.35f, 0.42f), new Vector3(0f, -1.2f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2f, 6f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 11 smoke rgb orbit", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.5f;
        camera.Position = new Vector3(MathF.Sin(t) * 6f, 1.6f + MathF.Sin(t * 0.6f) * 0.6f, MathF.Cos(t) * 6f);
        camera.Target = new Vector3(0f, 0.3f, 0f);
    }, onCreated: engine => cubeMaterial.Texture = engine.LoadTexture(AssetPath("sizes/rgb_1024x1024.png")));
}

// Ряд кубов с текстурами нестандартных размеров; имена загруженных файлов печатаются в консоль.
static void Scenario13_WeirdSizes()
{
    Console.WriteLine("Смотрите: ряд кубов с текстурами нестандартных размеров.");
    Console.WriteLine("Слева направо: rgb_67x67, rgb_1x1024, rgb_512x1024, gray_1x1, gray_1024x1, gray_1024x512, gray_1000x1000.");

    string[] files =
    [
        "sizes/rgb_67x67.png",
        "sizes/rgb_1x1024.png",
        "sizes/rgb_512x1024.png",
        "sizes/gray_1x1.png",
        "sizes/gray_1024x1.png",
        "sizes/gray_1024x512.png",
        "sizes/gray_1000x1000.png",
    ];

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.4f);
    var materials = new MaterialData[files.Length];
    for (int i = 0; i < files.Length; i++)
    {
        materials[i] = new MaterialData();
        var item = new SceneObject(PrimitiveFactory.CreateCube(), materials[i]);
        item.Transform.Position = new Vector3((i - (files.Length - 1) / 2f) * 2.2f, 0f, 0f);
        item.Transform.Scale = new Vector3(1.5f);
        scene.Add(item);
    }

    Add(scene, PrimitiveFactory.CreatePlane(30f, 30f), new ColorRgba(0.3f, 0.35f, 0.4f), new Vector3(0f, -1.6f, 0f));

    var camera = new Camera { Position = new Vector3(0f, 2f, 8f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 13 weird sizes", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.5f;
        camera.Position = new Vector3(MathF.Sin(t) * 12f, 2.2f + MathF.Sin(t * 1.3f), 8f);
    }, onCreated: engine =>
    {
        for (int i = 0; i < files.Length; i++)
        {
            materials[i].Texture = engine.LoadTexture(AssetPath(files[i]));
            Console.WriteLine($"  загружено: {files[i]}");
        }
    });
}

// Абстрактная композиция: поле вращающихся кубов с текстурами из Assets/random/, камера облетает сцену.
static void Scenario14_AbstractCubes()
{
    Console.WriteLine("Смотрите: абстрактное поле кубов с текстурами из Assets/random/, камера облетает сцену.");
    Console.WriteLine("Ожидаем: текстуры видны на гранях без артефактов и чёрных граней; облёт и вращение плавные.");

    string[] files =
    [
        "random/metal.png",
        "random/mosaic.png",
        "random/stone.png",
        "random/tiles.png",
        "random/velvet.png",
        "random/wood.png",
    ];

    var rng = new Random(20260512);
    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.4f);
    var materials = new MaterialData[files.Length];
    for (int i = 0; i < files.Length; i++)
        materials[i] = new MaterialData();

    const int grid = 3;
    var cubes = new List<SceneObject>();
    for (int x = 0; x < grid; x++)
    for (int y = 0; y < grid; y++)
    for (int z = 0; z < grid; z++)
    {
        int idx = (x + y * grid + z * grid * grid) % files.Length;
        var cube = new SceneObject(PrimitiveFactory.CreateCube(), materials[idx]);
        float jitter = (float)(rng.NextDouble() * 1.4 - 0.7);
        cube.Transform.Position = new Vector3((x - 1) * 3.4f, (y - 1) * 3.4f, (z - 1) * 3.4f + jitter);
        cube.Transform.Scale = new Vector3(1.5f + (float)rng.NextDouble() * 1.0f);
        scene.Add(cube);
        cubes.Add(cube);
    }

    var camera = new Camera { Position = new Vector3(0f, 0f, 15f), Target = Vector3.Zero };
    float t = 0f;
    RunWindow("Engine3D #10 - 14 abstract cubes", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds;
        foreach (SceneObject cube in cubes)
        {
            cube.Transform.Rotation =
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, t * 0.5f) *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, t * 0.33f);
        }

        camera.Position = new Vector3(MathF.Sin(t * 0.35f) * 13f, MathF.Sin(t * 0.6f) * 5f, MathF.Cos(t * 0.35f) * 13f);
        camera.Target = new Vector3(MathF.Sin(t * 0.2f) * 3f, 0f, 0f);
    }, onCreated: engine =>
    {
        for (int i = 0; i < files.Length; i++)
        {
            materials[i].Texture = engine.LoadTexture(AssetPath(files[i]));
            Console.WriteLine($"  загружено: {files[i]}");
        }
    });
}

// Поле текстурированных кубов; направленный свет облетает сцену, день сменяется ночью.
static void Scenario15_MovingLightCubes()
{
    Console.WriteLine("Смотрите: поле кубов с текстурами; направленный свет облетает сцену, «день» сменяется «ночью».");
    Console.WriteLine("Ожидаем: грани плавно светлеют и темнеют по мере движения света, текстуры читаются, без резких скачков.");

    string[] files =
    [
        "random/wood.png",
        "random/stone.png",
        "random/mosaic.png",
        "random/velvet.png",
        "random/tiles.png",
        "random/metal.png",
    ];

    var scene = NewLitScene();
    var materials = new MaterialData[files.Length];
    for (int i = 0; i < files.Length; i++)
        materials[i] = new MaterialData();

    const int grid = 3;
    for (int x = 0; x < grid; x++)
    for (int z = 0; z < grid; z++)
    {
        int idx = (x + z * grid) % files.Length;
        var cube = new SceneObject(PrimitiveFactory.CreateCube(), materials[idx]);
        cube.Transform.Position = new Vector3((x - 1) * 3.6f, 1f, (z - 1) * 3.6f);
        cube.Transform.Scale = new Vector3(2f);
        scene.Add(cube);
    }

    var camera = new Camera { Position = new Vector3(0f, 9f, 16f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 15 moving light", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.6f;
        scene.Lighting.Direction = new Vector3(MathF.Cos(t), -0.6f, MathF.Sin(t));

        float day = MathF.Sin(t * 0.5f) * 0.5f + 0.5f;
        scene.Lighting.DirectionalColor = Vector3.Lerp(new Vector3(0.15f, 0.2f, 0.4f), new Vector3(1f, 0.95f, 0.8f), day);
        scene.Lighting.AmbientColor = new Vector3(0.08f + 0.12f * day);
    }, onCreated: engine =>
    {
        for (int i = 0; i < files.Length; i++)
        {
            materials[i].Texture = engine.LoadTexture(AssetPath(files[i]));
            Console.WriteLine($"  загружено: {files[i]}");
        }
    });
}

// Сто кубов (10×10) с чередующимися текстурами, каждый вращается вокруг своей оси; освещение дефолтное.
static void Scenario18_RotatingCubeField()
{
    Console.WriteLine("Смотрите: 100 кубов (10×10) с чередующимися текстурами wood/stone, каждый вращается вокруг своей оси.");
    Console.WriteLine("Освещение дефолтное. Ожидаем: чередование текстур как шахматное поле; вращение ровное, без мерцания.");

    var scene = NewLitScene();
    var materials = new[]
    {
        new MaterialData(),
        new MaterialData(),
    };

    var cubes = new List<SceneObject>();
    const int grid = 10;
    for (int x = 0; x < grid; x++)
    for (int z = 0; z < grid; z++)
    {
        int idx = (x + z) & 1;
        var cube = new SceneObject(PrimitiveFactory.CreateCube(), materials[idx]);
        cube.Transform.Position = new Vector3((x - (grid - 1) / 2f) * 2.3f, 0.6f, (z - (grid - 1) / 2f) * 2.3f);
        cube.Transform.Scale = new Vector3(1.5f);
        scene.Add(cube);
        cubes.Add(cube);
    }

    var axes = new[]
    {
        Vector3.UnitY,
        Vector3.UnitX,
        Vector3.UnitZ,
        Vector3.Normalize(new Vector3(1f, 1f, 0f)),
        Vector3.Normalize(new Vector3(1f, 0f, 1f)),
        Vector3.Normalize(new Vector3(0f, 1f, 1f)),
    };

    var camera = new Camera { Position = new Vector3(0f, 14f, 18f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 14 rotating cube field", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds;
        for (int i = 0; i < cubes.Count; i++)
        {
            float speed = 0.6f + (i % 5) * 0.15f;
            cubes[i].Transform.Rotation = Quaternion.CreateFromAxisAngle(axes[i % axes.Length], t * speed);
        }

        camera.Position = new Vector3(MathF.Sin(t * 0.25f) * 20f, 12f + MathF.Sin(t * 0.13f) * 2f, MathF.Cos(t * 0.25f) * 20f);
        camera.Target = new Vector3(0f, 0f, 0f);
    }, onCreated: engine =>
    {
        materials[0].Texture = engine.LoadTexture(AssetPath("random/wood.png"));
        materials[1].Texture = engine.LoadTexture(AssetPath("random/stone.png"));
        Console.WriteLine("  загружено: random/wood.png, random/stone.png");
    });
}

// Шахматная доска 8×8: клетки — плоские кубы с текстурами tiles/stone, фигуры — кубы поменьше с wood/metal.
static void Scenario19_Chessboard()
{
    Console.WriteLine("Смотрите: шахматная доска 8×8; клетки — кубы с текстурами tiles/stone, фигуры — кубы с wood/metal.");
    Console.WriteLine("Ожидаем: клетки чередуются как на доске, фигуры двух цветов стоят на крайних рядах, текстуры не перепутаны.");

    var scene = NewLitScene();
    var cellLight = new MaterialData();
    var cellDark = new MaterialData();
    var pieceWhite = new MaterialData();
    var pieceBlack = new MaterialData();

    const int size = 8;
    for (int x = 0; x < size; x++)
    for (int z = 0; z < size; z++)
    {
        var cell = new SceneObject(PrimitiveFactory.CreateCube(), (x + z) % 2 == 0 ? cellLight : cellDark);
        cell.Transform.Position = new Vector3((x - (size - 1) / 2f) * 1.1f, 0.05f, (z - (size - 1) / 2f) * 1.1f);
        cell.Transform.Scale = new Vector3(1.06f, 0.1f, 1.06f);
        scene.Add(cell);
    }

    for (int x = 0; x < size; x++)
    {
        var sideA = new SceneObject(PrimitiveFactory.CreateCube(), pieceWhite);
        sideA.Transform.Position = new Vector3((x - (size - 1) / 2f) * 1.1f, 0.32f, -(size - 1) / 2f * 1.1f);
        sideA.Transform.Scale = new Vector3(0.55f);
        scene.Add(sideA);

        var sideB = new SceneObject(PrimitiveFactory.CreateCube(), pieceBlack);
        sideB.Transform.Position = new Vector3((x - (size - 1) / 2f) * 1.1f, 0.32f, (size - 1) / 2f * 1.1f);
        sideB.Transform.Scale = new Vector3(0.55f);
        scene.Add(sideB);
    }

    var camera = new Camera { Position = new Vector3(0f, 9f, 11f), Target = new Vector3(0f, 0.2f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 15 chessboard", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.4f;
        camera.Position = new Vector3(MathF.Sin(t) * 11f, 8f + MathF.Sin(t * 0.6f) * 1.5f, MathF.Cos(t) * 11f);
        camera.Target = new Vector3(0f, 0.2f, 0f);
    }, onCreated: engine =>
    {
        cellLight.Texture = engine.LoadTexture(AssetPath("random/tiles.png"));
        cellDark.Texture = engine.LoadTexture(AssetPath("random/stone.png"));
        pieceWhite.Texture = engine.LoadTexture(AssetPath("random/wood.png"));
        pieceBlack.Texture = engine.LoadTexture(AssetPath("random/metal.png"));
        Console.WriteLine("  загружено: tiles, stone (клетки), wood, metal (фигуры)");
    });
}

// Эмблема Dota 2 из кубов: растеризация упрощённого лого (плита + белые угловые росчерки) по сетке.
static void Scenario22_Dota2Logo()
{
    Console.WriteLine("Смотрите: эмблема Dota 2 из кубов. Кубы стоят на месте, камера облетает лого.");
    Console.WriteLine("Форма лого берётся из упрощённого SVG (Wikimedia Commons, CC BY-SA 4.0), кубы — со случайными текстурами.");

    bool Inside((float X, float Y) point, (float X, float Y)[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            bool crosses = poly[i].Y > point.Y != poly[j].Y > point.Y
                && point.X < (poly[j].X - poly[i].X) * (point.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    (float X, float Y)[] mainSlash = { (95, 145.5f), (149, 115), (510, 421), (474, 507), (361, 481) };
    (float X, float Y)[] hornTop = { (430, 95), (514, 172), (390, 130) };
    (float X, float Y)[] hornLow = { (100, 430), (228, 483), (95, 515) };

    var scene = NewLitScene();
    scene.Lighting.AmbientColor = new Vector3(0.35f);

    var plateMaterial = new MaterialData { BaseColor = new ColorRgba(0.9f, 0.4f, 0.38f) };
    var plate = new SceneObject(PrimitiveFactory.CreateCube(), plateMaterial);
    plate.Transform.Position = new Vector3(0f, 0f, -0.3f);
    plate.Transform.Scale = new Vector3(4.3f, 4.3f, 0.5f);
    scene.Add(plate);

    string[] textures =
    [
        "random/wood.png",
        "random/stone.png",
        "random/mosaic.png",
        "random/tiles.png",
        "random/metal.png",
    ];
    var materials = new MaterialData[textures.Length];
    for (int i = 0; i < textures.Length; i++)
        materials[i] = new MaterialData();

    var rng = new Random(1234567);
    const int n = 24;
    float cell = 4f / n;
    int count = 0;
    for (int r = 0; r < n; r++)
    for (int c = 0; c < n; c++)
    {
        float svgX = 32f + (c + 0.5f) / n * 540f;
        float svgY = 28.5f + (r + 0.5f) / n * 540f;
        (float X, float Y) p = (svgX, svgY);
        if (!Inside(p, mainSlash) && !Inside(p, hornTop) && !Inside(p, hornLow))
            continue;

        MaterialData cellMaterial = materials[rng.Next(textures.Length)];

        var cube = new SceneObject(PrimitiveFactory.CreateCube(), cellMaterial);
        cube.Transform.Position = new Vector3((c - (n - 1) / 2f) * cell, ((n - 1) / 2f - r) * cell, 0f);
        cube.Transform.Scale = new Vector3(0.16f);
        scene.Add(cube);

        var backCube = new SceneObject(PrimitiveFactory.CreateCube(), cellMaterial);
        backCube.Transform.Position = new Vector3((c - (n - 1) / 2f) * cell, ((n - 1) / 2f - r) * cell, -0.6f);
        backCube.Transform.Scale = new Vector3(0.16f);
        scene.Add(backCube);

        count += 2;
    }

    Console.WriteLine($"  Кубы эмблемы: {count} (по {count / 2} с каждой стороны плиты).");

    var camera = new Camera { Position = new Vector3(0f, 1f, 6f), Target = new Vector3(0f, 0f, 0f) };
    float t = 0f;
    RunWindow("Engine3D #10 - 17 dota 2 logo", scene, camera, onUpdate: deltaSeconds =>
    {
        t += deltaSeconds * 0.35f;
        camera.Position = new Vector3(MathF.Sin(t) * 6f, 1.2f + MathF.Sin(t * 0.5f) * 0.6f, MathF.Cos(t) * 6f);
        camera.Target = Vector3.Zero;
    }, onCreated: engine =>
    {
        plateMaterial.Texture = engine.LoadTexture(AssetPath("random/velvet.png"));
        Console.WriteLine("  загружено: random/velvet.png (фон)");
        for (int i = 0; i < textures.Length; i++)
        {
            materials[i].Texture = engine.LoadTexture(AssetPath(textures[i]));
            Console.WriteLine($"  загружено: {textures[i]}");
        }
    });
}

static class WindowConfig
{
    public static string OutDir = "";
    public static (int X, int Y)? ForcedPosition; // задано ключом --position

    private static readonly (int X, int Y) s_defaultPosition = (0, 40);
    private static readonly (int Width, int Height) s_defaultSize = (1280, 720);

    private static string StateFile => Path.Combine(OutDir, "window-placement.txt");

    // Приоритет: явный --position → запомненное положение из файла → значение по умолчанию.
    public static (int X, int Y, int Width, int Height) Resolve()
    {
        (int X, int Y, int Width, int Height)? saved = ReadSaved();
        return (
            ForcedPosition?.X ?? saved?.X ?? s_defaultPosition.X,
            ForcedPosition?.Y ?? saved?.Y ?? s_defaultPosition.Y,
            saved?.Width ?? s_defaultSize.Width,
            saved?.Height ?? s_defaultSize.Height);
    }

    public static void WriteSaved((int X, int Y, int Width, int Height) bounds)
    {
        if (string.IsNullOrEmpty(OutDir))
            return;

        Directory.CreateDirectory(OutDir);
        File.WriteAllText(StateFile, $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
    }

    public static (int X, int Y, int Width, int Height)? ReadSaved()
    {
        if (string.IsNullOrEmpty(OutDir) || !File.Exists(StateFile))
            return null;

        string[] parts = File.ReadAllText(StateFile).Trim().Split(',');
        if (parts.Length == 4
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0
            && height > 0)
        {
            return (x, y, width, height);
        }

        return null;
    }
}
