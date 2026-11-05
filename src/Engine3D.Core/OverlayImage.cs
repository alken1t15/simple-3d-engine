namespace Engine3D.Core;

/// <summary>Полное изображение, растянутое в Bounds; RGBA умножается на Tint.</summary>
public readonly record struct OverlayImage(UiRect Bounds, TextureData Texture, ColorRgba Tint);
