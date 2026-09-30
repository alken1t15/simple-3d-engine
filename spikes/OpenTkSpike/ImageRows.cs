namespace OpenTkSpike;

internal static class ImageRows
{
    // Переворачивает изображение по вертикали на месте: OpenGL хранит строки снизу вверх, PNG — сверху вниз.
    public static void FlipInPlace(byte[] pixels, int rowSize, int height)
    {
        var temp = new byte[rowSize];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            var topRow = pixels.AsSpan(top * rowSize, rowSize);
            var bottomRow = pixels.AsSpan(bottom * rowSize, rowSize);
            topRow.CopyTo(temp);
            bottomRow.CopyTo(topRow);
            temp.CopyTo(bottomRow);
        }
    }
}
