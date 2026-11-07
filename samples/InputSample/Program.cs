using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine3D.Core;
using Engine3D.OpenGL;

try
{
    if (args is ["--help"])
    {
        Console.WriteLine("InputSample [--smoke] [--trace]\nEnter: capture/release; Escape: release, then exit.\nWASD/arrows: move cube; Shift: faster; mouse: rotate; Space/buttons: color; R: reset.\n--smoke: three frames, no capture. --trace: JSON snapshots for native checks.");
        return;
    }
    if (args.Any(arg => arg is not ("--smoke" or "--trace")))
        throw new ArgumentException("Unknown arguments. Use --help.");
    bool smoke = args.Contains("--smoke"), trace = args.Contains("--trace");
    string title = trace ? $"Engine3D Input Probe {Environment.ProcessId}" : "Engine3D Input Sample — Enter: capture, Escape: release/exit";
    var scene = new Scene();
    var material = new MaterialData { BaseColor = new ColorRgba(0.2f, 0.6f, 1f) };
    var cube = new SceneObject(PrimitiveFactory.CreateCube(), material);
    scene.Add(cube);
    var camera = new Camera { Position = new Vector3(0f, 2f, 7f), Target = Vector3.Zero };
    using var engine = Engine.Create(new EngineOptions { Title = title, Width = 800, Height = 600, FocusOnShow = !smoke });
    var timer = Stopwatch.StartNew();
    double nextReport = 0d;
    int updates = 0;
    float yaw = 0f, pitch = 0f;
    bool? focused = null;
    if (trace) Console.WriteLine(JsonSerializer.Serialize(new { kind = "ready", title }));
    else Console.WriteLine("Enter: захват/освобождение; Escape: освободить, затем выйти. WASD/стрелки, Shift, мышь, Space/кнопки, R. Alt-Tab освобождает курсор.");
    engine.Run(scene, camera, seconds =>
    {
        var input = engine.Input;
        bool modeChanged = false;
        if (!smoke)
        {
            if (input.WasPressed(InputKey.Escape))
            {
                if (engine.CursorMode == CursorMode.Captured) { engine.CursorMode = CursorMode.Normal; modeChanged = true; }
                else engine.RequestClose();
            }
            else if (input.WasPressed(InputKey.Enter))
            {
                engine.CursorMode = engine.CursorMode == CursorMode.Normal ? CursorMode.Captured : CursorMode.Normal;
                modeChanged = true;
            }
            int Held(InputKey key) => input.IsDown(key) ? 1 : 0;
            Vector3 movement = new(Held(InputKey.D) + Held(InputKey.Right) - Held(InputKey.A) - Held(InputKey.Left),
                0f, Held(InputKey.S) + Held(InputKey.Down) - Held(InputKey.W) - Held(InputKey.Up));
            if (movement.LengthSquared() > 1f) movement = Vector3.Normalize(movement);
            float speed = input.IsDown(InputKey.LeftShift) || input.IsDown(InputKey.RightShift) ? 4f : 2f;
            cube.Transform.Position = Vector3.Clamp(cube.Transform.Position + movement * speed * MathF.Min(seconds, 0.1f),
                new Vector3(-3f, 0f, -3f), new Vector3(3f, 0f, 3f));
            if (!modeChanged && engine.CursorMode == CursorMode.Captured)
            {
                yaw = MathF.IEEERemainder(yaw + input.Pointer.Delta.X * 0.003f, MathF.Tau);
                pitch = MathF.IEEERemainder(pitch + input.Pointer.Delta.Y * 0.003f, MathF.Tau);
                cube.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0f);
            }
            material.BaseColor = input.IsDown(InputKey.Space) || input.Pointer.IsDown(MouseButton.Left) ? new ColorRgba(1f, 0.5f, 0.1f)
                : input.Pointer.IsDown(MouseButton.Right) ? new ColorRgba(0.3f, 1f, 0.3f)
                : input.Pointer.IsDown(MouseButton.Middle) ? new ColorRgba(1f, 0.3f, 1f) : new ColorRgba(0.2f, 0.6f, 1f);
            if (input.WasPressed(InputKey.R))
            {
                cube.Transform.Position = Vector3.Zero;
                cube.Transform.Rotation = Quaternion.Identity;
                yaw = pitch = 0f;
            }
        }
        updates++;
        if (trace)
        {
            var bounds = engine.ClientSize;
            var framebuffer = engine.FramebufferSize;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "frame", frame = updates, focused = input.IsFocused, mode = engine.CursorMode.ToString(),
                keys = Enum.GetValues<InputKey>().Where(input.IsDown).Select(key => key.ToString()),
                pressed = Enum.GetValues<InputKey>().Where(input.WasPressed).Select(key => key.ToString()),
                released = Enum.GetValues<InputKey>().Where(input.WasReleased).Select(key => key.ToString()),
                buttons = Enum.GetValues<MouseButton>().Where(input.Pointer.IsDown).Select(button => button.ToString()),
                mousePressed = Enum.GetValues<MouseButton>().Where(input.Pointer.WasPressed).Select(button => button.ToString()),
                mouseReleased = Enum.GetValues<MouseButton>().Where(input.Pointer.WasReleased).Select(button => button.ToString()),
                x = input.Pointer.Position.X, y = input.Pointer.Position.Y,
                dx = input.Pointer.Delta.X, dy = input.Pointer.Delta.Y, inside = input.Pointer.IsInsideClient,
                width = bounds.Width, height = bounds.Height, framebufferWidth = framebuffer.Width, framebufferHeight = framebuffer.Height,
            }));
        }
        else if (focused != input.IsFocused || modeChanged || timer.Elapsed.TotalSeconds >= nextReport)
        {
            Console.WriteLine($"focus={input.IsFocused} mode={engine.CursorMode} pointer={input.Pointer.Position} delta={input.Pointer.Delta} inside={input.Pointer.IsInsideClient} client={engine.ClientSize.Width}x{engine.ClientSize.Height} framebuffer={engine.FramebufferSize}");
            nextReport = timer.Elapsed.TotalSeconds + 0.5d;
        }
        focused = input.IsFocused;
        if (smoke && engine.RenderedFrames >= 3) engine.RequestClose();
        if (smoke && timer.Elapsed.TotalSeconds > 10d) throw new TimeoutException("Smoke did not render three frames.");
    });
    if (!trace) Console.WriteLine($"Rendered frames: {engine.RenderedFrames}; cursor: {engine.CursorMode}");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"InputSample: {exception.Message}");
    Environment.ExitCode = 1;
}
