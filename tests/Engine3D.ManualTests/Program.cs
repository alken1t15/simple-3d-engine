using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Engine3D.Core;
using Engine3D.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

string repoRoot = FindRepoRoot();
string configuration = AppContext.BaseDirectory.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal)
    ? "Release" : "Debug";
string demoPath = Path.Combine(repoRoot, "src", "Engine3D.Demo", "bin", configuration, "net10.0", "Engine3D.Demo.dll");
string texturePath = Path.Combine(repoRoot, "src", "Engine3D.Demo", "Assets", "control.png");
string workDir = Path.Combine(repoRoot, "artifacts", "manual-tests", "13");
string placementPath = Path.Combine(workDir, "window-placement.txt");

if (args is ["--child", var mode, .. var childArgs])
    return Child(mode, childArgs);

// Описание кейса 1 выводится после справки Demo.
const string DemoHelpDescription = "Справка Demo должна описывать запуск MVP и параметры проверки.";

// Верхняя граница ожидания автоматических запусков Demo. Визуальные кейсы ее не используют.
const int AutomaticDemoTimeoutMilliseconds = 120_000;
const int DemoTimeoutExitCode = int.MinValue;
const string DemoTimeoutMessage = "Demo не завершился за 120 секунд; процесс и дерево процессов остановлены.";

// Полный список из 13 кейсов issue #13.
ManualCase[] cases =
[
    new(1, "Запуск MVP demo", DemoHelpDescription, HelpCase),
    new(2, "Видимость куба и плоскости", "Видны текстурированный куб и плоскость на своих местах.", VisibilityCase),
    new(3, "Перекрытие по глубине", "Ближний объект перекрывает дальний; пол и кубы не просвечивают.", DepthCase),
    new(4, "Ориентация текстуры", "Буква F не зеркальна и не перевернута; цвета углов ожидаемые.", TextureCase),
    new(5, "Вращение объекта и движение камеры отдельно", "Два прогона: только вращение куба и только движение камеры.", MotionCase),
    new(6, "Несколько чистых циклов запуска/закрытия", "Три процесса Demo и три цикла Engine в одном процессе.", CycleCase),
    new(7, "API жизненного цикла", "Неверный размер, второй Run, Run/LoadTexture после Dispose и повторный Dispose.", ApiCase),
    new(8, "Исключение в onUpdate", "Run прерывается, исходное исключение сохраняется, следующий Engine работает.", OnUpdateCase),
    new(9, "RequestClose и ошибки файлов", "RequestClose; отсутствующий, битый, неподдерживаемый и недоступный файл.", RequestCloseFilesCase),
    new(10, "Отказ создания контекста", "Create без дисплея завершается ошибкой; иначе SKIP.", ContextFailureCase),
    new(11, "Сценарий C: свет", "Смена направления света меняет яркость граней; текстура и depth корректны.", LightingCase),
    new(12, "Сценарий D: сохранение и загрузка JSON", "Сохранить сцену, затем загрузить ее в новом процессе и сравнить.", RoundTripCase),
    new(13, "Ошибки JSON после загрузки", "Битый JSON, отсутствующая текстура и сцена без loader дают ошибки контракта.", JsonErrorsCase),
];

if (args is ["--help"] || args.Length == 0)
{
    PrintHelp();
    return 0;
}

if (args is ["--all"])
    return RunCases(cases);

if (args is ["--list"])
{
    foreach (ManualCase item in cases)
        Console.WriteLine($"{item.Number,2}. {item.Name}");
    return 0;
}

if (args is ["--case", var value] && int.TryParse(value, out int number))
{
    ManualCase? one = cases.FirstOrDefault(item => item.Number == number);
    if (one is null)
    {
        Console.Error.WriteLine($"Кейс {number} не найден.");
        return 1;
    }

    return RunCases([one]);
}

Console.Error.WriteLine($"Неизвестный аргумент: {string.Join(' ', args)}");
PrintHelp();
return 1;

// Запускает выбранные кейсы и печатает итог.
int RunCases(IEnumerable<ManualCase> selected)
{
    int passed = 0, failed = 0, skipped = 0;
    ManualCase[] list = selected.ToArray();
    Console.WriteLine("Ручной smoke #13. Для визуальных кейсов откройте окно и ответьте y/n в терминале.");

    foreach (ManualCase item in list)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {item.Number}. {item.Name} ===");
        if (item.Number != 1)
            Console.WriteLine(item.Description);
        int result = item.Run();
        if (result == 0) passed++;
        else if (result == 2) skipped++;
        else failed++;
    }

    Console.WriteLine();
    Console.WriteLine($"Итог: пройдено {passed}, провалено {failed}, пропущено {skipped} из {list.Length}.");
    return failed > 0 ? 1 : skipped > 0 ? 2 : 0;
}

// Печатает справку по аргументам харнесса.
static void PrintHelp()
{
    Console.WriteLine("""
        Ручной smoke #13.

        Использование:
          dotnet run --project tests/Engine3D.ManualTests -c Release -- --all     # запуск всех тест-кейсов
          dotnet run --project tests/Engine3D.ManualTests -c Release -- --list    # список тест-кейсов
          dotnet run --project tests/Engine3D.ManualTests -c Release -- --case N  # запуск только указанного тест-кейса
          dotnet run --project tests/Engine3D.ManualTests -c Release -- --help    # вывод этого сообщения
          dotnet run --project tests/Engine3D.ManualTests -c Release              # вывод этого сообщения
        """);
}

// Кейс 1: запускает Demo --help и подтверждает справку.
int HelpCase()
{
    Console.WriteLine("--- Справка Demo ---");
    int code = RunDemoWithEnv(["--help"], environment: null, quiet: false);
    Console.WriteLine("--- Конец справки Demo ---");
    if (code == DemoTimeoutExitCode)
        return Fail(DemoTimeoutMessage);
    if (code != 0)
        return Fail($"Demo --help завершился с кодом {code}.");

    Console.WriteLine(DemoHelpDescription);
    return PromptYesNo()
        ? Pass("справка Demo соответствует описанию.")
        : Fail("пользователь отметил несовпадение.");
}

// Кейс 2: проверяет видимость куба и плоскости.
int VisibilityCase() => ShowDemo("--static");

// Кейс 3: проверяет перекрытие по глубине.
int DepthCase() => ShowDemo("--static");

// Кейс 4: проверяет ориентацию текстуры.
int TextureCase() => ShowDemo("--static");

// Запускает Demo для визуального подтверждения.
int ShowDemo(params string[] demoArgs) =>
    RunVisual(null, continueOnly: false, "Совпало с описанием?", demoArgs);

// Запускает Demo с отдельным вопросом для сравнения сцен.
int ShowSceneComparison(string question, params string[] demoArgs) =>
    RunVisual(null, continueOnly: false, question, demoArgs);

// Первая фаза D: сохраняет JSON и ждет перехода.
int ShowRoundTripSave(string path) =>
    RunVisual(path, continueOnly: true, question: "", ["--static", "--save", path]);

// Показывает окно, ждет ответ и останавливает Demo.
int RunVisual(string? waitForFile, bool continueOnly, string question, params string[] demoArgs)
{
    if (Console.IsInputRedirected)
        return Fail("нужен интерактивный терминал для ответа y/n.");

    string[] effective = WithPlacement(demoArgs);
    using Process child = StartDemo(effective);
    bool proceed = continueOnly ? PromptContinue() : Prompt(question);
    if (proceed && waitForFile is not null)
        WaitForFile(waitForFile);
    bool exited = child.HasExited;
    int exitCode = exited ? child.ExitCode : 0;
    Stop(child);

    if (!proceed)
        return Fail("пользователь отметил несовпадение.");
    if (exited && exitCode != 0)
        return Fail($"Demo завершился до ответа с кодом {exitCode}.");
    return continueOnly
        ? Pass("сцена сохранена; переход ко второму этапу.")
        : Pass("визуальная проверка подтверждена.");
}

// Ждет появления непустого JSON после сохранения.
static void WaitForFile(string path)
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return;
        Thread.Sleep(50);
    }
}

// Показывает визуальный дочерний режим харнесса.
int ShowChild(string description, params string[] childArgs)
{
    if (Console.IsInputRedirected)
        return Fail("нужен интерактивный терминал для ответа y/n.");

    Console.WriteLine($"  {description}");
    using Process child = StartChild(childArgs);
    bool confirmed = PromptYesNo();
    bool exited = child.HasExited;
    int exitCode = exited ? child.ExitCode : 0;
    Stop(child);

    if (!confirmed)
        return Fail("пользователь отметил несовпадение.");
    if (exited && exitCode != 0)
        return Fail($"процесс завершился до ответа с кодом {exitCode}.");
    return Pass("визуальная проверка подтверждена.");
}

// Кейс 5: отдельно проверяет вращение куба и движение камеры.
int MotionCase()
{
    int rotate = ShowChild("5a: вращается только куб; камера неподвижна.", "--child", "motion", "rotate");
    int camera = ShowChild("5b: куб неподвижен; камера облетает сцену.", "--child", "motion", "camera");
    return rotate == 0 && camera == 0 ? 0 : 1;
}

// Кейс 6: проверяет циклы в отдельных процессах и в одном процессе.
int CycleCase()
{
    for (int cycle = 1; cycle <= 3; cycle++)
    {
        int code = RunDemo("--frames", "3", "--no-focus");
        if (code == DemoTimeoutExitCode)
            return Fail($"цикл {cycle}: {DemoTimeoutMessage}");
        if (code != 0)
            return Fail($"цикл {cycle} завершился с кодом {code}.");
    }

    int sameProcess = RunChildCore(["cycles"], environment: null);
    if (sameProcess != 0)
        return sameProcess == 2 ? Skip("циклы в одном процессе пропущены.") : sameProcess;

    return ConfirmCleanClose() ? Pass("три процесса и три цикла в одном процессе завершились штатно.") : 1;
}

// Кейс 7: проверяет API жизненного цикла Engine.
int ApiCase() => RunChild("api");

// Кейс 8: проверяет исключение onUpdate и следующий Engine.
int OnUpdateCase() => RunChild("onupdate");

// Кейс 9: проверяет RequestClose и ошибки файлов без сырого stderr.
int RequestCloseFilesCase()
{
    var checks = new Checks();
    DemoResult frames = RunDemoCaptured("--frames", "2", "--no-focus");
    if (frames.ExitCode == DemoTimeoutExitCode)
        return Fail(DemoTimeoutMessage);
    checks.Check(
        frames.ExitCode == 0
        && frames.Output.Contains("Завершено штатно.", StringComparison.Ordinal)
        && frames.Output.Contains("Показано кадров: 2.", StringComparison.Ordinal),
        "RequestClose",
        DemoFailure(frames, $"код {frames.ExitCode}, в выводе нет штатного завершения с двумя кадрами"));

    string directory = Path.Combine(workDir, "files");
    Directory.CreateDirectory(directory);
    string missing = Path.Combine(directory, "missing.json");
    string broken = Path.Combine(directory, "broken.json");
    File.WriteAllText(broken, "{ \"version\": 1, \"meshes\": [");

    DemoResult missingRun = RunDemoCaptured("--load", missing, "--no-focus");
    if (missingRun.ExitCode == DemoTimeoutExitCode)
        checks.Fail("отсутствующий файл", DemoTimeoutMessage);
    else
        checks.Check(
            missingRun.ExitCode != 0
            && missingRun.Error.Contains(missing, StringComparison.Ordinal)
            && missingRun.Error.Contains("Ошибка Demo:", StringComparison.Ordinal),
            "отсутствующий файл",
            $"код {missingRun.ExitCode}, в stderr нет пути или ошибки Demo");

    DemoResult brokenRun = RunDemoCaptured("--load", broken, "--no-focus");
    if (brokenRun.ExitCode == DemoTimeoutExitCode)
        checks.Fail("битый файл", DemoTimeoutMessage);
    else
        checks.Check(
            brokenRun.ExitCode != 0
            && brokenRun.Error.Contains(broken, StringComparison.Ordinal)
            && brokenRun.Error.Contains("$.meshes", StringComparison.Ordinal),
            "битый файл",
            $"код {brokenRun.ExitCode}, в stderr нет пути или места ошибки");

    string unsupported = Path.Combine(directory, "unsupported.txt");
    File.WriteAllText(unsupported, "not a supported scene or image");
    DemoResult unsupportedRun = RunDemoCaptured("--texture", unsupported, "--frames", "1", "--no-focus");
    if (unsupportedRun.ExitCode == DemoTimeoutExitCode)
        checks.Fail("неподдерживаемый файл", DemoTimeoutMessage);
    else
        checks.Check(
            unsupportedRun.ExitCode != 0
            && unsupportedRun.Error.Contains(unsupported, StringComparison.Ordinal)
            && unsupportedRun.Error.Contains("Ошибка Demo:", StringComparison.Ordinal),
            "неподдерживаемый файл",
            $"код {unsupportedRun.ExitCode}, в stderr нет пути или ошибки Demo");
    TryDelete(unsupported);

    if (OperatingSystem.IsWindows())
    {
        checks.Skip("недоступный файл", "проверка POSIX-прав не выполняется на Windows");
    }
    else
    {
        string unreadable = Path.Combine(directory, "unreadable.png");
        File.Copy(texturePath, unreadable, overwrite: true);
        File.SetUnixFileMode(unreadable, UnixFileMode.None);
        try
        {
            DemoResult unreadableRun = RunDemoCaptured("--texture", unreadable, "--frames", "1", "--no-focus");
            if (unreadableRun.ExitCode == DemoTimeoutExitCode)
                checks.Fail("недоступный файл", DemoTimeoutMessage);
            else if (unreadableRun.ExitCode == 0)
                checks.Skip("недоступный файл", "файл остался читаемым (возможно, запуск от root)");
            else
                checks.Check(
                    unreadableRun.Error.Contains(unreadable, StringComparison.Ordinal)
                    && unreadableRun.Error.Contains("Ошибка Demo:", StringComparison.Ordinal),
                    "недоступный файл",
                    $"код {unreadableRun.ExitCode}, в stderr нет пути или ошибки Demo");
        }
        finally
        {
            File.SetUnixFileMode(unreadable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            TryDelete(unreadable);
        }
    }

    if (checks.HasFailures || checks.HasSkips)
        return checks.Finish();
    return ConfirmCleanClose() ? Pass("RequestClose и ошибки файлов воспроизведены.") : 1;
}

// Кейс 10: проверяет отказ Create без дисплея и восстановление.
int ContextFailureCase()
{
    if (!OperatingSystem.IsLinux())
        return SkipCase("условие воспроизводится только на Linux");

    string? restoreDisplay = Environment.GetEnvironmentVariable("DISPLAY");
    var environment = new Dictionary<string, string?>
    {
        ["DISPLAY"] = "127.0.0.1:59999",
        ["WAYLAND_DISPLAY"] = null,
        ["XDG_SESSION_TYPE"] = "x11",
    };
    int code = RunChildCore(["context-failure", restoreDisplay ?? ""], environment);
    if (code == 3)
        return SkipCase("нет исходного DISPLAY для проверки восстановления");
    if (code != 0)
        return code == 2 ? Skip("условие контекста не воспроизведено.") : 1;
    return ConfirmCleanClose() ? Pass("Create без дисплея и восстановление контекста проверены.") : 1;
}

// Кейс 11: проверяет сценарий C со светом.
int LightingCase() => ShowDemo("--scenario", "lighting");

// Кейс 12: сохраняет сцену и открывает ее в новом процессе.
int RoundTripCase()
{
    string scene = Path.Combine(workDir, "roundtrip", "scene.json");
    Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
    File.Delete(scene);

    Console.WriteLine("  Фаза 1/2: Demo сохраняет JSON перед показом и открывает сцену.");
    int save = ShowRoundTripSave(scene);
    if (save != 0)
        return save;
    if (!File.Exists(scene) || new FileInfo(scene).Length == 0)
        return Fail($"JSON не создан: {scene}");

    Console.WriteLine($"  Сцена сохранена: {scene}");
    Console.WriteLine("  Фаза 2/2: откройте загруженную сцену и сравните ее с исходной.");
    return ShowSceneComparison(
        "Показанная сцена совпадает с предыдущей?",
        "--load", scene, "--frames", "100000");
}

// Запускает автоматический child-режим и просит подтвердить завершение.
// Кейс 13: проверяет ошибки JSON, текстуры и загрузчика.
int JsonErrorsCase() => RunChild("json-errors");

int RunChild(params string[] childArgs)
{
    int code = RunChildCore(childArgs, environment: null);
    return code switch
    {
        0 when ConfirmCleanClose() => Pass("автоматические проверки пройдены."),
        0 => 1,
        2 => Skip("условие не воспроизведено на этой платформе."),
        _ => Fail($"проверки не пройдены (код {code})."),
    };
}

// Запускает child-режим и возвращает его код без вопроса.
int RunChildCore(string[] childArgs, IReadOnlyDictionary<string, string?>? environment)
{
    using Process child = StartChild(["--child", .. childArgs], environment);
    if (!child.WaitForExit(120_000))
    {
        Stop(child);
        return Fail("дочерний процесс не завершился за 120 секунд.");
    }

    return child.ExitCode;
}

// Спрашивает, не было ли окна аварийного завершения ОС.
static bool ConfirmCleanClose() =>
    Prompt("Программа завершилась штатно, без ошибок ОС?");

// Запускает Demo без вывода его stdout.
int RunDemo(params string[] demoArgs) => RunDemoWithEnv(demoArgs, environment: null);

// Запускает Demo и перехватывает stdout/stderr для проверок.
DemoResult RunDemoCaptured(params string[] demoArgs)
{
    using Process child = StartDemo(demoArgs, environment: null, quietError: true, drain: false);
    Task<string> output = child.StandardOutput.ReadToEndAsync();
    Task<string> error = child.StandardError.ReadToEndAsync();
    if (!child.WaitForExit(AutomaticDemoTimeoutMilliseconds))
    {
        Stop(child);
        try
        {
            Task.WaitAll([output, error], 3000);
        }
        catch
        {
        }

        string stdout = output.IsCompletedSuccessfully ? output.Result : "";
        string stderr = error.IsCompletedSuccessfully ? error.Result : "";
        return new DemoResult(
            DemoTimeoutExitCode,
            stdout,
            $"{stderr}{Environment.NewLine}{DemoTimeoutMessage}");
    }

    return new DemoResult(child.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
}

// Запускает Demo с переменными окружения.
int RunDemoWithEnv(
    string[] demoArgs,
    IReadOnlyDictionary<string, string?>? environment,
    bool quiet = true,
    bool quietError = false)
{
    using Process child = StartDemo(demoArgs, environment, quiet, quietError);
    if (!child.WaitForExit(AutomaticDemoTimeoutMilliseconds))
    {
        Stop(child);
        Console.Error.WriteLine($"{DemoTimeoutMessage} Аргументы: {string.Join(' ', demoArgs)}");
        return DemoTimeoutExitCode;
    }

    return child.ExitCode;
}

// Возвращает причину провала запуска Demo с учетом timeout.
static string DemoFailure(DemoResult result, string detail) =>
    result.ExitCode == DemoTimeoutExitCode ? DemoTimeoutMessage : detail;

// Создает процесс Demo с нужными аргументами.
Process StartDemo(
    IReadOnlyList<string> demoArgs,
    IReadOnlyDictionary<string, string?>? environment = null,
    bool quiet = true,
    bool quietError = false,
    bool drain = true)
{
    if (!File.Exists(demoPath))
        throw new InvalidOperationException($"Demo не собран: {demoPath}. Выполните dotnet build Engine3D.sln -c {configuration}.");

    var startInfo = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = quiet,
        RedirectStandardError = quietError,
        WorkingDirectory = repoRoot,
    };
    startInfo.ArgumentList.Add(demoPath);
    foreach (string argument in demoArgs)
        startInfo.ArgumentList.Add(argument);
    ApplyEnvironment(startInfo, environment);
    Process child = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить Demo.");
    if (drain && quiet)
        _ = child.StandardOutput.ReadToEndAsync();
    if (drain && quietError)
        _ = child.StandardError.ReadToEndAsync();
    return child;
}

// Запускает этот же харнесс в дочернем режиме.
Process StartChild(
    IReadOnlyList<string> childArgs,
    IReadOnlyDictionary<string, string?>? environment = null)
{
    string host = Environment.ProcessPath ?? "dotnet";
    string? assembly = Assembly.GetEntryAssembly()?.Location;
    bool hostIsDotnet = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    var startInfo = new ProcessStartInfo(host)
    {
        UseShellExecute = false,
        WorkingDirectory = repoRoot,
    };
    if (hostIsDotnet && !string.IsNullOrEmpty(assembly))
        startInfo.ArgumentList.Add(assembly);
    foreach (string argument in childArgs)
        startInfo.ArgumentList.Add(argument);
    ApplyEnvironment(startInfo, environment);
    return Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить дочерний процесс.");
}

// Применяет переопределения переменных окружения к процессу.
static void ApplyEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string?>? environment)
{
    if (environment is null)
        return;
    foreach ((string key, string? value) in environment)
    {
        if (value is null)
            startInfo.Environment.Remove(key);
        else
            startInfo.Environment[key] = value;
    }
}

// Добавляет к аргументам Demo сохраненные границы окна.
string[] WithPlacement(IReadOnlyList<string> demoArgs)
{
    var placement = ReadPlacement();
    return
    [
        .. demoArgs,
        "--no-focus",
        "--position", $"{placement.X},{placement.Y}",
        "--size", $"{placement.Width}x{placement.Height}",
        "--bounds-out", placementPath,
    ];
}

// Читает сохраненные границы окна или возвращает значения по умолчанию.
(int X, int Y, int Width, int Height) ReadPlacement()
{
    try
    {
        string[] parts = File.ReadAllText(placementPath).Trim().Split(',');
        if (parts.Length == 4
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            && width > 0 && height > 0)
        {
            return (x, y, width, height);
        }
    }
    catch
    {
    }

    return (0, 40, 1280, 720);
}

// Сохраняет границы окна для следующих кейсов.
void SavePlacement((int X, int Y, int Width, int Height) placement)
{
    try
    {
        Directory.CreateDirectory(workDir);
        File.WriteAllText(placementPath, $"{placement.X},{placement.Y},{placement.Width},{placement.Height}");
    }
    catch
    {
    }
}

// Запоминает изменившиеся границы окна во время показа.
void Track(Engine engine, ref (int X, int Y, int Width, int Height) placement)
{
    var bounds = engine.WindowBounds;
    if (bounds.Width <= 0 || bounds.Height <= 0)
        return;
    var current = (bounds.X, bounds.Y, bounds.Width, bounds.Height);
    if (current != placement)
    {
        placement = current;
        SavePlacement(current);
    }
}

// Создает Engine с сохраненными границами и без захвата фокуса.
Engine CreateEngine(string title)
{
    var placement = ReadPlacement();
    return Engine.Create(new EngineOptions
    {
        Width = placement.Width,
        Height = placement.Height,
        Title = title,
        FocusOnShow = false,
        Position = (placement.X, placement.Y),
    });
}

// Показывает заданное число кадров и закрывает окно.
void RunFrames(Engine engine, Scene scene, Camera camera, int frames)
{
    var placement = ReadPlacement();
    engine.Run(scene, camera, _ =>
    {
        Track(engine, ref placement);
        if (engine.RenderedFrames >= frames)
            engine.RequestClose();
    });
    Track(engine, ref placement);
}

// Печатает PASS и возвращает код успеха.
static int Pass(string message)
{
    Console.WriteLine($"  PASS: {message}");
    return 0;
}

// Печатает FAIL и возвращает код ошибки.
static int Fail(string message)
{
    Console.Error.WriteLine($"  FAIL: {message}");
    return 1;
}

// Печатает SKIP и возвращает код пропуска.
static int Skip(string message)
{
    Console.WriteLine($"  SKIP: {message}");
    return 2;
}

// Спрашивает разрешение пропустить кейс.
static int SkipCase(string reason) =>
    Prompt($"Пропустить тест-кейс? ({reason})")
        ? Skip(reason)
        : Fail("пользователь не подтвердил пропуск тест-кейса.");

// Задает стандартный вопрос о совпадении.
static bool PromptYesNo() => Prompt("Совпало с описанием?");

// Ждет любую клавишу для перехода к следующей фазе.
static bool PromptContinue()
{
    Console.Write("Нажмите любую клавишу, чтобы перейти ко второму этапу...");
    if (Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Error.WriteLine("Нужен интерактивный терминал: клавиша вводится вручную.");
        return false;
    }

    Console.ReadKey(intercept: true);
    Console.WriteLine();
    return true;
}

// Печатает вопрос и читает ответ y/n из терминала.
static bool Prompt(string question)
{
    Console.Write($"{question} [y/N] ");
    if (Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Error.WriteLine("Нужен интерактивный терминал: ответ y/n вводится вручную.");
        return false;
    }

    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
    Console.WriteLine(key.KeyChar);
    return key.KeyChar is 'y' or 'Y';
}

// Завершает дочерний процесс вместе с окном.
static void Stop(Process child)
{
    if (child.HasExited)
        return;
    try
    {
        child.Kill(entireProcessTree: true);
        child.WaitForExit(3000);
    }
    catch
    {
    }
}

// Удаляет временный файл, игнорируя ошибки.
static void TryDelete(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}

// Ищет корень репозитория по Engine3D.sln.
static string FindRepoRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Engine3D.sln")))
            return directory.FullName;
    }
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Engine3D.sln")))
            return directory.FullName;
    }
    throw new InvalidOperationException("Не удалось найти корень репозитория: Engine3D.sln не найден.");
}

// Диспетчер внутренних дочерних режимов.
int Child(string mode, string[] childArgs) => mode switch
{
    "motion" => Motion(childArgs.ElementAtOrDefault(0)),
    "cycles" => Cycles(),
    "api" => Api(),
    "onupdate" => OnUpdate(),
    "context-failure" => ContextFailure(childArgs.ElementAtOrDefault(0)),
    "json-errors" => JsonErrors(),
    _ => UnknownChild(mode),
};

// Сообщает о неизвестном child-режиме.
static int UnknownChild(string mode)
{
    Console.Error.WriteLine($"Неизвестный child-режим '{mode}'.");
    return 1;
}

// Вращает куб или двигает камеру в отдельном окне.
int Motion(string? mode)
{
    if (mode is not ("rotate" or "camera"))
        return UnknownChild(mode ?? "<нет>");

    var (scene, camera, cube) = MotionScene();
    using var engine = CreateEngine($"Engine3D manual #13 - {mode}");
    var placement = ReadPlacement();
    float time = 0f;
    engine.Run(scene, camera, deltaSeconds =>
    {
        Track(engine, ref placement);
        time += deltaSeconds;
        if (mode == "rotate")
        {
            cube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, time);
        }
        else
        {
            camera.Position = new Vector3(MathF.Cos(time * 0.6f) * 6f, 2.2f, MathF.Sin(time * 0.6f) * 6f);
            camera.Target = new Vector3(0f, 0.3f, 0f);
        }
    });
    Track(engine, ref placement);
    return 0;
}

// Три цикла Create/Run/Dispose в одном процессе.
int Cycles()
{
    var checks = new Checks();
    for (int cycle = 1; cycle <= 3; cycle++)
    {
        try
        {
            var (scene, camera, _) = MotionScene();
            using var engine = CreateEngine($"Engine3D manual #13 - cycle {cycle}");
            RunFrames(engine, scene, camera, 2);
            checks.Check(engine.RenderedFrames >= 2, $"цикл {cycle}", "кадров меньше 2");
        }
        catch (Exception exception)
        {
            checks.Fail($"цикл {cycle}", $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    return checks.Finish();
}

// Проверяет отказ контекста и восстановление после него.
int ContextFailure(string? restoreDisplay)
{
    var checks = new Checks();
    GLFW.InitHint(InitHintPlatform.Platform, Platform.X11);

    for (int attempt = 1; attempt <= 2; attempt++)
    {
        try
        {
            using var engine = CreateEngine($"Engine3D manual #13 - no display {attempt}");
            checks.Fail($"попытка {attempt}", "Engine.Create неожиданно создал окно");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("Failed to create an OpenGL 3.3 core window", StringComparison.Ordinal)
                  && exception.Message.Contains("PlatformUnavailable", StringComparison.Ordinal))
        {
            checks.Pass($"попытка {attempt}");
        }
        catch (Exception exception)
        {
            checks.Fail($"попытка {attempt}", $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    if (checks.HasFailures)
        return checks.Finish();
    if (string.IsNullOrEmpty(restoreDisplay))
        return 3;
    if (NativeMethods.SetEnvironmentVariable("DISPLAY", restoreDisplay, 1) != 0)
    {
        checks.Fail("восстановление DISPLAY", "setenv завершился ошибкой");
        return checks.Finish();
    }

    try
    {
        var (scene, camera, _) = MotionScene();
        using var engine = CreateEngine("Engine3D manual #13 - context restored");
        RunFrames(engine, scene, camera, 2);
        checks.Check(engine.RenderedFrames >= 2, "Create после восстановления", "кадров меньше 2");
    }
    catch (Exception exception)
    {
        checks.Fail("Create после восстановления", $"{exception.GetType().Name}: {exception.Message}");
    }

    return checks.Finish();
}

// Проверяет жизненный цикл и ошибки использования Engine.
int Api()
{
    var checks = new Checks();
    checks.Throws<ArgumentOutOfRangeException>("ширина 0", () => Engine.Create(new EngineOptions { Width = 0 }));
    checks.Throws<ArgumentOutOfRangeException>("высота 0", () => Engine.Create(new EngineOptions { Height = 0 }));

    var (scene, camera, _) = MotionScene();
    var engine = CreateEngine("Engine3D manual #13 - api");
    try
    {
        RunFrames(engine, scene, camera, 2);
        checks.Check(engine.RenderedFrames >= 2, "первый Run", "кадров меньше 2");
        checks.Throws<InvalidOperationException>("второй Run", () => engine.Run(scene, camera));
    }
    finally
    {
        engine.Dispose();
    }

    checks.Throws<ObjectDisposedException>("Run после Dispose", () => engine.Run(scene, camera));
    checks.Throws<ObjectDisposedException>("LoadTexture после Dispose", () => engine.LoadTexture(texturePath));
    checks.DoesNotThrow("повторный Dispose", engine.Dispose);
    return checks.Finish();
}

// Проверяет исключение onUpdate и следующий Engine.
int OnUpdate()
{
    var checks = new Checks();
    var (scene, camera, _) = MotionScene();
    var expected = new InvalidOperationException("sentinel onUpdate");
    var engine = CreateEngine("Engine3D manual #13 - onUpdate");
    try
    {
        try
        {
            engine.Run(scene, camera, _ => throw expected);
            checks.Fail("исключение onUpdate", "Run завершился без исключения");
        }
        catch (Exception exception)
        {
            checks.Check(ReferenceEquals(exception, expected), "исключение onUpdate", "исходное исключение не сохранено");
        }
    }
    finally
    {
        engine.Dispose();
    }

    checks.Throws<ObjectDisposedException>("Engine освобожден", () => engine.LoadTexture(texturePath));

    using var next = CreateEngine("Engine3D manual #13 - next");
    RunFrames(next, scene, camera, 2);
    checks.Check(next.RenderedFrames >= 2, "новый Engine показывает сцену", "кадров меньше 2");
    return checks.Finish();
}

// Проверяет ошибки JSON, текстуры и загрузчика.
int JsonErrors()
{
    var checks = new Checks();
    string directory = Path.Combine(workDir, "json-errors");
    Directory.CreateDirectory(directory);
    string validPath = Path.Combine(directory, "textured.json");
    string brokenPath = Path.Combine(directory, "broken.json");
    string missingTexture = Path.Combine(directory, "missing.png");

    var texture = new TextureData(1, 1, [255, 255, 255, 255], missingTexture);
    var scene = new Scene();
    scene.Add(new SceneObject(PrimitiveFactory.CreateCube(), new MaterialData { Texture = texture }));
    var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero };
    SceneSerializer.Save(validPath, scene, camera);

    File.WriteAllText(brokenPath, "{ \"version\": 1, \"meshes\": [");
    checks.Throws<InvalidDataException>("битый JSON",
        () => SceneSerializer.Load(brokenPath),
        exception => exception.Message.Contains(brokenPath, StringComparison.Ordinal)
                     && exception.Message.Contains("$", StringComparison.Ordinal));
    checks.Throws<InvalidOperationException>("сцена без loader",
        () => SceneSerializer.Load(validPath),
        exception => exception.Message.Contains("texture loader", StringComparison.OrdinalIgnoreCase));

    using var engine = CreateEngine("Engine3D manual #13 - json errors");
    checks.Throws<FileNotFoundException>("отсутствующая текстура",
        () => SceneSerializer.Load(validPath, engine.LoadTexture),
        exception => exception.Message.Contains(missingTexture, StringComparison.Ordinal));
    return checks.Finish();
}

// Создает простую сцену для API и motion-проверок.
static (Scene Scene, Camera Camera, SceneObject Cube) MotionScene()
{
    var scene = new Scene();
    scene.Lighting.AmbientColor = new Vector3(0.25f);
    scene.Lighting.DirectionalColor = Vector3.One;
    scene.Lighting.Direction = new Vector3(-0.5f, -1f, -0.4f);

    var cube = new SceneObject(PrimitiveFactory.CreateCube(), new MaterialData
    {
        BaseColor = new ColorRgba(1f, 0.75f, 0.3f),
    });
    cube.Transform.Position = new Vector3(0f, 0.6f, 0f);
    scene.Add(cube);

    var floor = new SceneObject(PrimitiveFactory.CreatePlane(8f, 8f), new MaterialData
    {
        BaseColor = new ColorRgba(0.35f, 0.4f, 0.45f),
    });
    floor.Transform.Position = new Vector3(0f, -0.4f, 0f);
    scene.Add(floor);

    var camera = new Camera
    {
        Position = new Vector3(3f, 2.2f, 6f),
        Target = new Vector3(0f, 0.3f, 0f),
    };
    return (scene, camera, cube);
}

// Описание одного ручного кейса.
sealed record ManualCase(int Number, string Name, string Description, Func<int> Run);

// Результат запуска Demo с захваченным выводом.
sealed record DemoResult(int ExitCode, string Output, string Error);

// Низкоуровневые вызовы для восстановления DISPLAY.
static class NativeMethods
{
    // Восстанавливает DISPLAY для GLFW после проверки недоступного дисплея.
    [DllImport("libc", EntryPoint = "setenv")]
    public static extern int SetEnvironmentVariable(string name, string value, int overwrite);
}

// Накапливает результаты CHECK и итоговый статус.
sealed class Checks
{
    private int _failures;
    private int _skips;

    // Есть ли проваленные проверки.
    public bool HasFailures => _failures > 0;

    // Есть ли пропущенные проверки.
    public bool HasSkips => _skips > 0;

    // Отмечает проверку пройденной.
    public void Pass(string name) => Write("PASS", name);

    // Отмечает проверку проваленной.
    public void Fail(string name, string detail = "")
    {
        _failures++;
        Write("FAIL", name, detail);
    }

    // Отмечает проверку пропущенной.
    public void Skip(string name, string detail = "")
    {
        _skips++;
        Write("SKIP", name, detail);
    }

    // Проверяет условие и печатает PASS/FAIL.
    public void Check(bool condition, string name, string failDetail)
    {
        if (condition) Pass(name);
        else Fail(name, failDetail);
    }

    // Проверяет, что действие не бросает исключение.
    public void DoesNotThrow(string name, Action action)
    {
        try
        {
            action();
            Pass(name);
        }
        catch (Exception exception)
        {
            Fail(name, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    // Проверяет тип исключения и при необходимости сообщение.
    public void Throws<T>(string name, Action action, Func<T, bool>? predicate = null) where T : Exception
    {
        try
        {
            action();
            Fail(name, $"ожидалось {typeof(T).Name}, исключения нет");
        }
        catch (T exception) when (predicate is null || predicate(exception))
        {
            Pass(name);
        }
        catch (T exception)
        {
            Fail(name, $"{typeof(T).Name}, но сообщение не совпало: {exception.Message}");
        }
        catch (Exception exception)
        {
            Fail(name, $"ожидалось {typeof(T).Name}, получено {exception.GetType().Name}: {exception.Message}");
        }
    }

    // Печатает итог и возвращает код завершения.
    public int Finish()
    {
        string result = _failures > 0 ? "FAIL" : _skips > 0 ? "SKIP" : "PASS";
        Console.WriteLine($"RESULT: {result}");
        return _failures > 0 ? 1 : _skips > 0 ? 2 : 0;
    }

    // Печатает одну строку CHECK.
    private static void Write(string status, string name, string detail = "")
    {
        string suffix = string.IsNullOrWhiteSpace(detail) ? "" : $": {detail}";
        Console.WriteLine($"CHECK {name}: {status}{suffix}");
    }
}
