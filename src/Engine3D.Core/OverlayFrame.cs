namespace Engine3D.Core;

/// <summary>
/// Неизменяемые данные overlay без окна/GPU. Слои: panels, images, texts; внутри каждого слоя — порядок списка.
/// Входные коллекции копируются; immutable TextureData разделяется по ссылке.
/// </summary>
public sealed class OverlayFrame
{
    private readonly OverlayPanel[] _panels;
    private readonly OverlayImage[] _images;
    private readonly OverlayText[] _texts;

    /// <exception cref="ArgumentNullException">Одна из коллекций равна null.</exception>
    /// <exception cref="ArgumentException">
    /// Элемент содержит неконечные координаты/размеры, отрицательные размеры, неверный RGBA,
    /// null-текстуру/строку или неположительную высоту строки. Выход за экран допустим.
    /// </exception>
    public OverlayFrame(IReadOnlyList<OverlayPanel> panels, IReadOnlyList<OverlayImage> images,
        IReadOnlyList<OverlayText> texts)
    {
        ArgumentNullException.ThrowIfNull(panels);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(texts);
        _panels = panels.ToArray();
        _images = images.ToArray();
        _texts = texts.ToArray();

        for (int i = 0; i < _panels.Length; i++)
            if (!Valid(_panels[i].Bounds) || !_panels[i].Color.IsValid)
                throw new ArgumentException($"Overlay panel {i} has invalid bounds or color.", nameof(panels));
        for (int i = 0; i < _images.Length; i++)
            if (!Valid(_images[i].Bounds) || _images[i].Texture is null || !_images[i].Tint.IsValid)
                throw new ArgumentException($"Overlay image {i} has invalid bounds, texture or tint.", nameof(images));
        for (int i = 0; i < _texts.Length; i++)
        {
            var text = _texts[i];
            if (!float.IsFinite(text.Position.X) || !float.IsFinite(text.Position.Y) || text.Text is null
                || !float.IsFinite(text.PixelHeight) || text.PixelHeight <= 0f || !text.Color.IsValid)
                throw new ArgumentException($"Overlay text {i} has invalid position, text, height or color.", nameof(texts));
        }
    }

    /// <summary>Панели в порядке рисования, только для чтения.</summary>
    public ReadOnlySpan<OverlayPanel> Panels => _panels;

    /// <summary>Изображения в порядке рисования, только для чтения.</summary>
    public ReadOnlySpan<OverlayImage> Images => _images;

    /// <summary>Строки в порядке рисования, только для чтения.</summary>
    public ReadOnlySpan<OverlayText> Texts => _texts;

    private static bool Valid(UiRect bounds) => float.IsFinite(bounds.X) && float.IsFinite(bounds.Y)
        && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height) && bounds.Width >= 0f && bounds.Height >= 0f;
}
