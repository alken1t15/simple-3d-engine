# Публичный API: проект контракта P0

**Статус:** проектные C# сигнатуры для планирования, 27.09.2026. Это **не реализованный код**. Имена и типы уточняются в задаче 1.3 после OpenTK spike 1.2; функциональные сценарии и правила владения должны сохраниться. [Требования](requirements.md) · [Архитектура](architecture.md) · [Основной план](../project_plan.md).

## 1. Что должен уметь вызывающий код

1. Создать окно/движок одним вызовом и получить понятную ошибку при невозможности создать контекст.
2. Получить процедурный куб/плоскость или передать собственный индексный mesh с позицией и UV.
3. Загрузить одну растровую текстуру, создать базовый материал и добавить несколько объектов в сцену.
4. Задать камеру и в каждом кадре менять `Transform` объекта без обращения к OpenGL.
5. Показать сцену, запросить закрытие, получить число показанных кадров для внешнего замера, освободить графические ресурсы через `using`.

Сцена не включает игровой мир: в API нет коллизий, физики, поведения объектов, переходов анимации и правил игры. `onUpdate` — callback вызывающего приложения; он меняет данные сцены, а рендерер только отображает их.

## 2. Предлагаемые типы и сигнатуры

Следующий блок — **эскиз контракта**, его не нужно вставлять в проект без проверки OpenTK lifecycle, namespace и компиляции:

```csharp
// Engine3D.Core
public readonly record struct Vertex(Vector3 Position, Vector2 Uv);

public sealed class MeshData
{
    public MeshData(IReadOnlyList<Vertex> vertices, IReadOnlyList<uint> indices);
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
    public int Width { get; }
    public int Height { get; }
    // RGBA8; данные после создания не меняются.
}

public readonly record struct ColorRgba(float R, float G, float B, float A = 1f);

public sealed class MaterialData
{
    public ColorRgba BaseColor { get; set; } = new(1f, 1f, 1f, 1f);
    public TextureData? Texture { get; set; }
}

public sealed class Transform
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
}

public sealed class SceneObject
{
    public SceneObject(MeshData mesh, MaterialData material);
    public MeshData Mesh { get; }
    public MaterialData Material { get; set; }
    public Transform Transform { get; } = new();
}

public sealed class Scene
{
    public IReadOnlyList<SceneObject> Objects { get; }
    public void Add(SceneObject item);
    public bool Remove(SceneObject item);
}

public sealed class Camera
{
    public Vector3 Position { get; set; }
    public Vector3 Target { get; set; }
    public Vector3 Up { get; set; } = Vector3.UnitY;
    public float FieldOfViewDegrees { get; set; } = 60f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 100f;
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
| `LoadTexture` | Для P0 вызывается после `Create` и до `Run`; декодирует изображение в CPU-данные. Выбор декодера — часть spike/реализации. |
| `Run` | Блокирует вызывающий поток до закрытия окна или `RequestClose`; обрабатывает события, вызывает `onUpdate`, рендерит и показывает кадры. Для P0 один `Run` на один `Engine`. |
| `RenderedFrames` | Обнуляется перед `Run`, увеличивается после показанного кадра и доступен после возврата `Run`. Не равен числу вызовов `onUpdate`. |
| `RequestClose` | Просит завершить цикл при ближайшей обработке события; не освобождает ресурсы немедленно. |
| `Dispose` | Освобождает GPU-кэш, шейдеры, окно и контекст. Повторный вызов безопасен. `Scene`, `MeshData`, `TextureData` не вызывают `Dispose`. |

**Владелец GPU-ресурсов — `Engine`.** Публичный `MeshData` и `TextureData` содержат только управляемые CPU-данные. Одни и те же данные можно повторно использовать для 1000 объектов. Приложение отвечает за время жизни единственного `Engine` через `using`; ручной `Dispose` каждого mesh/texture в P0 не нужен. Цена этого решения — GPU-кэш хранит ранее использованные ресурсы до закрытия движка; она приемлема для коротких демонстрационных сцен и должна быть проверена нагрузкой.

## 4. Сценарий A — текстурированный куб

Эскиз использования. Имена в коде окончательно проверить при задаче 1.3; смысл вызовов не зависит от конкретного названия декодера изображения.

```csharp
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
using var engine = Engine.Create(new EngineOptions { VSync = false });
var sharedMesh = PrimitiveFactory.CreateCube();
var sharedMaterial = new MaterialData();
var scene = new Scene();

for (int i = 0; i < 1000; i++)
{
    var item = new SceneObject(sharedMesh, sharedMaterial);
    item.Transform.Position = PositionForGridIndex(i); // функция демо, не движка
    scene.Add(item);
}

var camera = CreateStressCamera(); // функция демо, не движка
var timer = Stopwatch.StartNew();
engine.Run(scene, camera, deltaSeconds =>
{
    if (timer.Elapsed >= TimeSpan.FromSeconds(70))
        engine.RequestClose();
});

long frames = engine.RenderedFrames;
// Демо сохраняет frames, длительность, число объектов, VSync и параметры ПК.
```

В нагрузочном протоколе первые 10 секунд — прогрев, следующие 60 секунд — замер. Чтобы считать **только кадры периода замера**, демо запоминает `RenderedFrames` после прогрева и вычитает из финального значения. Измерять количество `onUpdate` вместо показанных кадров нельзя. Для подробного анализа просадок демо позднее может записывать покадровые timestamps; публичная система профилирования в P0 не требуется.

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

Семейство `EngineException`, `TryLoadTexture`, пользовательский logger и другие расширения из черновика сокомандника **не требуются P0**. Ошибки должны быть понятными и проверяемыми, но не создают отдельной большой подсистемы.

## 7. Решения и технические проверки

**Выбрано в плане:** `Engine.Create` + один `Run` + `Dispose`; GPU-ресурсами владеет `Engine`; CPU-данные mesh/texture неизменяемы; `Scene` не владеет GPU; callback исполняет внешнюю логику; `RenderedFrames` дает минимальную поддержку замера.

**Проверить в spike 1.2:** точное создание OpenTK `GameWindow`, возможность загрузить текстуру до `Run`, порядок callbacks и закрытия, ориентацию PNG/UV, систему координат и передачу `System.Numerics.Matrix4x4` в GLSL. Если проверка заставит изменить сигнатуру `Engine.Run` или жизненный цикл, обновить [архитектуру](architecture.md), этот API и пример демо до разделения задач между разработчиками.
