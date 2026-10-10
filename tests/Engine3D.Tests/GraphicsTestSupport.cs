using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTK.Windowing.Desktop;

namespace Engine3D.Tests;

/// <summary>Общие действия графических тестов: создание Engine, показ заданного числа кадров, чтение пикселей.</summary>
internal static class GraphicsTestSupport
{
    // Допуск на канал: округление к 8 битам (±0.5) и точность float у драйвера, в том числе программного llvmpipe.
    // Проверяемые уровни (51, 113, 198, 255) отличаются гораздо сильнее.
    private const int ColorTolerance = 2;

    /// <summary>
    /// MSTest выполняет тесты не на главном потоке процесса, а OpenTK по умолчанию разрешает GLFW только там
    /// (требование macOS). Engine сам проверяет, что его методы вызываются с потока Create, а тесты идут
    /// последовательно (DoNotParallelize), поэтому GLFW не используется из двух потоков одновременно.
    /// </summary>
    public static void AllowTestRunnerThreads() => GLFWProvider.CheckForMainThread = false;

    /// <summary>Небольшое окно без VSync: тесты не ждут частоту экрана.</summary>
    public static Engine CreateEngine(string title)
    {
        AllowTestRunnerThreads();
        return Engine.Create(new EngineOptions { Width = 400, Height = 300, Title = title, VSync = false });
    }

    /// <summary>
    /// Показывает <paramref name="frames"/> обычных кадров; перед кадром k вызывает <paramref name="beforeFrame"/>(k)
    /// из onUpdate, затем запрашивает закрытие. Перерисовки по запросу системы добавляются к счетчику Engine.
    /// </summary>
    public static void RunFrames(Engine engine, Scene scene, Camera camera, int frames, Action<int>? beforeFrame = null)
    {
        int frame = 0;
        engine.Run(scene, camera, _ =>
        {
            if (frame == frames)
            {
                engine.RequestClose();
                return;
            }

            // Счетчик увеличивается отдельно: в beforeFrame?.Invoke(frame++) при null аргумент не вычисляется.
            beforeFrame?.Invoke(frame);
            frame++;
        });
    }

    public static SceneObject AddObject(Scene scene, MeshData mesh, ColorRgba color, Vector3 position, Vector3? scale = null)
    {
        var item = new SceneObject(mesh, new MaterialData { BaseColor = color });
        item.Transform.Position = position;
        item.Transform.Scale = scale ?? Vector3.One;
        scene.Add(item);
        return item;
    }

    /// <summary>Ожидаемый уровень канала 8 бит для яркости после ограничения [0, 1].</summary>
    public static int Level(float intensity) => (int)MathF.Round(Math.Clamp(intensity, 0f, 1f) * 255f);

    public static void AssertColor(int r, int g, int b, Rgb actual, string what)
    {
        bool close = Math.Abs(actual.R - r) <= ColorTolerance
            && Math.Abs(actual.G - g) <= ColorTolerance
            && Math.Abs(actual.B - b) <= ColorTolerance;
        Assert.IsTrue(close, $"{what}: expected ({r}, {g}, {b}) ±{ColorTolerance}, actual {actual}.");
    }
}

internal readonly record struct Rgb(int R, int G, int B);

/// <summary>Отрисованный кадр из <c>Engine.FrameRendered</c>: RGBA8, строки снизу вверх.</summary>
internal sealed record Frame(int Width, int Height, byte[] Pixels)
{
    /// <summary>Пиксель с координатами от левого нижнего угла.</summary>
    public Rgb At(int x, int y)
    {
        int index = (y * Width + x) * 4;
        return new Rgb(Pixels[index], Pixels[index + 1], Pixels[index + 2]);
    }

    /// <summary>Пиксель, в который камера проецирует мировую точку (по матрицам Core; поправка глубины на x, y не влияет).</summary>
    public Rgb At(Camera camera, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), camera.GetViewMatrix() * camera.GetProjectionMatrix(Width / (float)Height));
        int x = (int)((clip.X / clip.W * 0.5f + 0.5f) * Width);
        int y = (int)((clip.Y / clip.W * 0.5f + 0.5f) * Height);
        return At(x, y);
    }
}
