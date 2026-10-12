using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Engine3D.Core;
using Engine3D.OpenGL;

// Ручная проверка того, что добавлено в ветке khmelev/9-opengl-renderer.
// Программа умеет:
//   (без аргументов)  ручной прогон всех кейсов: печатает описание, открывает окно отдельным
//                     процессом, спрашивает y/n одним символом и сама закрывает окно;
//   --only N [N...]   то же, но только для перечисленных кейсов;
//   --nth N           открыть только N-й сценарий в окне (N = 1..7);
//   --all             прогнать все сценарии по очереди без вопросов (окно закрывает человек).
// Кейсы и их порядок совпадают с массивом scenarios. Отдельный сценарий запускается в дочернем
// процессе, чтобы родительский мог параллельно читать y/n из терминала.
// Это не демо-приложение #11: файл предназначен только для ручной проверки и не коммитится.

var scenarios = new (string Name, Action Run)[]
{
    ("освещение и нормали (simple light, P0-10)", Scenario2_Lighting),
    ("поправка глубины проекции и диапазон near/far", Scenario4_DepthRange),
    ("облёт камерой: depth + общий MeshData", Scenario6_Orbit),
    ("полёт сквозь тоннель (камера + transform)", Scenario7_Tunnel),
    ("движущийся свет при движении камеры", Scenario8_MovingLight),
    ("дождь кубов: камера пролетает сквозь объём", Scenario9_CubeField),
    ("панорама камеры по большой сцене", Scenario10_Pan),
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

args = [.. cliArgs];

if (args.Length == 0)
    return await GuidedAsync(AllIndices(scenarios.Length), scenarios, descriptions, outDir);

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
    Console.WriteLine("Ручная проверка рендера #9. Использование:");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release -- --only 1 5");
    Console.WriteLine($"  dotnet run --project src/Engine3D.Demo --configuration Release -- --nth N   (N = 1..{count})");
    Console.WriteLine("  dotnet run --project src/Engine3D.Demo --configuration Release -- --all");
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
    Console.WriteLine(" Ручная проверка OpenGL-рендера #9 (ветка khmelev/9-opengl-renderer)");
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
// Пока окно открыто, его текущее положение и размер пишутся в файл, чтобы следующее окно (следующий кейс
// или повторный запуск) открылось там же, где пользователь оставил предыдущее — включая ручной перетаскиванием.
static void RunWindow(string title, Scene scene, Camera camera, Action<float>? onUpdate = null)
{
    using var engine = CreateEngine(title);
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
