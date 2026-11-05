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

    private BitmapFont(TextureData texture, int cellWidth, int cellHeight, int columns, float advance)
    {
        Texture = texture;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = columns;
        Advance = advance;
    }

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
