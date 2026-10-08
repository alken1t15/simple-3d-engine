# Публичный API — контракт версии 1

**Статус:** описан контракт версии 1 по результатам spike #5, 07.10.2026. CPU-типы `Engine3D.Core` из раздела 2 реализованы в #7, кроме `PrimitiveFactory` (#8) и `SceneSerializer`/`SceneSnapshot` (#25); остальное — контракт для реализации. Нормали/свет и JSON приняты через [PR #26](https://github.com/alken1t15/simple-3d-engine/pull/26) и сохранены; эта задача завершает базовые сигнатуры, CPU-доступ renderer, матрицы, lifecycle и ошибки по результатам spike #5. Принятие базовых дополнений подтверждается ревью PR. [Требования](requirements.md) · [Архитектура](architecture.md) · [Основной план](../project_plan.md).

## 1. Что должен уметь вызывающий код

1. Создать окно/движок одним вызовом и получить понятную ошибку при невозможности создать контекст.
2. Получить процедурный куб/плоскость или передать собственный индексный mesh с позицией, UV и нормалями.
3. Загрузить одну растровую текстуру, создать базовый материал и добавить несколько объектов в сцену.
4. Задать камеру и в каждом кадре менять `Transform` объекта без обращения к OpenGL.
5. Показать сцену, запросить закрытие, получить число показанных кадров для внешнего замера, освободить графические ресурсы через `using`.
6. Задать фоновый и один направленный рассеянный свет.
7. Сохранить собственную сцену в JSON и восстановить ее в новую сцену с камерой и светом.

Сцена не включает игровой мир: в API нет коллизий, физики, поведения объектов, переходов анимации и правил игры. `onUpdate` — callback вызывающего приложения; он меняет данные сцены, а рендерер только отображает их.

## 2. Типы и сигнатуры версии 1

Следующий блок — справочник сигнатур, без тел методов; это не готовый файл реализации. Канонические пространства имен — Engine3D.Core и Engine3D.OpenGL; математика — System.Numerics. CPU-доступ renderer и клиентские примеры проверяются в #6, реальные функции — в последующих задачах:

```csharp
// Engine3D.Core
public readonly record struct Vertex(Vector3 Position, Vector2 Uv, Vector3 Normal);

public sealed class MeshData
{
    public MeshData(IReadOnlyList<Vertex> vertices, IReadOnlyList<uint> indices);
    public ReadOnlySpan<Vertex> Vertices { get; }
    public ReadOnlySpan<uint> Indices { get; }
    public int IndexCount { get; }
    public int VertexCount { get; }
    public int TriangleCount { get; }
}

public static class PrimitiveFactory
{
    public static MeshData CreateCube(float size = 1f);
    public static MeshData CreatePlane(float width = 1f, float depth = 1f);
}

public sealed class TextureData
{
    public TextureData(int width, int height, ReadOnlySpan<byte> rgba, string? sourcePath = null);
    public ReadOnlySpan<byte> Pixels { get; }
    public string? SourcePath { get; }
    public int Width { get; }
    public int Height { get; }
    // RGBA8; данные после создания не меняются.
}

public readonly record struct ColorRgba(float R, float G, float B, float A = 1f);

public sealed class MaterialData
{
    public ColorRgba BaseColor { get; set; } = new(1f, 1f, 1f, 1f);
    public TextureData? Texture { get; set; }
    public void Validate();
}

public sealed class Transform
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
    public Matrix4x4 GetModelMatrix();
    public void Validate();
}

public sealed class SceneObject
{
    public SceneObject(MeshData mesh, MaterialData material);
    public MeshData Mesh { get; }
    public MaterialData Material { get; set; }
    public Transform Transform { get; } = new();
}

public sealed class SceneLighting
{
    public Vector3 AmbientColor { get; set; } = new(0.2f);
    public Vector3 DirectionalColor { get; set; } = Vector3.One;
    public Vector3 Direction { get; set; } = new(-1f, -1f, -1f);
    public Vector3 GetNormalizedDirection();
    public void Validate();
}

public sealed class Scene
{
    public SceneLighting Lighting { get; } = new();
    public IReadOnlyList<SceneObject> Objects { get; }
    public void Add(SceneObject item);
    public bool Remove(SceneObject item);
}

public sealed class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Target { get; set; } = -Vector3.UnitZ;
    public Vector3 Up { get; set; } = Vector3.UnitY;
    public float FieldOfViewDegrees { get; set; } = 60f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 100f;
    public Matrix4x4 GetViewMatrix();
    public Matrix4x4 GetProjectionMatrix(float aspectRatio);
    public void Validate();
}

public sealed record SceneSnapshot(Scene Scene, Camera Camera);

public static class SceneSerializer
{
    public static void Save(string path, Scene scene, Camera camera);
    public static SceneSnapshot Load(string path, Func<string, TextureData>? textureLoader = null);
}

// Engine3D.OpenGL
public sealed class EngineOptions
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public string Title { get; init; } = "Engine3D Demo";
    public bool VSync { get; init; } = true;
}

public sealed class Engine : IDisposable
{
    public static Engine Create(EngineOptions options);
    public TextureData LoadTexture(string path);
    public long RenderedFrames { get; }
    public void Run(Scene scene, Camera camera, Action<float>? onUpdate = null);
    public void RequestClose();
    public void Dispose();
}
```

`System.Numerics` используется в `Core`; все преобразования к форматам OpenGL находятся в `Engine3D.OpenGL`. `MeshData` и `TextureData` копируют входные массивы или иным способом гарантируют неизменность: renderer кеширует их по идентичности. Не выставлять изменяемые внутренние массивы, иначе CPU-данные и GPU-кэш разойдутся.

`MaterialData` и `Transform` допускают изменение во время `Run`, **на том же потоке**, на котором выполняется `onUpdate`. Сцена не обещает безопасную запись из фонового потока. `MaterialData.Texture` может ссылаться на одну `TextureData` во многих объектах.

## 3. Жизненный цикл и владение

| Действие | Правило |
|---|---|
| `Engine.Create` | Создает готовый графический контекст. Отдельного `Initialize` нет. При ошибке не возвращает частично созданный объект. |
| `LoadTexture` | Для P0 вызывается после `Create` и до `Run`; декодирует изображение в CPU-данные. Для P0 выбран PNG и декодер StbImageSharp; реализация — в #10. |
| `Run` | Блокирует вызывающий поток до закрытия окна или `RequestClose`; обрабатывает события, вызывает `onUpdate`, рендерит и показывает кадры. Для P0 один `Run` на один `Engine`. |
| `RenderedFrames` | Обнуляется перед `Run`, увеличивается после показанного кадра и доступен после возврата `Run`. Не равен числу вызовов `onUpdate`. |
| `RequestClose` | Просит завершить цикл при ближайшей обработке события; не освобождает ресурсы немедленно. |
| `Dispose` | Освобождает GPU-кэш, шейдеры, окно и контекст. Повторный вызов безопасен. `Scene`, `MeshData`, `TextureData` не вызывают `Dispose`. |

**Владелец GPU-ресурсов — `Engine`.** Публичный `MeshData` и `TextureData` содержат только управляемые CPU-данные. Одни и те же данные можно повторно использовать для 1000 объектов. Приложение отвечает за время жизни единственного `Engine` через `using`; ручной `Dispose` каждого mesh/texture в P0 не нужен. Цена этого решения — GPU-кэш хранит ранее использованные ресурсы до закрытия движка; она приемлема для коротких демонстрационных сцен и должна быть проверена нагрузкой.

## 4. Сценарий A — текстурированный куб

Пример использования контракта v1; функции движка в каркасе пока не реализованы.

```csharp
using Engine3D.Core;
using Engine3D.OpenGL;
using System.Numerics;

using var engine = Engine.Create(new EngineOptions { Width = 1280, Height = 720 });

var cubeMesh = PrimitiveFactory.CreateCube();
var floorMesh = PrimitiveFactory.CreatePlane(6f, 6f);
var texture = engine.LoadTexture("Assets/checker.png");

var scene = new Scene();
var cube = new SceneObject(cubeMesh, new MaterialData { Texture = texture });
cube.Transform.Position = new Vector3(0f, 0f, 0f);
scene.Add(cube);

var floor = new SceneObject(floorMesh, new MaterialData());
floor.Transform.Position = new Vector3(0f, -1.5f, 0f);
scene.Add(floor);

var camera = new Camera
{
    Position = new Vector3(3f, 2f, 5f),
    Target = Vector3.Zero
};

float angle = 0f;
engine.Run(scene, camera, deltaSeconds =>
{
    angle += deltaSeconds;
    cube.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
});
```

**Проверка:** нет вызовов `GL.*` в приложении; куб и плоскость имеют разные transform; куб вращается, камера задает ракурс, текстура ориентирована правильно, объекты перекрываются по глубине, `using` закрывает окно без падения. Это покрывает P0-01…P0-06 и часть P0-08.

## 5. Сценарий B — много объектов и замер

```csharp
using Engine3D.Core;
using Engine3D.OpenGL;
using System.Diagnostics;
using System.Numerics;

using var engine = Engine.Create(new EngineOptions { VSync = false });
var sharedMesh = PrimitiveFactory.CreateCube();
var sharedMaterial = new MaterialData();
var scene = new Scene();

for (int i = 0; i < 1000; i++)
{
    var item = new SceneObject(sharedMesh, sharedMaterial);
    item.Transform.Position = new((i % 10) * 2f, ((i / 10) % 10) * 2f, (i / 100) * 2f);
    scene.Add(item);
}

var camera = new Camera { Position = new(35f, 30f, 40f), Target = new(9f, 9f, 9f), FarPlane = 200f };
var timer = new Stopwatch();
long startFrames = 0;
TimeSpan? startTime = null;
engine.Run(scene, camera, deltaSeconds =>
{
    if (!timer.IsRunning) timer.Start();
    if (startTime is null && timer.Elapsed >= TimeSpan.FromSeconds(10))
    {
        startFrames = engine.RenderedFrames;
        startTime = timer.Elapsed;
    }
    if (startTime is { } start && timer.Elapsed - start >= TimeSpan.FromSeconds(60))
        engine.RequestClose();
});
timer.Stop();
if (startTime is { } measuredFrom)
{
    double seconds = (timer.Elapsed - measuredFrom).TotalSeconds;
    if (seconds > 0)
    {
        double fps = (engine.RenderedFrames - startFrames) / seconds;
        string status = seconds >= 60 ? "полный замер" : "неполный замер";
        Console.WriteLine($"{fps:F2} FPS over {seconds:F2} seconds ({status})");
    }
    else
        Console.WriteLine("Замер отменён: нет измеренного интервала.");
}
else
    Console.WriteLine("Замер отменён: окно закрыто до окончания прогрева.");
// Демо сохраняет число объектов/треугольников, разрешение, VSync и характеристики CPU/GPU в протоколе.
```

В нагрузочном протоколе первые 10 секунд — прогрев, следующие 60 секунд — замер. Размер окна во время замера не менять. При досрочном закрытии результат отмечается как неполный и не заменяет полный 60-секундный замер; закрытие до конца прогрева отменяет замер. Чтобы считать **только кадры периода замера**, демо запоминает `RenderedFrames` после прогрева и вычитает из финального значения. Измерять количество `onUpdate` вместо показанных кадров нельзя. Для подробного анализа просадок демо позднее может записывать покадровые timestamps; публичная система профилирования в P0 не требуется.

**Проверка:** 1000 объектов используют одну ссылку `MeshData` и одну ссылку `MaterialData`; внутренний GPU mesh создается один раз; приложение может закрыть окно автоматически после измерения; результат сопровождается сведениями о стенде. Это покрывает P0-02, P0-07, P0-08.

## 6. Ошибки и граничные случаи

| Случай | Ожидаемое поведение |
|---|---|
| Ширина/высота окна ≤ 0 | Ошибка аргумента до создания контекста. |
| Контекст OpenGL не создан | Понятная ошибка `Engine.Create`, без частично живого `Engine`. |
| Пустой mesh/индекс вне диапазона/число индексов не кратно 3 | `MeshData` отклоняет данные до GPU-загрузки. |
| Файл текстуры не существует или неподдерживаем | `LoadTexture` сообщает путь и причину; демо может показать сообщение и завершиться штатно. |
| Камера с `NearPlane ≤ 0` или `FarPlane ≤ NearPlane` | Ошибка параметров до кадра либо при вычислении проекции. |
| Вызов `Run` после `Dispose` или второй раз | Явная ошибка состояния; не создается второй скрытый цикл окна. |
| Исключение в callback | `Run` прерывается, `using` вызывает `Dispose`; ошибка не теряется. |

Семейство `EngineException`, `TryLoadTexture`, пользовательский logger и другие расширения **не требуются P0**. Ошибки должны быть понятными и проверяемыми, но не создают отдельной большой подсистемы.

## 7. Базовые решения версии 1 по результатам spike

### Границы реализации и CPU-доступ renderer

| Issue | Ответственность |
|---|---|
| #6 | Четыре проекта net10.0, CI и базовый контракт v1; расширения PR #26 сохранены. |
| #7 | CPU Vertex/MeshData, ColorRgba/MaterialData/TextureData, Scene/SceneObject/Transform/Camera и SceneLighting. Только стандартная .NET. |
| #8 | Фабрики cube/plane, UV/нормали, внутреннее описание происхождения примитива для Save. |
| #9 | Engine/EngineOptions, окно/цикл, GPU mesh-кэш, renderer/depth и свет. |
| #10 | Engine.LoadTexture, PNG-декодирование StbImageSharp, GPU texture-кэш. CPU-типы не дублируются. |
| #25 | SceneSerializer/SceneSnapshot и внутренние DTO System.Text.Json внутри Core; не новый проект и не второй декодер. |
| #11 / #12 / #13 | Demo A–D, CPU-тесты и ручная графическая приемка. |

MeshData копирует вершины/индексы, TextureData копирует входной rgba; Vertices/Indices/Pixels — ReadOnlySpan поверх собственных приватных массивов. Renderer получает span только на время upload, не хранит его между кадрами и не получает изменяемый массив. Изменение исходных массивов не меняет CPU-данные. Индексы uint32; IndexCount = Indices.Length, TriangleCount = IndexCount / 3. null vertices/indices — ArgumentNullException. Пустые vertices/indices, число индексов не кратное трем или индекс вне диапазона — ArgumentException до GPU; позиции/UV/нормали проверяются по разделу 8. Вырожденные треугольники допустимы. PrimitiveFactory требует положительные конечные size/width/depth; неверный размер — ArgumentOutOfRangeException.

Scene.Objects — живая readonly-коллекция в порядке добавления. Add/Remove(null) — ArgumentNullException; повторное Add той же ссылки — ArgumentException; Remove отсутствующего объекта возвращает false. SceneObject требует ненулевые Mesh/Material; setter Material не допускает null. Scene.Remove не инвалидирует общий mesh/texture и не освобождает GPU.

### Матрицы, координаты и UV

Основание — [принятые соглашения spike](spikes.md#принятые-соглашения). Правая система координат, Y вверх, камера в пространстве вида смотрит вдоль -Z; UV (0,0) снизу слева. Cube центрирован в начале координат, 24 вершины/36 индексов; plane лежит в XZ, нормаль +Y. CCW при взгляде снаружи; правила отрицательного Scale и нормалей — в разделе 8.

Core использует вектор-строку и вычисляет:

```text
Transform.GetModelMatrix(): Scale * Rotation(normalized quaternion) * Translation
Camera.GetViewMatrix(): Matrix4x4.CreateLookAt(Position, Target, Up)
Camera.GetProjectionMatrix(aspect): CreatePerspectiveFieldOfView(fovRadians, aspect, near, far)
combinedCore = model * view * projection
```

Core-проекция имеет NDC depth [0,1]. Только OpenGL-слой добавляет справа коррекцию: C = Identity, C.M33 = 2, C.M43 = -1; projectionGL = projection * C, то есть z' = 2z - w и NDC depth [-1,1]. Не применять ее дважды. Построчные поля M11..M44 передаются с transpose=false; shader видит транспонированную матрицу и использует `projection * view * model * vec4(position,1)`. NormalMatrix и отрицательный масштаб определены принятым разделом 8.

CPU RGBA8-строки идут сверху вниз. LoadTexture до Run только декодирует PNG, копирует RGBA8 и задает полный SourcePath; GPU upload выполняется лениво по идентичности TextureData и переворачивает строки ровно один раз. Это уточняет проверенный spike, где upload был в OnLoad; декодирование до Run не требует GPU. Размеры/буфер/SourcePath сохраняют контракт раздела 9.

### Состояния Engine, callbacks и ошибки

- Engine.Create проверяет options, положительные Width/Height и непустой Title; создает контекст OpenGL 3.3 core на вызывающем потоке. Ошибка аргумента — ArgumentException (null — ArgumentNullException); ошибка создания — InvalidOperationException с причиной/InnerException, без частично живого Engine. Частичные ресурсы освобождаются.
- Только поток Create может вызывать методы Engine; другой поток — InvalidOperationException. После Dispose — ObjectDisposedException, кроме повторного Dispose и чтения RenderedFrames. Нужен новый Engine для повторного показа.
- LoadTexture допускается после Create и до начала Run; позже — InvalidOperationException. Пустой path — ArgumentException, отсутствие файла — FileNotFoundException, доступ — UnauthorizedAccessException, прочие I/O — IOException, испорченный/неподдерживаемый PNG — InvalidDataException с путем и причиной. PNG обязательна, остальные форматы вне гарантии P0.
- Один блокирующий Run(scene,camera,onUpdate) на Engine. Второй Run — InvalidOperationException; null scene/camera — ArgumentNullException. Порядок: события → onUpdate → полная CPU-валидация изменяемого состояния → render → успешный swap → счетчик. deltaSeconds конечное неотрицательное реальное время между updates; первый callback получает 0.
- Refresh может перерисовать последнее состояние без onUpdate; следующий обычный update учитывает прошедшее время. Viewport/aspect используют FramebufferSize, а не логический размер окна. При нулевом framebuffer пропустить render/swap/счетчик. RenderedFrames начинается с 0, сбрасывается перед Run и включает каждый успешный swap, в том числе refresh, а не callback-вызовы.
- RequestClose во время Run запрашивает закрытие при ближайшей обработке событий; из onUpdate новый обычный кадр уже не показывается. Повтор безопасен. До Run — InvalidOperationException; после завершенного Run — no-op до Dispose.
- Активное закрытие из callback выполняется RequestClose. Dispose вызывается после возврата/исключения Run; внутри onUpdate — InvalidOperationException. Dispose идемпотентен, удаляет кэши/shaders до уничтожения контекста/окна. Ошибка одного удаления не отменяет попытки остальных.
- Невалидное изменяемое состояние после onUpdate и перед GL — InvalidOperationException по разделу 8. GetModelMatrix/GetViewMatrix/GetProjectionMatrix также сообщают InvalidOperationException при неверном состоянии Transform/Camera; неверный аргумент aspectRatio — ArgumentOutOfRangeException. Quaternion конечный/ненулевой нормализуется при вычислении, Scale конечный/ненулевой по всем осям; отрицательный Scale сохраняется. Camera требует конечные данные, ненулевой Up не параллельный взгляду, Position != Target, 0 < FOV < 180 и 0 < near < far; aspect конечный > 0.
- Проверки изменяемого состояния сосредоточены в Core (#7): `Transform.Validate()`, `Camera.Validate()`, `MaterialData.Validate()` и `SceneLighting.Validate()` проверяют текущие значения по разделам 7–9 и сообщают InvalidOperationException; `SceneLighting.GetNormalizedDirection()` проверяет свет и возвращает единичное направление. Присваивание свойств значений не проверяет, поэтому в onUpdate их можно менять в любом порядке (например, NearPlane и FarPlane). Renderer (#9) вызывает эти методы после onUpdate до GL-вызовов кадра, SceneSerializer (#25) — перед Save; правила проверки не дублируются.
- Ошибки GL проверяются минимум раз на кадр и при загрузке ресурсов; InvalidOperationException содержит операцию/код. Исключение onUpdate сохраняется исходным; using освобождает ресурсы. Исключения native refresh/resize/input перехватываются, закрывают окно и повторно выбрасываются после native loop, не выходят через GLFW. Ошибка очистки не заменяет первоначальную ошибку Run.

Единственный владелец GPU — Engine. Кэши по идентичности CPU-объекта живут до Dispose; CPU-объекты не IDisposable. Demo перехватывает исключение на верхнем уровне, пишет причину в stderr и завершает процесс с кодом 1. Собственные exception-классы, backend/DI-контейнер и публичный profiler не добавляются. Ограничения Wayland/macOS/Windows/HiDPI из spike сохраняются, их проверка этим контрактом не объявляется.

## 8. Нормали и простой свет (P0-10)

Это согласованный минимальный объем команды по ответу преподавателя; отдельная анимация и frustum culling необязательны. Правила относятся к реализации, а не к уже существующему коду движка.

- Normal — локальная нормаль поверхности, конечная и ненулевая. Конструктор MeshData сначала проверяет каждую нормаль на конечность и ненулевую длину (ошибка ArgumentException), затем копирует вершины и нормализует нормали в собственной неизменяемой копии. Позиции/UV конечные; вырожденные треугольники по-прежнему допускаются. Отсутствие нормали у вручную созданного mesh теперь ошибка аргумента; фабрики дают корректные нормали автоматически.
- Cube — отдельные вершины/UV/нормали граней; plane в XZ — нормаль +Y и согласованный winding. GPU-формат Position/UV/Normal согласован между #7/#8/#9.
- Цвета света — конечный RGB в диапазоне [0,1]. Direction — направление распространения лучей в мировых координатах, конечное и ненулевое; renderer проверяет и нормализует его перед вычислением освещения каждого кадра, в том числе после onUpdate. Нулевой цвет допустим (выключить составляющую), новой подсистемы источников не нужно.
- Нормаль преобразуется inverse-transpose линейной части model, затем нормализуется; нельзя просто умножить на model при неравномерном масштабе. Translation не участвует. Row-vector model = scale * rotation * translation, MVP = model * view * projection; данные System.Numerics передаются GLSL transpose=false, как в ShaderProgram.SetMatrix spike. На стороне GLSL normalMatrix = transpose(inverse(mat3(u_model))); view не участвует, свет и нормаль в world space.
- Необратимый transform (нулевой scale) отклоняется перед GL-вызовами кадра с понятной ошибкой состояния; отрицательный ненулевой scale не запрещается. Back-face culling в базовой версии можно оставить выключенным, как в текущем архитектурном контракте.
- RGB результата = baseColor.rgb * sampledTexture.rgb * (ambient + directionalColor * max(dot(worldNormal, -normalizedDirection),0)); без texture множитель (1,1,1). Шейдер ограничивает вывод диапазоном [0,1]. Материал непрозрачный, alpha не включает blending. Тени, specular, gamma/HDR/PBR и несколько источников не добавляются.
- Свет/transform/material менять только на потоке Run/onUpdate. После onUpdate и до любых GL-вызовов кадра невалидные изменяемые данные отклоняются с InvalidOperationException, а не передаются GL. Это дополняет существующие правила ошибок; не вводить новые exception-классы.

## 9. JSON собственной сцены (P0-11, #25)

Сериализация — CPU-код `Engine3D.Core`. `SceneSerializer` не зависит от `Engine`/OpenGL; клиент может передать `engine.LoadTexture` как делегат, но это не меняет граф зависимостей сборок.

- Save/Load синхронные, до Run или после его возврата, на потоке вызывающего приложения без одновременного изменения сцены. Нового Run, контекста, DI-контейнера и отдельной сборки нет.
- Конструктор TextureData требует положительные width/height и ровно width*height*4 байт RGBA8 (проверять размер без переполнения), копирует входной буфер; Pixels только для чтения, SourcePath при наличии приводится к полному пути. Неверные аргументы отклоняются ArgumentException. Это также закрывает создание CPU TextureData декодером #10 и тестовым загрузчиком без окна.
- SourcePath неизменяем, это полный путь исходного изображения. Engine.LoadTexture задает его при декодировании. TextureData, созданная без исходного файла, может использоваться renderer, но Save текстурированного материала без SourcePath сообщает NotSupportedException. Не создавать изменяемый дубликат пути в MaterialData.
- Фабрики хранят внутри MeshData неизменяемое описание происхождения: cube/size либо plane/width/depth. Это служебные CPU-данные Core, не новый публичный GPU API. Произвольный пользовательский mesh продолжает отображаться, но Save без описания примитива сообщает NotSupportedException; не пытаться угадать тип по геометрии.
- Transform и camera/light проверяются также до Save: числовые поля конечны, quaternion ненулевой: нормализуется при вычислении model matrix, при сохранении и при восстановлении; scale ненулевой, position != target, up ненулевой и не параллелен взгляду, 0<FOV<180 и 0<near<far. BaseColor — конечный RGBA [0,1], непрозрачный материал рендерится без blending. Не поддерживать неизвестные kind/version; отсутствующие обязательные поля, дубли mesh id или meshId без записи дают InvalidDataException при Load.
- Схема JSON version=1: meshes (уникальные id/kind/size либо width+depth); objects в порядке Scene.Objects (meshId, transform.position/rotation/scale, material.baseColor/texturePath); camera; lighting. Vector3/цвет — массивы чисел, Quaternion — [x,y,z,w]. Пиксели, вершины, GPU handles, delegates и состояния окна не записываются.
- Mesh ids задаются внутри документа, не глобальные идентификаторы: по ссылочной идентичности MeshData (ReferenceEquals), не по сравнению геометрии, при Save, по id при Load один новый экземпляр mesh на запись. Материалы восстанавливаются на объект; сохранение общей идентичности материалов не требуется. Текстуры кешируются внутри одного Load по полному разрешенному пути, не на каждый объект.
- texturePath — относительный путь от каталога JSON; Save вычисляет его из SourcePath, Load вычисляет полный путь от каталога JSON, независимо от текущего cwd. Переносить нужно JSON вместе с файлами по указанной относительной структуре. Пути с .. допустимы для собственной локальной сцены; формат не является sandbox для недоверенных сцен и не обещает переносимость Windows-путей в Linux.
- Load сначала полностью читает и валидирует DTO version/поля/типы/id/ссылки/числа/размеры/камеру/transform/light, затем создает CPU-сцену и вызывает textureLoader для реально используемых путей. Не сериализовать напрямую System.Numerics или граф доменных объектов; использовать простой внутренний DTO + System.Text.Json без новых пакетов.
- Для текстурированной сцены loader обязателен; null дает InvalidOperationException сразу после валидации DTO и до создания сцены/материалов или вызова любых загрузчиков. Core не вызывает Engine/OpenGL. Делегат получает полный путь; возвращает ненулевую TextureData с SourcePath, соответствующим этому полному пути, чтобы следующая Save работала; нарушение этого контракта дает InvalidOperationException. Engine.LoadTexture удовлетворяет контракту. Абсолютный SourcePath не записывается в JSON, а вычисляется заново при загрузке; ошибка загрузчика сохраняется с исходной причиной, итоговая SceneSnapshot не возвращается. Существующая сцена не изменяется; временные CPU-данные можно собрать GC. Нет обещания откатить побочные эффекты пользовательского делегата.
- Чтение файла/доступ — стандартные IOException/UnauthorizedAccessException. Неверный JSON или неподдерживаемая version/невалидные данные документа — InvalidDataException с путем и причиной (JsonException как InnerException при синтаксической ошибке). Неподдерживаемое описание/mesh при Save — NotSupportedException. Неверные аргументы Save/Load — ArgumentException (null — ArgumentNullException).
- Save сначала валидирует сцену и строит полный JSON в памяти, затем пишет файл. Если невалидна сцена — файл не изменяется. Отказ записи сообщает стандартную ошибку I/O; атомарное восстановление старого файла при I/O-отказе не обещается в P0. Такую гарантию нельзя добавлять в тесты.

### Схема version=1

```json
{
  "version": 1,
  "meshes": [{ "id": "m0", "kind": "cube", "size": 1.0 }],
  "objects": [
    {
      "meshId": "m0",
      "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
      "material": { "baseColor": [1,1,1,1], "texturePath": "../Assets/checker.png" }
    },
    {
      "meshId": "m0",
      "transform": { "position": [2,0,0], "rotation": [0,0,0,1], "scale": [1,1,1] },
      "material": { "baseColor": [1,0.5,0.5,1], "texturePath": null }
    }
  ],
  "camera": { "position": [3,2,5], "target": [0,0,0], "up": [0,1,0], "fovDegrees": 60, "nearPlane": 0.1, "farPlane": 100 },
  "lighting": { "ambientColor": [0.2,0.2,0.2], "directionalColor": [1,1,1], "direction": [-1,-1,-1] }
}
```

Для plane запись mesh имеет id/kind="plane"/width/depth вместо size. Все названные поля обязательны, texturePath может быть null. Размеры примитива положительные конечные; пустой список объектов допустим, но не заменяет демонстрацию P0-02. Общий m0 восстанавливается единожды и разделяется двумя объектами. При Load пути относятся к каталогу JSON; для Linux сравнение текстурных путей регистрозависимое. Unknown дополнительные свойства можно игнорировать, но версия, обязательные данные и ссылки валидируются. Save пишет только schema1, совместимость с неизвестными версиями не обещается.

## 10. Сценарии C/D и проверки

```csharp
using var engine = Engine.Create(new EngineOptions());
var mesh = PrimitiveFactory.CreateCube();
var scene = new Scene();
scene.Add(new SceneObject(mesh, new MaterialData()));
var camera = new Camera { Position = new Vector3(3f, 2f, 5f), Target = Vector3.Zero };
scene.Lighting.Direction = new Vector3(-1f, -1f, -1f);

Directory.CreateDirectory("Scenes"); // подготовка каталога — вызывающее приложение
SceneSerializer.Save("Scenes/demo.json", scene, camera);
var loaded = SceneSerializer.Load("Scenes/demo.json", engine.LoadTexture);
engine.Run(loaded.Scene, loaded.Camera, dt =>
{
    // Смена направления для C; D также проверить при фиксированном направлении.
    loaded.Scene.Lighting.Direction = new Vector3(-1f, -1f, -0.5f);
});
```

Эскиз показывает публичный путь, не реализованную функцию. Для проверки D выбрать статическую сцену с cube/plane, двумя объектами на общем mesh и текстурой; после загрузки сравнить transform, материалы/пути, камеру и свет. В C смена направления меняет яркость граней при корректных UV/depth.

CPU-тесты #12 проверяют нормали примитивов и их преобразование при неравномерном масштабе, round-trip в новую сцену, общую ссылку mesh, пути относительно JSON и кеширование декодирования внутри Load. Текстурный loader в CPU-тесте возвращает `new TextureData(...)`, без Engine. Битые ссылки, версия/kind, отсутствующие поля, невалидные camera/quaternion/light/scale, unsupported mesh/texture без SourcePath, null/ошибка loader имеют отдельные проверки. Численные допуски обосновать в тестах; побайтовое равенство GPU-кадров не обещается. Визуальная приемка C/D — #11/#13, финальный FPS со светом после загрузки и прогрева — #14.
