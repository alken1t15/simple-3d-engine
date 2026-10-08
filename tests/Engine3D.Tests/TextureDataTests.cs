using Engine3D.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class TextureDataTests
{
    [TestMethod]
    public void Copies_pixels_and_keeps_size()
    {
        byte[] rgba = [1, 2, 3, 4, 5, 6, 7, 8];
        var texture = new TextureData(2, 1, rgba);

        rgba[0] = 99;

        Assert.AreEqual(2, texture.Width);
        Assert.AreEqual(1, texture.Height);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, texture.Pixels.ToArray());
        Assert.IsNull(texture.SourcePath);
    }

    [TestMethod]
    public void Source_path_becomes_full_path()
    {
        var texture = new TextureData(1, 1, new byte[4], "Assets/checker.png");

        Assert.IsTrue(Path.IsPathFullyQualified(texture.SourcePath!));
        Assert.AreEqual(Path.GetFullPath("Assets/checker.png"), texture.SourcePath);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    [DataRow(-1, 1)]
    [DataRow(1, -5)]
    public void Rejects_non_positive_size(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => new TextureData(width, height, new byte[4]));
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(5)]
    [DataRow(0)]
    public void Rejects_buffer_of_wrong_length(int length)
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new TextureData(1, 1, new byte[length]));

        Assert.Contains("Expected 4 bytes", exception.Message);
    }

    [TestMethod]
    public void Huge_size_is_rejected_without_overflow()
    {
        // Регрессия ревью #7: int.MaxValue * int.MaxValue * 4 = 2^64 − 2^34 + 4 не помещается в long,
        // раньше в сообщении было отрицательное число байт.
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new TextureData(int.MaxValue, int.MaxValue, new byte[4]));

        Assert.Contains("Expected 18446744056529682436 bytes", exception.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Rejects_blank_source_path(string path)
    {
        Assert.Throws<ArgumentException>(() => new TextureData(1, 1, new byte[4], path));
    }
}
