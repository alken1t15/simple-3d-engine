using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;

// Минимальный стенд #10, не полное демонстрационное приложение #11.
// Необязательный первый аргумент — путь PNG; второй — число кадров для автоматического smoke-запуска.
try
{
    string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Assets", "control.png");
    int? frameLimit = args.Length > 1 ? int.Parse(args[1]) : null;
    if (frameLimit is <= 0)
        throw new ArgumentException("Frame limit must be positive.");

    using var engine = Engine.Create(new EngineOptions { Width = 960, Height = 640, Title = "Engine3D — texture control (#10)" });
    var texture = engine.LoadTexture(path);
    var mesh = PrimitiveFactory.CreateCube();
    var scene = new Scene();
    scene.Lighting.AmbientColor = new Vector3(0.4f);
    scene.Lighting.DirectionalColor = new Vector3(0.6f);
    scene.Lighting.Direction = new Vector3(-1f, -1f, -2f);
    for (int i = 0; i < 3; i++)
    {
        // Первые два разных материала разделяют одну CPU-текстуру; третий остается без текстуры.
        var item = new SceneObject(mesh, new MaterialData
        {
            Texture = i < 2 ? texture : null,
            BaseColor = i == 1 ? new ColorRgba(1f, 0.6f, 0.6f) : new ColorRgba(1f, 1f, 1f),
        });
        item.Transform.Position = new Vector3((i - 1) * 1.6f, 0f, 0f);
        scene.Add(item);
    }
    var camera = new Camera { Position = new Vector3(0f, 1.6f, 5f), Target = Vector3.Zero };
    int frames = 0;
    engine.Run(scene, camera, _ =>
    {
        if (frameLimit is { } limit && frames++ >= limit)
            engine.RequestClose();
    });
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
