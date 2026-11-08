using System.Text.Json;
using Engine3D.Core;
using StbImageSharp;

namespace Engine3D.OpenGL;

/// <summary>Один встроенный CPU-atlas; не зависит от окна, cwd и установленных шрифтов.</summary>
internal sealed class BitmapFont
{
    public static BitmapFont Default { get; } = Load();

    public TextureData Texture { get; }
    public int CellWidth { get; }
    public int CellHeight { get; }
    public int Columns { get; }
    public float Advance { get; }
    private readonly UiRect[] inkBounds = new UiRect[95];

    private BitmapFont(TextureData texture, int cellWidth, int cellHeight, int columns, float advance)
    {
        Texture = texture;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = columns;
        Advance = advance;
        var pixels = texture.Pixels;
        for (int glyph = 0; glyph < inkBounds.Length; glyph++)
        {
            int left = cellWidth, top = cellHeight, right = 0, bottom = 0;
            int originX = glyph % columns * cellWidth, originY = glyph / columns * cellHeight;
            for (int y = 0; y < cellHeight; y++)
            for (int x = 0; x < cellWidth; x++)
            {
                if (pixels[((originY + y) * texture.Width + originX + x) * 4 + 3] == 0) continue;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
            inkBounds[glyph] = right > left ? new(left, top, right - left, bottom - top) : default;
        }
    }

    public UiRect InkBounds(int glyph) => inkBounds[glyph];

    public int GlyphIndex(int unicodeScalar) => (unicodeScalar is >= 32 and <= 126 ? unicodeScalar : '?') - 32;

    private static BitmapFont Load()
    {
        var assembly = typeof(BitmapFont).Assembly;
        using var png = assembly.GetManifestResourceStream("Engine3D.OpenGL.Assets.ascii-atlas.png")
            ?? throw new InvalidOperationException("Embedded ASCII atlas is missing.");
        using var json = assembly.GetManifestResourceStream("Engine3D.OpenGL.Assets.ascii-atlas.json")
            ?? throw new InvalidOperationException("Embedded ASCII metrics are missing.");
        using var metrics = JsonDocument.Parse(json);
        var root = metrics.RootElement;
        var image = ImageResult.FromStream(png, ColorComponents.RedGreenBlueAlpha);
        return new BitmapFont(new TextureData(image.Width, image.Height, image.Data),
            root.GetProperty("cellWidth").GetInt32(), root.GetProperty("cellHeight").GetInt32(),
            root.GetProperty("columns").GetInt32(), root.GetProperty("advance").GetSingle());
    }
}
