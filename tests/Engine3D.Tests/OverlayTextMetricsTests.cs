using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class OverlayTextMetricsTests
{
    [TestMethod]
    public void BoundsExcludeActualAtlasPaddingAndIncludeDescenders()
    {
        Assert.AreEqual(new UiRect(3, 13, 28, 35), OverlayTextMetrics.MeasureInkBounds("A", 63));
        Assert.AreEqual(new UiRect(5, 21, 23, 37), OverlayTextMetrics.MeasureInkBounds("g", 63));
        var play = OverlayTextMetrics.MeasureInkBounds("Play", 63);
        Assert.AreEqual(12, play.Y);
        Assert.AreEqual(46, play.Height);
        var half = OverlayTextMetrics.MeasureInkBounds("Play", 31.5f);
        Assert.AreEqual(play.X / 2, half.X);
        Assert.AreEqual(play.Width / 2, half.Width);
        Assert.AreEqual(play.Y / 2, half.Y);
        Assert.AreEqual(play.Height / 2, half.Height);
    }

    [TestMethod]
    public void SpacesAndUnicodeFollowRendererScalarAdvances()
    {
        Assert.AreEqual(default(UiRect), OverlayTextMetrics.MeasureInkBounds("   ", 63));
        Assert.AreEqual(default(UiRect), OverlayTextMetrics.MeasureInkBounds("", 63));
        Assert.AreEqual(OverlayTextMetrics.MeasureInkBounds("?", 63), OverlayTextMetrics.MeasureInkBounds("😀", 63));
        var plain = OverlayTextMetrics.MeasureInkBounds("A", 63);
        var spaced = OverlayTextMetrics.MeasureInkBounds(" A ", 63);
        Assert.AreEqual(plain.X + 28.90625f, spaced.X);
        Assert.AreEqual(plain.Width, spaced.Width);
    }

    [TestMethod]
    public void InvalidHeightAndOverflowAreRejectedWithoutAWindow()
    {
        foreach (float height in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OverlayTextMetrics.MeasureInkBounds("A", height));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => OverlayTextMetrics.MeasureInkBounds(new string('A', 100), float.MaxValue));
        Assert.ThrowsExactly<ArgumentNullException>(() => OverlayTextMetrics.MeasureInkBounds(null!, 20));
    }
}
