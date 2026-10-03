using System.Globalization;
using StbImageSharp;

namespace OpenTkSpike;

// Режим сравнения двух PNG для автоматических проверок (CI):
//   dotnet run -- --compare a.png b.png [--max-diff-percent P] [--min-diff-percent P]
// Печатает долю отличающихся пикселей; код возврата 1, если доля вне заданных границ.
internal static class ImageCompare
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
            throw new ArgumentException("Usage: --compare a.png b.png [--max-diff-percent P] [--min-diff-percent P]");

        double? maxPercent = null;
        double? minPercent = null;
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            double value = double.Parse(args[i + 1], CultureInfo.InvariantCulture);
            switch (args[i])
            {
                case "--max-diff-percent":
                    maxPercent = value;
                    break;
                case "--min-diff-percent":
                    minPercent = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        var first = Load(args[0]);
        var second = Load(args[1]);
        if (first.Width != second.Width || first.Height != second.Height)
            throw new InvalidOperationException(
                $"Image sizes differ: {first.Width}x{first.Height} vs {second.Width}x{second.Height}.");

        int total = first.Width * first.Height;
        int differing = 0;
        for (int offset = 0; offset < first.Data.Length; offset += 4)
        {
            if (!first.Data.AsSpan(offset, 4).SequenceEqual(second.Data.AsSpan(offset, 4)))
                differing++;
        }

        double percent = 100.0 * differing / total;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Differing pixels: {differing} of {total} ({percent:0.####}%)"));

        bool tooMany = percent > maxPercent;
        bool tooFew = percent < minPercent;
        if (tooMany)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAIL: more than {maxPercent}% differ."));
        if (tooFew)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"FAIL: less than {minPercent}% differ."));

        return tooMany || tooFew ? 1 : 0;
    }

    private static ImageResult Load(string path)
    {
        using var stream = File.OpenRead(path);
        return ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
    }
}
