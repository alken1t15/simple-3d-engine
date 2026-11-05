using System.Text;
using Engine3D.Core;

namespace Engine3D.OpenGL;

// Позиции уже в clip space; UV соответствуют единственному flip TextureData → GPU.
internal readonly record struct OverlayQuad(float Left, float Top, float Right, float Bottom,
    float ULeft, float VTop, float URight, float VBottom, ColorRgba Color, TextureData? Texture);

/// <summary>CPU clipping и glyph layout. Double-промежуточные значения сохраняют finite-геометрию для float.MaxValue.</summary>
internal static class OverlayGeometry
{
    public static void Build(OverlayFrame frame, int width, int height, List<OverlayQuad> output)
    {
        output.Clear();
        if (width <= 0 || height <= 0) return;
        foreach (var panel in frame.Panels)
            Add(panel.Bounds.X, panel.Bounds.Y, panel.Bounds.Width, panel.Bounds.Height,
                0d, 1d, 1d, 0d, panel.Color, null, width, height, output);
        foreach (var image in frame.Images)
            Add(image.Bounds.X, image.Bounds.Y, image.Bounds.Width, image.Bounds.Height,
                0d, 1d, 1d, 0d, image.Tint, image.Texture, width, height, output);
        if (frame.Texts.IsEmpty) return;
        var font = BitmapFont.Default;
        foreach (var text in frame.Texts)
        {
            double scale = (double)text.PixelHeight / font.CellHeight;
            double x = text.Position.X;
            foreach (Rune rune in text.Text.EnumerateRunes())
            {
                if (x >= width) break;
                int glyph = font.GlyphIndex(rune.Value);
                double u = (double)(glyph % font.Columns * font.CellWidth) / font.Texture.Width;
                double v = (double)(glyph / font.Columns * font.CellHeight) / font.Texture.Height;
                if (rune.Value != ' ')
                    Add(x, text.Position.Y, font.CellWidth * scale, text.PixelHeight,
                        u, 1d - v, u + (double)font.CellWidth / font.Texture.Width,
                        1d - v - (double)font.CellHeight / font.Texture.Height,
                        text.Color, font.Texture, width, height, output);
                x += font.Advance * scale;
            }
        }
    }

    private static void Add(double x, double y, double w, double h, double u0, double v0, double u1, double v1,
        ColorRgba color, TextureData? texture, int width, int height, List<OverlayQuad> output)
    {
        if (w <= 0d || h <= 0d || color.A == 0f) return;
        double left = Math.Max(0d, x), top = Math.Max(0d, y);
        double right = Math.Min(width, x + w), bottom = Math.Min(height, y + h);
        if (right <= left || bottom <= top) return;
        double ul = u0 + (u1 - u0) * ((left - x) / w);
        double ur = u0 + (u1 - u0) * ((right - x) / w);
        double vt = v0 + (v1 - v0) * ((top - y) / h);
        double vb = v0 + (v1 - v0) * ((bottom - y) / h);
        output.Add(new OverlayQuad((float)(left / width * 2d - 1d), (float)(1d - top / height * 2d),
            (float)(right / width * 2d - 1d), (float)(1d - bottom / height * 2d),
            (float)ul, (float)vt, (float)ur, (float)vb, color, texture));
    }
}
