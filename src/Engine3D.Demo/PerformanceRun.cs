using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Engine3D.Core;
using Engine3D.OpenGL;

namespace Engine3D.Demo;

internal static class PerformanceRun
{
    internal static void Execute(PerformanceOptions options)
    {
        if (options.Help)
        {
            Console.WriteLine(PerformanceOptions.Usage);
            return;
        }

        // Не перезаписываем протокол предыдущего запуска. Все записи — вне измеряемого интервала.
        if (Directory.Exists(options.OutputDirectory) || File.Exists(options.OutputDirectory))
            throw new IOException("Каталог --output уже существует: выберите новый для отдельного замера.");
        Directory.CreateDirectory(options.OutputDirectory);
        using var process = Process.GetCurrentProcess();
        using var engine = Engine.Create(new EngineOptions
        {
            Width = options.Width,
            Height = options.Height,
            VSync = options.VSync,
            Title = "Engine3D — performance #14 (1000 lit cubes)",
        });
        var snapshot = PerformanceScene.Create(engine.LoadTexture(Path.Combine(AppContext.BaseDirectory, "Assets", "control.png")));
        var measurement = new PerformanceMeasurement(options.WarmupSeconds, options.DurationSeconds);
        var initialBuffer = engine.FramebufferSize;
        var initialBounds = engine.WindowBounds;
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        Console.WriteLine($"1000 кубов / 12000 треугольников, VSync={options.VSync}, запрошен клиент {options.Width}x{options.Height}.");
        Console.WriteLine($"Прогрев {options.WarmupSeconds} с + замер {options.DurationSeconds} с. Не сворачивайте и не меняйте размер окна.");
        Console.WriteLine("RequestClose завершит замер автоматически. Результат будет записан после Run.");

        // Старт в первом callback: создание сцены, PNG и показ окна не отнимают время прогрева.
        var clock = new Stopwatch();
        // Не создаём новый delegate на каждом callback: это исказило бы наблюдение managed-памяти.
        Func<MemorySample> readMemory = ReadMemory;
        Exception? failure = null;
        try
        {
            engine.Run(snapshot.Scene, snapshot.Camera, _ =>
            {
                var bounds = engine.WindowBounds;
                var buffer = engine.FramebufferSize;
                if (!clock.IsRunning)
                {
                    // Оконная система может скорректировать размер при первом показе скрытого окна.
                    // Прогрев начинается только после фиксации реально показанного клиента/framebuffer.
                    initialBounds = bounds;
                    initialBuffer = buffer;
                    Console.WriteLine($"Показанное окно: клиент {bounds.Width}x{bounds.Height}, framebuffer {buffer.Width}x{buffer.Height}.");
                    clock.Start();
                }
                bool sizeChanged = bounds.Width != initialBounds.Width || bounds.Height != initialBounds.Height || buffer != initialBuffer;
                if (sizeChanged && measurement.StartSeconds is null && buffer.Width > 0 && buffer.Height > 0)
                {
                    // Например, tiling WM раскладывает новое окно в несколько этапов. После последнего
                    // изменения до замера нужен заново полный прогрев при зафиксированном разрешении.
                    initialBounds = bounds;
                    initialBuffer = buffer;
                    measurement = new PerformanceMeasurement(options.WarmupSeconds, options.DurationSeconds);
                    clock.Restart();
                    Console.WriteLine($"До замера изменился размер: клиент {bounds.Width}x{bounds.Height}, framebuffer {buffer.Width}x{buffer.Height}; прогрев заново.");
                }
                else if (sizeChanged || buffer.Width <= 0 || buffer.Height <= 0)
                    measurement.Invalidate($"Размер окна/framebuffer изменился или буфер недоступен: клиент {bounds.Width}x{bounds.Height}, framebuffer {buffer.Width}x{buffer.Height}; повторите запуск без resize/minimize.");

                if (measurement.Observe(clock.Elapsed.TotalSeconds, engine.RenderedFrames, readMemory))
                    engine.RequestClose();
            });
        }
        catch (Exception exception)
        {
            failure = exception;
            measurement.Invalidate($"Run завершился с ошибкой: {exception.Message}");
            throw;
        }
        finally
        {
            // После Run сохраняем конечные счётчики и время, включая его завершающую часть.
            clock.Stop();
            try
            {
                measurement.Finish(clock.Elapsed.TotalSeconds, engine.RenderedFrames, readMemory);
                WriteReport(options, snapshot, measurement, initialBuffer, initialBounds, startedUtc);
            }
            catch (Exception exception) when (failure is not null)
            {
                // Не скрываем исходную ошибку движка ошибкой записи отчёта.
                Console.Error.WriteLine($"Не удалось записать протокол: {exception.Message}");
            }
        }
        Console.WriteLine(FormattableString.Invariant($"{measurement.Status}: {measurement.Fps:F2} FPS; {measurement.MeanFrameMs:F3} ms/frame (среднее); {measurement.MeasuredFrames} кадров за {measurement.MeasuredSeconds:F3} с."));
        Console.WriteLine($"Протокол: {Path.Combine(options.OutputDirectory, "report.md")}");
        if (measurement.Status != "complete")
            Environment.ExitCode = 2;

        MemorySample ReadMemory()
        {
            process.Refresh();
            return new MemorySample(process.WorkingSet64, GC.GetTotalMemory(forceFullCollection: false));
        }
    }

    internal static void WriteReport(PerformanceOptions options, SceneSnapshot snapshot, PerformanceMeasurement measurement,
        (int Width, int Height) buffer, (int X, int Y, int Width, int Height) bounds, DateTimeOffset startedUtc)
    {
        var document = new
        {
            SchemaVersion = 1,
            StartedUtc = startedUtc,
            AssemblyVersion = typeof(PerformanceRun).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Environment = new
            {
                OS = RuntimeInformation.OSDescription,
                Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                LogicalProcessors = System.Environment.ProcessorCount,
                HardwareDescription = options.Hardware,
            },
            Workload = new
            {
                Objects = snapshot.Scene.Objects.Count,
                Triangles = snapshot.Scene.Objects.Sum(item => item.Mesh.TriangleCount),
                UniqueMeshes = snapshot.Scene.Objects.Select(item => item.Mesh).Distinct(ReferenceEqualityComparer.Instance).Count(),
                UniqueTextures = snapshot.Scene.Objects.Select(item => item.Material.Texture).Distinct(ReferenceEqualityComparer.Instance).Count(),
                Grid = "10x10x10, spacing 2, cube size 1; static camera/objects/light",
                Texture = "Assets/control.png",
                Ambient = Components(snapshot.Scene.Lighting.AmbientColor),
                Directional = Components(snapshot.Scene.Lighting.DirectionalColor),
                LightDirection = Components(snapshot.Scene.Lighting.Direction),
                CameraPosition = Components(snapshot.Camera.Position),
                CameraTarget = Components(snapshot.Camera.Target),
                snapshot.Camera.FieldOfViewDegrees,
                snapshot.Camera.NearPlane,
                snapshot.Camera.FarPlane,
            },
            Window = new
            {
                RequestedWidth = options.Width,
                RequestedHeight = options.Height,
                ClientWidth = bounds.Width,
                ClientHeight = bounds.Height,
                FramebufferWidth = buffer.Width,
                FramebufferHeight = buffer.Height,
                RequestedVSync = options.VSync,
            },
            RequestedWarmupSeconds = options.WarmupSeconds,
            RequestedDurationSeconds = options.DurationSeconds,
            measurement.Status,
            measurement.InvalidReason,
            measurement.StartSeconds,
            measurement.StartFrames,
            measurement.EndSeconds,
            measurement.EndFrames,
            measurement.MeasuredSeconds,
            measurement.MeasuredFrames,
            measurement.Fps,
            measurement.MeanFrameMs,
            measurement.StartMemory,
            measurement.Samples,
        };
        File.WriteAllText(Path.Combine(options.OutputDirectory, "measurement.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));

        var report = new StringBuilder();
        report.AppendLine("# Протокол performance scene #14");
        report.AppendLine();
        report.AppendLine($"- Начало UTC: {startedUtc:O}");
        report.AppendLine($"- Состояние: `{measurement.Status}`; {measurement.InvalidReason ?? "без обнаруженного изменения размера окна в интервале измерения"}");
        report.AppendLine($"- Стенд (ввод оператора): {options.Hardware}");
        report.AppendLine($"- ОС: {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"- Нагрузка: 1000 кубов, 12000 треугольников; 1 CPU MeshData, 1 MaterialData, 1 TextureData; статическая сетка 10×10×10 с шагом 2");
        report.AppendLine($"- Клиент: {bounds.Width}×{bounds.Height}; framebuffer: {buffer.Width}×{buffer.Height} физических пикселей; запрошен VSync={options.VSync}");
        report.AppendLine("- Свет: ambient RGB=(0.25,0.25,0.25), directional RGB=(0.75,0.75,0.75), направление=(-1,-1.5,-2); текстура control.png, baseColor=(1,0.8,0.6)");
        report.AppendLine("- Камера: (35,30,40) → (9,9,9), FOV=60°, near=0.1, far=200; геометрия до прогрева валидируется, JSON не используется");
        report.AppendLine("- Время Stopwatch — от начала последнего полного прогрева: изменение размеров до замера перезапускает прогрев; после начала замера делает результат invalid");
        report.AppendLine(FormattableString.Invariant($"- Граница прогрева: {measurement.StartSeconds:F6} с / RenderedFrames={measurement.StartFrames}; после Run: {measurement.EndSeconds:F6} с / RenderedFrames={measurement.EndFrames}"));
        report.AppendLine(FormattableString.Invariant($"- Замер: {measurement.MeasuredFrames} кадров / {measurement.MeasuredSeconds:F6} с = {measurement.Fps:F3} FPS; средний frame time {measurement.MeanFrameMs:F3} ms"));
        report.AppendLine();
        report.AppendLine("## Интервалы и память");
        report.AppendLine();
        report.AppendLine("Интервалы около 1 с, последний может быть короче; фактические границы и все метрики — в measurement.json. В них учитываются RenderedFrames, включая refresh, а не число callbacks. Память: working set процесса и оценка живой managed-памяти без принудительного GC; это не GPU memory и не точный размер выделений.");
        if (measurement.StartMemory is { } start && measurement.Samples.Count > 0)
        {
            var last = measurement.Samples[^1].Memory;
            report.AppendLine();
            report.AppendLine(FormattableString.Invariant($"Working set: начало {MiB(start.WorkingSetBytes):F2} MiB → конец {MiB(last.WorkingSetBytes):F2} MiB, разница {MiB(last.WorkingSetBytes - start.WorkingSetBytes):F2} MiB; диапазон с учётом начала {MiB(Math.Min(start.WorkingSetBytes, measurement.Samples.Min(s => s.Memory.WorkingSetBytes))):F2}–{MiB(Math.Max(start.WorkingSetBytes, measurement.Samples.Max(s => s.Memory.WorkingSetBytes))):F2} MiB."));
            report.AppendLine(FormattableString.Invariant($"Managed: начало {MiB(start.ManagedBytes):F2} MiB → конец {MiB(last.ManagedBytes):F2} MiB. По этим точкам нельзя автоматически заключить отсутствие/наличие утечки; опишите форму ряда и повторите прогон."));
        }
        report.AppendLine();
        report.AppendLine("Пять самых медленных интервалов длительностью хотя бы 0.5 с (не отдельные кадры):");
        report.AppendLine();
        report.AppendLine("| От–до, с | Длительность, с | Кадры | FPS | Среднее, ms/frame | Max callback gap, ms |");
        report.AppendLine("|---|---|---|---|---|---|");
        foreach (var sample in measurement.Samples.Where(s => s.Seconds >= 0.5).OrderBy(s => s.Fps).Take(5))
            report.AppendLine(FormattableString.Invariant($"| {sample.FromSeconds:F3}–{sample.ToSeconds:F3} | {sample.Seconds:F3} | {sample.Frames} | {sample.Fps:F2} | {sample.MeanFrameMs:F3} | {sample.MaxCallbackGapMs:F3} |"));
        report.AppendLine();
        report.AppendLine("## Ограничения и ручное наблюдение");
        report.AppendLine();
        report.AppendLine("Среднее время кадра = длительность / число показанных кадров, не GPU timestamp. Max callback gap — интервал между опросами приложения с обработкой событий/рендером/swap/инструментированием, не точный frame time: refresh может показать несколько кадров без callback. Субсекундные просадки локализуются только до интервала наблюдения; p95/p99 и точная длительность отдельных зависаний не вычисляются. Запись файлов и JSON — после Run, память опрашивается примерно раз в секунду; стоимость опроса входит в замер.");
        report.AppendLine();
        report.AppendLine("- Визуальные просадки: **заполнить оператору** — где, как долго приблизительно, разовые/повторяющиеся; сопоставить с интервалами, не объявлять средний FPS доказательством плавности");
        report.AppendLine("- Тренд памяти по полному ряду: **заполнить оператору** — плато/колебания/рост/падение; численного порога утечки не задано");
        report.AppendLine("- GPU-кэш: **приложить результат** отдельного performance-cache-smoke для этой сцены (1 GPU mesh и 1 GPU texture после показа; 0 после Dispose); данный FPS-прогон внутренние GPU handles не читает");
        report.AppendLine("- Условия стенда: **дополнить** CPU/RAM/GPU/драйвер, SDK/OpenTK, дисплей/частота/масштаб, питание/профиль, фоновые процессы, версия исходников, #13 smoke; фактическое ограничение VSync драйвером отдельно от запрошенного");
        report.AppendLine("- Финальная приёмка: **не подтверждена автоматически** — нужен выбранный Linux-ПК VladimirKhmelev; результаты других машин/llvmpipe лишь дополнительные. 30–60 FPS — ориентир Team Lead, не гарантия преподавателя");
        File.WriteAllText(Path.Combine(options.OutputDirectory, "report.md"), report.ToString());
    }

    private static float[] Components(System.Numerics.Vector3 vector) => [vector.X, vector.Y, vector.Z];
    private static double MiB(long bytes) => bytes / (1024d * 1024d);
}
