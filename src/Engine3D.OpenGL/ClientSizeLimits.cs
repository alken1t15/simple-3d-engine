namespace Engine3D.OpenGL;

/// <summary>Эффективные границы клиентской области в логических пикселях.
/// int.MaxValue означает отсутствие верхней границы.</summary>
public readonly record struct ClientSizeLimits(
    int MinimumWidth, int MinimumHeight, int MaximumWidth, int MaximumHeight);

internal static class WindowSizePolicy
{
    internal static ClientSizeLimits Calculate((int Width, int Height)? minimum,
        int availableWidth, int availableHeight)
    {
        int maxWidth = Math.Max(1, availableWidth), maxHeight = Math.Max(1, availableHeight);
        return new(Math.Min(minimum?.Width ?? 1, maxWidth), Math.Min(minimum?.Height ?? 1, maxHeight),
            maxWidth, maxHeight);
    }

    internal static (int Width, int Height) Clamp(ClientSizeLimits limits, int width, int height) =>
        (Math.Clamp(width, limits.MinimumWidth, limits.MaximumWidth),
         Math.Clamp(height, limits.MinimumHeight, limits.MaximumHeight));
}
