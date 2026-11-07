using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Engine3D.Core;
using Engine3D.OpenGL;

if (args.Contains("--help"))
{
    Console.WriteLine("OverlaySample [--smoke] [--still] [--trace] [--no-vsync]\nSpace: overlay on/off; R: reset rotation; Escape: exit. Cursor remains Normal.\n--no-vsync: compare pointer marker latency without waiting for vertical refresh.");
    return;
}
if (args.Any(arg => arg is not ("--smoke" or "--still" or "--trace" or "--no-vsync")))
{
    Console.Error.WriteLine("Unknown argument; use --help.");
    Environment.ExitCode = 2;
    return;
}

bool smoke = args.Contains("--smoke"), still = args.Contains("--still"), trace = args.Contains("--trace");
bool noVsync = args.Contains("--no-vsync");
string title = trace ? $"Engine3D OverlaySample {Environment.ProcessId}"
    : $"Engine3D - Overlay sample (VSync {(noVsync ? "off" : "on")})";
try
{
    using var engine = Engine.Create(new EngineOptions { Width = 960, Height = 640, Title = title, VSync = !noVsync });
    var texture = CreateImage();
    var scene = new Scene();
    scene.Lighting.AmbientColor = new Vector3(0.45f);
    scene.Lighting.Direction = new Vector3(-1, -2, -1);
    var mesh = PrimitiveFactory.CreateCube();
    var cube = new SceneObject(mesh, new MaterialData { BaseColor = new ColorRgba(1, 1, 1), Texture = texture });
    cube.Transform.Position = new Vector3(1, 0.2f, 0);
    cube.Transform.Scale = new Vector3(1.7f);
    scene.Add(cube);
    var back = new SceneObject(mesh, new MaterialData { BaseColor = new ColorRgba(0.2f, 0.65f, 1f) });
    back.Transform.Position = new Vector3(2, 0, -2.5f);
    back.Transform.Scale = new Vector3(2);
    scene.Add(back);
    var ground = new SceneObject(PrimitiveFactory.CreatePlane(30, 30),
        new MaterialData { BaseColor = new ColorRgba(0.16f, 0.23f, 0.32f) });
    ground.Transform.Position = new Vector3(0, -1, 0);
    scene.Add(ground);
    var camera = new Camera { Position = new Vector3(5, 3, 8), Target = new Vector3(0, 0, 0) };
    var timer = Stopwatch.StartNew();
    bool visible = true;
    float angle = 0;
    int updates = 0;
    double nextTrace = 0;
    OverlayFrame Build(Vector2 pointer, bool inside)
    {
        var client = engine.ClientSize;
        var framebuffer = engine.FramebufferSize;
        float card = Math.Clamp(client.Width * 0.36f, 260f, 350f);
        List<OverlayPanel> panels = [
            new(new UiRect(24, 24, card, 386), new ColorRgba(0.035f, 0.055f, 0.09f, 0.94f)),
            new(new UiRect(24, 24, card, 4), new ColorRgba(0.2f, 0.85f, 0.9f)),
            new(new UiRect(44, 238, card - 40, 2), new ColorRgba(0.2f, 0.3f, 0.4f)),
            new(new UiRect(44, 302, card - 40, 10), new ColorRgba(0.16f, 0.22f, 0.3f)),
            new(new UiRect(44, 302, (card - 40) * 0.75f, 10), new ColorRgba(0.2f, 0.85f, 0.9f)),
            new(new UiRect(client.Width - 180, 24, 156, 184), new ColorRgba(0.035f, 0.055f, 0.09f, 0.9f)),
            new(new UiRect(-12, client.Height - 9, client.Width + 24, 12), new ColorRgba(0.2f, 0.85f, 0.9f)),
            new(new UiRect(0, client.Height - 70, client.Width, 60), new ColorRgba(0.035f, 0.055f, 0.09f, 0.92f))];
        if (inside)
        {
            panels.Add(new(new UiRect(pointer.X - 7, pointer.Y - 1, 15, 3), new ColorRgba(1, 0.15f, 0.75f)));
            panels.Add(new(new UiRect(pointer.X - 1, pointer.Y - 7, 3, 15), new ColorRgba(1, 0.15f, 0.75f)));
        }
        ColorRgba white = new(0.95f, 0.98f, 1f), muted = new(0.6f, 0.7f, 0.8f), cyan = new(0.2f, 0.85f, 0.9f);
        return new OverlayFrame(panels,
            [new(new UiRect(client.Width - 164, 40, 124, 124), texture, new ColorRgba(0.85f, 1f, 1f))], [
            new(new Vector2(44, 45), "OVERLAY / 17", 36, white),
            new(new Vector2(44, 90), "PANELS + IMAGES + TEXT", 19, cyan),
            new(new Vector2(44, 135), "Training Track", 28, white),
            new(new Vector2(44, 179), "Coins 3/10", 26, white),
            new(new Vector2(44, 211), "Boost 1.2s", 22, muted),
            new(new Vector2(44, 260), "Volume 75%", 25, white),
            new(new Vector2(44, 335), "ASCII: !?+-*/()[]", 20, muted),
            new(new Vector2(44, 365), "Fallback: \u2603", 20, muted),
            new(new Vector2(client.Width - 164, 172), "IMAGE / TINT", 20, white),
            new(new Vector2(24, client.Height - 65), "Space: overlay on/off   R: reset   Esc: exit", 22, white),
            new(new Vector2(24, client.Height - 35), string.Create(CultureInfo.InvariantCulture,
                $"Client {client.Width}x{client.Height}  FB {framebuffer.Width}x{framebuffer.Height}  Pointer {pointer.X:F0},{pointer.Y:F0}"), 18, muted)]);
    }
    engine.SetOverlay(Build(Vector2.Zero, false));
    if (trace) Console.WriteLine(JsonSerializer.Serialize(new { kind = "ready", title, vsync = !noVsync }));
    else Console.WriteLine($"VSync: {(noVsync ? "off" : "on")}. Space: overlay on/off; R: reset; Escape: exit. Magenta marker follows logical pointer; --no-vsync compares presentation latency.");
    engine.Run(scene, camera, seconds =>
    {
        var input = engine.Input;
        if (!smoke)
        {
            if (input.WasPressed(InputKey.Escape)) engine.RequestClose();
            if (input.WasPressed(InputKey.Space)) visible = !visible;
            if (input.WasPressed(InputKey.R)) angle = 0;
        }
        if (!still) angle = MathF.IEEERemainder(angle + MathF.Min(seconds, 0.1f) * 0.5f, MathF.Tau);
        cube.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(angle, angle * 0.4f, 0);
        engine.SetOverlay(visible ? Build(input.Pointer.Position, input.Pointer.IsInsideClient) : null);
        updates++;
        if (trace && timer.Elapsed.TotalSeconds >= nextTrace)
        {
            nextTrace = timer.Elapsed.TotalSeconds + 0.02;
            var client = engine.ClientSize;
            var fb = engine.FramebufferSize;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "frame", frame = updates, rendered = engine.RenderedFrames,
                focused = input.IsFocused, visible, mode = engine.CursorMode.ToString(),
                x = input.Pointer.Position.X, y = input.Pointer.Position.Y, inside = input.Pointer.IsInsideClient,
                width = client.Width, height = client.Height, framebufferWidth = fb.Width, framebufferHeight = fb.Height }));
        }
        if (smoke && engine.RenderedFrames >= 3) engine.RequestClose();
        if (smoke && timer.Elapsed.TotalSeconds > 10) throw new TimeoutException("Overlay smoke did not render three frames.");
    });
    if (!trace) Console.WriteLine($"Rendered frames: {engine.RenderedFrames}; cursor: {engine.CursorMode}");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"OverlaySample: {exception.Message}");
    Environment.ExitCode = 1;
}

static TextureData CreateImage()
{
    const int size = 32;
    byte[] pixels = new byte[size * size * 4];
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = (y * size + x) * 4;
            (int r, int g, int b) = (x < size / 2, y < size / 2) switch
            {
                (true, true) => (45, 160, 245), (false, true) => (70, 220, 180),
                (true, false) => (250, 150, 65), _ => (245, 220, 115),
            };
            pixels[offset] = (byte)r;
            pixels[offset + 1] = (byte)g;
            pixels[offset + 2] = (byte)b;
            pixels[offset + 3] = 255;
        }
    return new TextureData(size, size, pixels);
}
