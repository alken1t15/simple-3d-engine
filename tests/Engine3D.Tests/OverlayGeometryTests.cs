using System.Numerics;
using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class OverlayGeometryTests
{
    private static readonly ColorRgba White = new(1, 1, 1);

    [TestMethod]
    public void Screen_clipping_preserves_image_uv_and_top_left_coordinates()
    {
        var texture = new TextureData(1, 1, [255, 255, 255, 255]);
        var frame = new OverlayFrame([], [new(new UiRect(-50, -25, 100, 50), texture, White)], []);
        List<OverlayQuad> quads = [];
        OverlayGeometry.Build(frame, 100, 100, quads);
        Assert.HasCount(1, quads);
        var q = quads[0];
        Assert.AreEqual(-1f, q.Left);
        Assert.AreEqual(1f, q.Top);
        Assert.AreEqual(0f, q.Right);
        Assert.AreEqual(0.5f, q.Bottom);
        Assert.AreEqual(0.5f, q.ULeft);
        Assert.AreEqual(0.5f, q.VTop);
        Assert.AreEqual(1f, q.URight);
        Assert.AreEqual(0f, q.VBottom);
    }

    [TestMethod]
    public void Layers_and_painter_order_are_preserved()
    {
        var red = new ColorRgba(1, 0, 0);
        var blue = new ColorRgba(0, 0, 1);
        var image = new TextureData(1, 1, [255, 255, 255, 255]);
        var frame = new OverlayFrame([new(new UiRect(0, 0, 20, 20), red), new(new UiRect(0, 0, 20, 20), blue)],
            [new(new UiRect(0, 0, 20, 20), image, White)], [new(Vector2.Zero, "A", 20, White)]);
        List<OverlayQuad> quads = [];
        OverlayGeometry.Build(frame, 100, 100, quads);
        Assert.HasCount(4, quads);
        Assert.AreEqual(red, quads[0].Color);
        Assert.AreEqual(blue, quads[1].Color);
        Assert.AreSame(image, quads[2].Texture);
        Assert.AreSame(BitmapFont.Default.Texture, quads[3].Texture);
    }

    [TestMethod]
    public void Unsupported_unicode_scalar_is_one_question_mark_and_space_advances()
    {
        List<OverlayQuad> fallback = [], question = [], spaced = [];
        OverlayFrame Text(string text) => new([], [], [new(Vector2.Zero, text, 63, White)]);
        OverlayGeometry.Build(Text("😀"), 400, 300, fallback);
        OverlayGeometry.Build(Text("?"), 400, 300, question);
        OverlayGeometry.Build(Text(" A"), 400, 300, spaced);
        CollectionAssert.AreEqual(question, fallback);
        Assert.HasCount(1, spaced);
        Assert.AreEqual((float)(BitmapFont.Default.Advance / 400d * 2d - 1d), spaced[0].Left);
    }

    [TestMethod]
    public void Embedded_atlas_covers_printable_ascii_and_has_transparent_padding()
    {
        var font = BitmapFont.Default;
        Assert.AreEqual(95, font.GlyphIndex(126) + 1);
        Assert.AreEqual(font.GlyphIndex('?'), font.GlyphIndex('\n'));
        Assert.IsTrue(font.Advance > 0 && font.Advance < font.CellWidth);
        Assert.AreEqual(0, font.Texture.Pixels[3]);
        for (int code = 33; code <= 126; code++)
        {
            int glyph = font.GlyphIndex(code), coverage = 0;
            for (int y = 0; y < font.CellHeight; y++)
                for (int x = 0; x < font.CellWidth; x++)
                    coverage += font.Texture.Pixels[((glyph / font.Columns * font.CellHeight + y) * font.Texture.Width
                        + glyph % font.Columns * font.CellWidth + x) * 4 + 3];
            Assert.IsGreaterThan(0, coverage, $"Missing ASCII glyph {code}.");
        }
    }

    [TestMethod]
    public void Extreme_finite_bounds_are_clipped_without_infinite_gpu_coordinates()
    {
        var frame = new OverlayFrame([new(new UiRect(-float.MaxValue / 2, 0, float.MaxValue, float.MaxValue), White)], [],
            [new(Vector2.Zero, "Big", float.MaxValue, White)]);
        List<OverlayQuad> quads = [];
        OverlayGeometry.Build(frame, 800, 600, quads);
        Assert.IsGreaterThanOrEqualTo(1, quads.Count);
        foreach (var q in quads)
            foreach (float value in new[] { q.Left, q.Top, q.Right, q.Bottom, q.ULeft, q.URight, q.VTop, q.VBottom })
                Assert.IsTrue(float.IsFinite(value), $"Non-finite quad {q}");
        Assert.AreEqual(-1f, quads[0].Left);
        Assert.AreEqual(1f, quads[0].Right);
    }

    [TestMethod]
    public void Empty_offscreen_transparent_and_zero_surface_produce_no_geometry()
    {
        List<OverlayQuad> quads = [];
        var frame = new OverlayFrame([new(new UiRect(100, 100, 50, 50), White), default,
            new(new UiRect(0, 0, 10, 10), new ColorRgba(1, 1, 1, 0))], [], [new(Vector2.Zero, "", 20, White)]);
        OverlayGeometry.Build(frame, 100, 100, quads);
        Assert.HasCount(0, quads);
        OverlayGeometry.Build(frame, 0, 100, quads);
        Assert.HasCount(0, quads);
    }
}
