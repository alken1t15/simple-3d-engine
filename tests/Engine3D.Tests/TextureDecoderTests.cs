using Engine3D.Core;
using Engine3D.OpenGL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Engine3D.Tests;

[TestClass]
public sealed class TextureDecoderTests
{
    internal static string ControlPath => Path.Combine(AppContext.BaseDirectory, "Assets", "control.png");

    [TestMethod]
    public void Png_decodes_RGBA8_top_down_with_full_source_path()
    {
        var texture = TextureDecoder.Load(ControlPath);
        Assert.AreEqual(256, texture.Width);
        Assert.AreEqual(256, texture.Height);
        Assert.AreEqual(256 * 256 * 4, texture.Pixels.Length);
        Assert.AreEqual(Path.GetFullPath(ControlPath), texture.SourcePath);
        CollectionAssert.AreEqual(new byte[] { 0, 90, 255, 255 }, texture.Pixels[..4].ToArray()); // верх слева
        CollectionAssert.AreEqual(new byte[] { 255, 210, 0, 255 }, texture.Pixels.Slice(255 * 4, 4).ToArray());
        CollectionAssert.AreEqual(new byte[] { 230, 30, 30, 255 }, texture.Pixels.Slice(255 * 256 * 4, 4).ToArray());
    }

    [TestMethod]
    public void Relative_path_becomes_full_and_repeated_loads_are_independent_CPU_objects()
    {
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, ControlPath);
        var first = TextureDecoder.Load(relative);
        var second = TextureDecoder.Load(relative);
        Assert.AreEqual(Path.GetFullPath(ControlPath), first.SourcePath);
        Assert.AreNotSame(first, second); // GPU-кэш по TextureData, не по имени файла
        CollectionAssert.AreEqual(first.Pixels.ToArray(), second.Pixels.ToArray());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void Empty_paths_are_rejected(string? path) =>
        Assert.Throws<ArgumentException>(() => TextureDecoder.Load(path!));

    [TestMethod]
    public void Missing_file_reports_path()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png");
        var error = Assert.ThrowsExactly<FileNotFoundException>(() => TextureDecoder.Load(path));
        Assert.Contains(path, error.Message);
    }

    [TestMethod]
    public void Unreadable_directory_preserves_IO_or_access_error()
    {
        // Каталог нельзя прочитать как файл даже при запуске теста привилегированным пользователем.
        try
        {
            TextureDecoder.Load(Path.GetTempPath());
            Assert.Fail("Reading a directory must fail.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), exception.Message);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Unsupported_and_truncated_images_report_path_and_reason(bool pngSignature)
    {
        string path = Path.Combine(Path.GetTempPath(), $"texture-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, pngSignature ? [137, 80, 78, 71, 13, 10, 26, 10] : [1, 2, 3]);
            var error = Assert.ThrowsExactly<InvalidDataException>(() => TextureDecoder.Load(path));
            Assert.Contains(path, error.Message);
            Assert.Contains(pngSignature ? "Cannot decode PNG" : "invalid PNG signature", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void Upload_rows_are_reversed_once_without_changing_CPU_pixels(int height)
    {
        var input = Enumerable.Range(0, 2 * height * 4).Select(i => (byte)i).ToArray();
        var texture = new TextureData(2, height, input);
        var flipped = GpuTexture.BottomUpPixels(texture);
        for (int row = 0; row < height; row++)
            CollectionAssert.AreEqual(input.AsSpan(row * 8, 8).ToArray(), flipped.AsSpan((height - 1 - row) * 8, 8).ToArray());
        CollectionAssert.AreEqual(input, texture.Pixels.ToArray());
    }
}
