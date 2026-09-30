namespace OpenTkSpike.Tests;

public class ImageRowsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Flip_reverses_row_order(int height)
    {
        const int rowSize = 8;
        var pixels = CreateImageWithRowNumbers(rowSize, height);

        ImageRows.FlipInPlace(pixels, rowSize, height);

        for (int y = 0; y < height; y++)
            Assert.All(pixels.Skip(y * rowSize).Take(rowSize), b => Assert.Equal((byte)(height - 1 - y), b));
    }

    [Fact]
    public void Double_flip_restores_original()
    {
        const int rowSize = 12;
        const int height = 5;
        var original = CreateImageWithRowNumbers(rowSize, height);
        var pixels = (byte[])original.Clone();

        ImageRows.FlipInPlace(pixels, rowSize, height);
        ImageRows.FlipInPlace(pixels, rowSize, height);

        Assert.Equal(original, pixels);
    }

    private static byte[] CreateImageWithRowNumbers(int rowSize, int height)
    {
        var pixels = new byte[rowSize * height];
        for (int y = 0; y < height; y++)
            Array.Fill(pixels, (byte)y, y * rowSize, rowSize);
        return pixels;
    }
}
