using System.Numerics;

namespace XylemUI.Imaging;

/// <summary>
/// Shrinks premultiplied BGRA pixels to a smaller size by the mean over each target pixel's area (a box filter, design §5.0.4,
/// "Decoding size", IM1's Grill, Q18): each target pixel is the source pixels its square covers, each weighted by the part of it
/// covered. Flutter shrinks on the CPU with bilinear sampling and no mipmaps, which shows patterns when it shrinks much; Avalonia
/// shrinks with linear sampling and mipmaps.
/// </summary>
/// <remarks>
/// A target pixel spans at least one source pixel on each axis, so a source pixel falls in one target pixel or across the edge of
/// two. Each source pixel is read once: its column's target and its weight toward it (the rest goes to the next) are found before,
/// each source row is reduced to the target's width once, and its sums go to the one or two target rows it falls in. A pixel's four
/// channels are summed together as a <see cref="Vector4"/>. The pixels are premultiplied, so the mean of the channels is the mean of
/// the colors. In IM1 1b-2 a first version that summed each target pixel's span on its own, reducing a source row shared by two
/// target rows twice, took 30 ms to shrink a 1,512 x 1,134 image to 1,024 x 768 (docs/benchmarks/im1-element.md).
/// </remarks>
internal static class BoxResize
{
    /// <summary>
    /// Shrinks <paramref name="source"/> to <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The source pixels, rows of <paramref name="sourceWidth"/> × 4 bytes.</param>
    /// <param name="sourceWidth">The source's width.</param>
    /// <param name="sourceHeight">The source's height.</param>
    /// <param name="destination">The target pixels, rows of <paramref name="width"/> × 4 bytes.</param>
    /// <param name="width">The target's width, from 1 to <paramref name="sourceWidth"/>.</param>
    /// <param name="height">The target's height, from 1 to <paramref name="sourceHeight"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A target side is not from 1 to the source's.</exception>
    /// <exception cref="ArgumentException">A span is shorter than its pixels.</exception>
    public static void Shrink(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, Span<byte> destination, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, sourceWidth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, sourceHeight);
        if (source.Length < (long)sourceWidth * sourceHeight * 4 || destination.Length < (long)width * height * 4)
        {
            throw new ArgumentException("A span is shorter than its pixels.");
        }

        Split(sourceWidth, width, out int[] columns, out float[] columnWeights);
        Split(sourceHeight, height, out int[] rows, out float[] rowWeights);
        var reduced = new Vector4[width + 1];
        var current = new Vector4[width + 1];
        var next = new Vector4[width + 1];
        float scale = (float)((double)width * height / ((double)sourceWidth * sourceHeight));
        int target = 0;
        for (int sy = 0; sy < sourceHeight; sy++)
        {
            int ty = rows[sy];
            if (ty != target)
            {
                // The row before is done: no source row falls in it any more.
                Emit(current, destination.Slice(target * width * 4, width * 4), scale);
                (current, next) = (next, current);
                Array.Clear(next);
                target = ty;
            }

            Reduce(source.Slice(sy * sourceWidth * 4, sourceWidth * 4), columns, columnWeights, reduced);
            float weight = rowWeights[sy];
            float rest = 1 - weight;
            for (int x = 0; x < width; x++)
            {
                current[x] += reduced[x] * weight;
                next[x] += reduced[x] * rest;
            }
        }

        Emit(current, destination.Slice(target * width * 4, width * 4), scale);
    }

    // For each source pixel along an axis: the target pixel it starts in, and the part of it that falls there (the rest falls in
    // the next target pixel). A target pixel spans sourceLength / length source pixels.
    private static void Split(int sourceLength, int length, out int[] targets, out float[] weights)
    {
        targets = new int[sourceLength];
        weights = new float[sourceLength];
        double step = (double)sourceLength / length;
        for (int s = 0; s < sourceLength; s++)
        {
            int t = Math.Min(length - 1, (int)(s / step));
            double edge = (t + 1) * step;
            targets[s] = t;
            weights[s] = (float)Math.Clamp(edge - s, 0, 1);
        }
    }

    // A source row's sums for each target pixel of the row, each source pixel split between the one or two it falls in.
    private static void Reduce(ReadOnlySpan<byte> row, int[] columns, float[] weights, Vector4[] reduced)
    {
        Array.Clear(reduced);
        for (int sx = 0; sx < columns.Length; sx++)
        {
            int i = sx * 4;
            var pixel = new Vector4(row[i], row[i + 1], row[i + 2], row[i + 3]);
            float weight = weights[sx];
            int t = columns[sx];
            reduced[t] += pixel * weight;
            reduced[t + 1] += pixel * (1 - weight);
        }
    }

    private static void Emit(Vector4[] sums, Span<byte> row, float scale)
    {
        for (int x = 0; x < row.Length / 4; x++)
        {
            Vector4 mean = Vector4.Clamp((sums[x] * scale) + new Vector4(0.5f), Vector4.Zero, new Vector4(255));
            int i = x * 4;
            row[i] = (byte)mean.X;
            row[i + 1] = (byte)mean.Y;
            row[i + 2] = (byte)mean.Z;
            row[i + 3] = (byte)mean.W;
        }
    }
}
