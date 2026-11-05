using System.Numerics;
using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class OverlayFrameTests
{
    private static readonly ColorRgba White = new(1f, 1f, 1f);
    private static readonly TextureData Texture = new(1, 1, [255, 255, 255, 255]);

    [TestMethod]
    public void Frame_owns_copies_preserves_order_and_shares_immutable_texture()
    {
        OverlayPanel[] panels = [new(new UiRect(1, 2, 3, 4), White), new(new UiRect(5, 6, 7, 8), White)];
        OverlayImage[] images = [new(new UiRect(2, 3, 4, 5), Texture, White)];
        OverlayText[] texts = [new(new Vector2(10, 20), "Coins 3/10", 24, White)];
        var frame = new OverlayFrame(panels, images, texts);
        panels[0] = default;
        images[0] = default;
        texts[0] = default;
        Assert.AreEqual(1f, frame.Panels[0].Bounds.X);
        Assert.AreEqual(5f, frame.Panels[1].Bounds.X);
        Assert.AreSame(Texture, frame.Images[0].Texture);
        Assert.AreEqual("Coins 3/10", frame.Texts[0].Text);
        _ = new OverlayFrame([], [], []);
        Assert.AreEqual("Coins 3/10", frame.Texts[0].Text);
    }

    [TestMethod]
    public void Empty_zero_area_transparent_and_offscreen_elements_are_valid()
    {
        var empty = new OverlayFrame([], [], []);
        Assert.IsTrue(empty.Panels.IsEmpty && empty.Images.IsEmpty && empty.Texts.IsEmpty);
        var frame = new OverlayFrame([default, new(new UiRect(-100, -200, 0, 10), White)],
            [new(new UiRect(float.MaxValue, -float.MaxValue, float.MaxValue, 0), Texture, default)],
            [new(new Vector2(-1, -2), "", float.Epsilon, White)]);
        Assert.AreEqual(2, frame.Panels.Length);
    }

    [TestMethod]
    public void Null_collections_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new OverlayFrame(null!, [], []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new OverlayFrame([], null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new OverlayFrame([], [], null!));
    }

    [TestMethod]
    public void Invalid_rectangles_are_rejected_in_both_panel_and_image_layers()
    {
        UiRect[] invalid = [new(0, 0, -1, 1), new(0, 0, 1, -1), new(float.NaN, 0, 1, 1),
            new(0, float.PositiveInfinity, 1, 1), new(0, 0, float.NegativeInfinity, 1), new(0, 0, 1, float.NaN)];
        foreach (var rect in invalid)
        {
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([new(rect, White)], [], []));
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [new(rect, Texture, White)], []));
        }
    }

    [TestMethod]
    public void Invalid_colors_are_rejected_in_every_layer()
    {
        ColorRgba[] invalid = [new(-0.1f, 0, 0), new(0, 1.1f, 0), new(0, 0, float.NaN),
            new(0, 0, 0, float.PositiveInfinity), new(0, 0, 0, -1), new(0, 0, 0, 1.01f)];
        foreach (var color in invalid)
        {
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([new(new UiRect(), color)], [], []));
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [new(new UiRect(), Texture, color)], []));
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [], [new(Vector2.Zero, "x", 20, color)]));
        }
    }

    [TestMethod]
    public void Default_images_and_texts_and_invalid_text_metrics_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [default], []));
        Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [], [default]));
        Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [], [new(Vector2.Zero, null!, 20, White)]));
        foreach (float height in new[] { 0, -1f, float.NaN, float.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [], [new(Vector2.Zero, "x", height, White)]));
        foreach (var position in new[] { new Vector2(float.NaN, 0), new Vector2(0, float.NegativeInfinity) })
            Assert.ThrowsExactly<ArgumentException>(() => new OverlayFrame([], [], [new(position, "x", 20, White)]));
    }
}
