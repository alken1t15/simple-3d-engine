using System.Text;
using Engine3D.Core;

namespace Engine3D.OpenGL;

/// <summary>CPU-измерение видимых пикселей встроенного шрифта; окно и OpenGL-контекст не нужны.</summary>
public static class OverlayTextMetrics
{
    /// <summary>Границы непрозрачных пикселей относительно OverlayText.Position, в logical client pixels.
    /// Поля atlas и внешние пробелы не входят в границы. Неподдерживаемый scalar заменяется '?'.
    /// Для пустой строки/пробелов возвращает нулевой прямоугольник.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Высота неположительна/неfinite либо результат не представим float.</exception>
    public static UiRect MeasureInkBounds(string text, float pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(pixelHeight) || pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        var font = BitmapFont.Default;
        double x = 0, left = double.PositiveInfinity, top = double.PositiveInfinity, right = 0, bottom = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var bounds = font.InkBounds(font.GlyphIndex(rune.Value));
            if (bounds.Width > 0)
            {
                left = Math.Min(left, x + bounds.X); top = Math.Min(top, bounds.Y);
                right = Math.Max(right, x + bounds.X + bounds.Width); bottom = Math.Max(bottom, bounds.Y + bounds.Height);
            }
            x += font.Advance;
        }
        if (double.IsPositiveInfinity(left)) return default;
        double scale = pixelHeight / (double)font.CellHeight;
        var result = new UiRect((float)(left * scale), (float)(top * scale),
            (float)((right - left) * scale), (float)((bottom - top) * scale));
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Width) || !float.IsFinite(result.Height))
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Границы строки не представимы в float");
        return result;
    }
}
