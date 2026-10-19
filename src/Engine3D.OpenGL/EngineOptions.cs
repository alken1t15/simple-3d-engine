namespace Engine3D.OpenGL;

/// <summary>Параметры окна, которое создает <see cref="Engine.Create"/>.</summary>
public sealed class EngineOptions
{
    /// <summary>Ширина клиентской области окна в логических пикселях; должна быть положительной.</summary>
    public int Width { get; init; } = 1280;

    /// <summary>Высота клиентской области окна в логических пикселях; должна быть положительной.</summary>
    public int Height { get; init; } = 720;

    /// <summary>Заголовок окна; не может быть пустым.</summary>
    public string Title { get; init; } = "Engine3D Demo";

    /// <summary>Синхронизация показа кадров с частотой экрана; для замера FPS обычно выключается.</summary>
    public bool VSync { get; init; } = true;

    /// <summary>
    /// Забирает ли окно фокус, когда <see cref="Engine.Run"/> делает его видимым. По умолчанию <c>true</c>.
    /// При <c>false</c> фокус остается у процесса, который запустил окно: удобно для ручного харнесса,
    /// отвечающего в терминале, пока сцена показана.
    /// </summary>
    public bool FocusOnShow { get; init; } = true;

    /// <summary>
    /// Начальное положение левого верхнего угла окна вместе с рамкой в экранных пунктах;
    /// <c>null</c> — положение выбирает ОС. Знаки допускаются.
    /// </summary>
    public (int X, int Y)? Position { get; init; }
}
