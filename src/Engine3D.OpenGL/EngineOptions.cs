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
    /// Начальное положение клиентской области окна в экранных пунктах от левого верхнего угла; <c>null</c> —
    /// положение выбирает ОС. Знаки допускаются. Фиксированное положение помогает, когда окна открываются
    /// подряд: без него ОС каждый раз выбирает место сама.
    /// </summary>
    public (int X, int Y)? Position { get; init; }
}
