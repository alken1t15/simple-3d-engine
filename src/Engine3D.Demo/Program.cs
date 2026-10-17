using Engine3D.Core;
using Engine3D.Demo;
using Engine3D.OpenGL;

try
{
    var options = DemoOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine(DemoOptions.Usage);
        return;
    }

    using var engine = Engine.Create(new EngineOptions
    {
        Width = options.Size?.Width ?? 960,
        Height = options.Size?.Height ?? 640,
        Title = $"Engine3D — Demo #11 ({(options.LoadPath is null ? options.Scenario.ToString() : "JSON")})",
        FocusOnShow = !options.NoFocus,
        Position = options.Position,
    });
    (int X, int Y, int Width, int Height)? savedBounds = null;
    savedBounds = SaveBoundsIfChanged(engine, options.BoundsOut, savedBounds);

    DemoScene? demo = null;
    SceneSnapshot snapshot;
    if (options.LoadPath is { } loadPath)
    {
        snapshot = SceneSerializer.Load(loadPath, engine.LoadTexture);
        Console.WriteLine($"Загружена статическая сцена: {Path.GetFullPath(loadPath)}");
    }
    else
    {
        demo = DemoScene.Create(engine.LoadTexture(options.TexturePath));
        snapshot = demo.Snapshot;
        if (options.Scenario == DemoScenario.RoundTrip)
        {
            Save(options.ScenePath, snapshot);
            // Загрузка текстур разрешена до Run. Показываем новую сцену, а не исходные объекты demo.
            snapshot = SceneSerializer.Load(options.ScenePath, engine.LoadTexture);
            Console.WriteLine("JSON загружен в новую сцену; показ восстановленных объектов, камеры и света без движения.");
        }
        else if (options.SavePath is { } savePath)
        {
            Save(savePath, snapshot);
        }
    }

    Console.WriteLine($"Объектов: {snapshot.Scene.Objects.Count}. Закройте окно для завершения.");
    if (options.LoadPath is null && !options.Static && options.Scenario == DemoScenario.Mvp)
        Console.WriteLine("MVP: куб вращается; камера облетает сцену за 24 секунды (другая сторона через 12 секунд).");
    else if (options.Scenario == DemoScenario.Lighting)
        Console.WriteLine("C: камера и объекты неподвижны; направление света меняется, полный оборот за 12 секунд.");

    engine.Run(snapshot.Scene, snapshot.Camera, deltaSeconds =>
    {
        savedBounds = SaveBoundsIfChanged(engine, options.BoundsOut, savedBounds);

        // Считаем показанные кадры: вызов callback, запросивший закрытие, не рисует новый кадр.
        if (options.FrameLimit is { } limit && engine.RenderedFrames >= limit)
        {
            engine.RequestClose();
            return;
        }

        if (options.LoadPath is null && !options.Static && options.Scenario != DemoScenario.RoundTrip)
            demo!.Update(options.Scenario, deltaSeconds);
    });
    SaveBoundsIfChanged(engine, options.BoundsOut, savedBounds);
    Console.WriteLine($"Завершено штатно. Показано кадров: {engine.RenderedFrames}.");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Ошибка Demo: {exception.Message}");
    Environment.ExitCode = 1;
}

static void Save(string path, SceneSnapshot snapshot)
{
    string fullPath = Path.GetFullPath(path);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    SceneSerializer.Save(fullPath, snapshot.Scene, snapshot.Camera);
    Console.WriteLine($"Сцена сохранена: {fullPath}");
}

// Пишет текущие границы окна в --bounds-out, только если они изменились.
static (int X, int Y, int Width, int Height)? SaveBoundsIfChanged(
    Engine engine,
    string? path,
    (int X, int Y, int Width, int Height)? previous)
{
    if (path is null)
        return previous;

    var bounds = engine.WindowBounds;
    if (bounds == previous || bounds.Width <= 0 || bounds.Height <= 0)
        return previous;

    try
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}");
        return bounds;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Не удалось записать положение окна: {exception.Message}");
        return bounds;
    }
}
