# Overlay: панели, изображения и ASCII-текст

Расширение для [задачи игры #17](https://github.com/alken1t15/simple-platformer/issues/17)
по [App/engine v1](https://github.com/alken1t15/simple-platformer/blob/main/docs/app-engine-contract-v1.md#overlay).
Реализованы универсальные данные и renderer. Игровые экраны, раскладка кнопок, hit-test,
выбор пункта, переходы и HUD-модель остаются в клиенте игры (#19).
Принятие upstream PR и обновление игрового gitlink — отдельные шаги интеграции.

## Публичный API

В `Engine3D.Core` находятся CPU-типы, без OpenTK, окна и GPU:

```csharp
public readonly record struct UiRect(float X, float Y, float Width, float Height);
public readonly record struct OverlayPanel(UiRect Bounds, ColorRgba Color);
public readonly record struct OverlayImage(UiRect Bounds, TextureData Texture, ColorRgba Tint);
public readonly record struct OverlayText(Vector2 Position, string Text, float PixelHeight, ColorRgba Color);
public sealed class OverlayFrame
{
    public OverlayFrame(IReadOnlyList<OverlayPanel> panels,
        IReadOnlyList<OverlayImage> images, IReadOnlyList<OverlayText> texts);
    public ReadOnlySpan<OverlayPanel> Panels { get; }
    public ReadOnlySpan<OverlayImage> Images { get; }
    public ReadOnlySpan<OverlayText> Texts { get; }
}
```

`Engine3D.OpenGL.Engine.SetOverlay(OverlayFrame? frame)` устанавливает последний Frame;
`null` очищает overlay. Метод не создаёт окно, не читает ввод и не загружает данные на GPU.
Он разрешён на потоке `Create` до `Run` и внутри `onUpdate`.
Другой поток, вызов во время `Run` вне `onUpdate` или после завершения `Run` дают
`InvalidOperationException`; после `Dispose` — `ObjectDisposedException`.
Сигнатура `Run(scene, camera, onUpdate)` сохранена.

Конструктор Frame сначала копирует все входные коллекции, затем проверяет свои копии.
Свойства возвращают `ReadOnlySpan`, mutable-массивы наружу не выдаются.
Строки и TextureData неизменяемы, изображения разделяются по ссылке.
Value types сами по себе не валидируют аргументы: проверка выполняется при создании Frame,
включая `default(OverlayImage)`/`default(OverlayText)`.

## Координаты, слои и валидация

- Единицы — логические пиксели client area, как `InputSnapshot.Pointer.Position` и
  Width/Height в `Engine.WindowBounds`. Начало слева сверху, X вправо, Y вниз.
- Width/Height прямоугольников конечны и неотрицательны; позиции конечны.
  PixelHeight конечен и строго положителен. RGBA/tint конечны и находятся в [0,1].
- Null-коллекция — `ArgumentNullException`. Невалидный элемент коллекции, null-строка
  или null-текстура — `ArgumentException` с именем коллекции и индексом элемента.
- Пустые коллекции, пустой текст, нулевые размеры и alpha=0 допустимы, ничего не рисуют.
  Отрицательные X/Y и элементы вне client area допустимы.
- Порядок: все panels → все images → все texts. Внутри каждого списка более поздний
  элемент рисуется поверх предыдущего. Команды по текстурам не пересортировываются.
- Вне экрана геометрия обрезается с сохранением соответствующих UV. Вложенного clipping,
  scroll-контейнеров, layout engine и автоматического переноса строк нет.

Renderer считает clipping в double, затем передаёт ограниченные clip coordinates на GPU:
`x_ndc = 2*x/logicalWidth - 1`, `y_ndc = 1 - 2*y/logicalHeight`.
Viewport получает physical framebuffer dimensions. Повторно умножать координаты клиента
на DPI не нужно. Resize не перераскладывает UI за приложение: клиент может построить
новый Frame по текущему WindowBounds в onUpdate. Системный Refresh рисует последний Frame
с актуальными размерами без повторного вызова onUpdate.
При нулевых размерах или фактическом iconified/minimized состоянии рисование пропускается;
после restore применяется тот же последний Frame. Проверяется реальное состояние GLFW,
поскольку свёрнутое X11-окно может сохранять ненулевой framebuffer.

## Цвет, изображения и текст

Цвета используют straight alpha. Для картинки выходной texel RGBA умножается на Tint RGBA;
для панели используется Color; glyph atlas содержит белый RGB и alpha coverage.
RGB смешивается как `src.rgb * src.a + dst.rgb * (1 - src.a)`, alpha —
`src.a + dst.a * (1 - src.a)`. Overlay не получает освещение сцены.
TextureData хранит строки сверху вниз; существующий GPU-upload переворачивает их один раз,
overlay задаёт UV соответственно. Изображение целиком растягивается в Bounds.

Встроен atlas DejaVu Sans Mono 2.37: печатный ASCII U+0020…U+007E.
Один неподдерживаемый Unicode scalar заменяется одним `?`, в том числе символ вне BMP.
Управляющие символы, переводы строк и табуляция также заменяются `?`.
Пробел не создаёт геометрию, но сдвигает позицию. Для нескольких строк передавайте несколько OverlayText.

Position задаёт левый верх прямоугольника строки, включая поля atlas. PixelHeight — высота
строки с полями, а не высота видимой части буквы. Исходный шрифт растеризован при 48 px,
ячейка 35×63, моноширинный advance 28.90625. Масштаб = PixelHeight/63; сдвиг на символ
= 28.90625×масштаб. Пробел имеет тот же advance. Kerning и автоматическое выравнивание не добавлены.
Встроенные PNG/метрики загружаются из assembly и не зависят от cwd или системных шрифтов.
Atlas имеет прозрачные поля, linear filtering без mipmaps. Произвольный размер текста допустим,
но качество сильного увеличения bitmap ограничено исходным растром.

Источник, SHA шрифта, версия Pillow и параметры —
[метрики](../src/Engine3D.OpenGL/Assets/ascii-atlas.json),
[лицензия](../src/Engine3D.OpenGL/Assets/FONT-LICENSE.txt), [THIRD_PARTY](../THIRD_PARTY.md).
Лицензия копируется в build/publish output вместе с библиотекой.
При необходимости пересоздать ассеты (Pillow нужен только для этой команды):

```sh
python3 scripts/generate_font_atlas.py /path/to/DejaVuSansMono.ttf
```

Для точного воспроизведения используйте исходный font SHA и Pillow из JSON.
Ни Python/Pillow, ни системный TTF не нужны при обычной .NET-сборке и запуске.

## Рендер и владение ресурсами

Общий путь: CPU-валидация 3D → 3D renderer → overlay → FrameRendered/readback → SwapBuffers.
Overlay не очищает color/depth buffer; на время прохода отключает depth test/write и
использует alpha blending. В `finally` восстанавливаются затронутые GL-состояния:
viewport, depth enable/write mask, blend enable/factors/equations, cull/scissor/stencil enable,
color write mask, program, VAO/array buffer, active texture/unit0 binding/sampler,
unpack buffer и изменённые pixel-store параметры. Depth function и scissor box не меняются.
Это действует также при первой загрузке ресурсов и ошибке upload.

Один динамический quad-buffer переиспользуется; изменение текста не создаёт новые texture.
Atlas и белая служебная texture загружаются лениво. Изображения кешируются по идентичности
TextureData в overlay-renderer до Engine.Dispose; `SetOverlay(null)` очищает отображение,
но не освобождает этот кеш. Одна TextureData в 3D и overlay может иметь отдельный upload
в каждом renderer. Повторное использование тех же изображений не растит overlay cache;
создавать новую TextureData каждый кадр не следует.

Engine освобождает shader, buffer, VAO, font/white/image textures до уничтожения контекста.
Частично созданные базовые ресурсы также очищаются; повторный Dispose безопасен.
Первоначальная ошибка Run не заменяется ошибкой cleanup. GPU handles остаются внутренними.

## Пример клиента

```csharp
using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;

using var engine = Engine.Create(new EngineOptions());
var scene = new Scene();
var camera = new Camera();
engine.SetOverlay(new OverlayFrame(
    [new(new UiRect(20, 20, 260, 100), new ColorRgba(0, 0, 0, 0.8f))],
    [],
    [new(new Vector2(32, 32), "Coins 3/10", 28, new ColorRgba(1, 1, 1))]));
engine.Run(scene, camera, _ =>
{
    if (engine.Input.WasPressed(InputKey.Space)) engine.SetOverlay(null);
    if (engine.Input.WasPressed(InputKey.Escape)) engine.RequestClose();
});
```

[OverlaySample](../samples/OverlaySample/Program.cs) показывает все примитивы поверх 3D,
pointer marker, clipping и размеры logical/framebuffer. [Проверки](overlay-testing.md).
