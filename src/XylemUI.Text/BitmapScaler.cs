using System.Numerics;
using System.Runtime.InteropServices;

namespace XylemUI.Text;

/// <summary>
/// Scales a premultiplied blue, green, red, and alpha image to another size, for the bitmap color glyphs of a font (design §5.0.3,
/// "PNG bitmaps", roadmap row T9). Each axis is scaled on its own: down by the average over the area each destination pixel
/// covers, each source pixel weighed by the part of it inside, and up by bilinear interpolation between the centers of the
/// source pixels, the edge pixels held beyond the edge. A side of the same size is copied.
/// </summary>
/// <remarks>
/// The weights and the intermediate image are kept for the next call, so one scaler is used on one thread.
/// </remarks>
internal sealed class BitmapScaler
{
    private Vector4[] _between = new Vector4[1024];
    private Vector4[] _sum = new Vector4[256];
    private Tap[] _taps = new Tap[1024];
    private int[] _starts = new int[257];

    /// <summary>
    /// Scales <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The image: rows of <paramref name="sourceWidth"/> × 4 bytes.</param>
    /// <param name="sourceWidth">The image's width, at least 1.</param>
    /// <param name="sourceHeight">The image's height, at least 1.</param>
    /// <param name="destination">Where the scaled image goes, in rows <paramref name="stride"/> bytes apart.</param>
    /// <param name="width">The scaled width, at least 1.</param>
    /// <param name="height">The scaled height, at least 1.</param>
    /// <param name="stride">The bytes from one destination row to the next, at least <paramref name="width"/> × 4.</param>
    public void Scale(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, Span<byte> destination, int width, int height, int stride)
    {
        // Across: from the source rows into sourceHeight rows of width pixels, the four channels of a pixel added at once.
        if (_between.Length < sourceHeight * width)
        {
            Array.Resize(ref _between, Math.Max(sourceHeight * width, _between.Length * 2));
        }

        Weights(sourceWidth, width);
        for (int y = 0; y < sourceHeight; y++)
        {
            ReadOnlySpan<uint> row = MemoryMarshal.Cast<byte, uint>(source.Slice(y * sourceWidth * 4, sourceWidth * 4));
            Span<Vector4> to = _between.AsSpan(y * width, width);
            ReadOnlySpan<Tap> taps = _taps;
            for (int x = 0; x < width; x++)
            {
                Vector4 sum = Vector4.Zero;
                for (int t = _starts[x]; t < _starts[x + 1]; t++)
                {
                    Tap tap = taps[t];
                    uint pixel = row[tap.Index];

                    // Through int: x64 has no instruction from an unsigned integer to a float (136 x 128 to 15 x 15 took 53 µs, 38 µs so).
                    sum += new Vector4((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF), (int)(pixel >> 24)) * tap.Weight;
                }

                to[x] = sum;
            }
        }

        // Down: from those rows into the destination, adding whole rows so that the reads stay in order.
        Weights(sourceHeight, height);
        if (_sum.Length < width)
        {
            Array.Resize(ref _sum, Math.Max(width, _sum.Length * 2));
        }

        Span<Vector4> sums = _sum.AsSpan(0, width);
        for (int y = 0; y < height; y++)
        {
            sums.Clear();
            for (int t = _starts[y]; t < _starts[y + 1]; t++)
            {
                ReadOnlySpan<Vector4> row = _between.AsSpan(_taps[t].Index * width, width);
                float weight = _taps[t].Weight;
                for (int x = 0; x < width; x++)
                {
                    sums[x] += row[x] * weight;
                }
            }

            // The sums are 0 to 255 but for float error, so adding a half and cutting rounds them to the nearest.
            Span<uint> to = MemoryMarshal.Cast<byte, uint>(destination.Slice(y * stride, width * 4));
            for (int x = 0; x < width; x++)
            {
                Vector4 value = Vector4.Clamp(sums[x], Vector4.Zero, new Vector4(255)) + new Vector4(0.5f);
                to[x] = (uint)value.X | ((uint)value.Y << 8) | ((uint)value.Z << 16) | ((uint)value.W << 24);
            }
        }
    }

    // The taps of each destination pixel along one axis, in _taps from _starts[i] to _starts[i + 1].
    private void Weights(int from, int to)
    {
        if (_starts.Length < to + 1)
        {
            Array.Resize(ref _starts, to + 1);
        }

        int count = 0;
        double scale = (double)from / to;
        for (int i = 0; i < to; i++)
        {
            _starts[i] = count;
            if (from == to)
            {
                Add(ref count, i, 1);
            }
            else if (to < from)
            {
                // The destination pixel covers [i × scale, (i + 1) × scale) of the source.
                double start = i * scale;
                double end = (i + 1) * scale;
                for (int s = (int)Math.Floor(start); s < Math.Min(from, (int)Math.Ceiling(end)); s++)
                {
                    double overlap = Math.Min(end, s + 1) - Math.Max(start, s);
                    if (overlap > 0)
                    {
                        Add(ref count, s, (float)(overlap / scale));
                    }
                }
            }
            else
            {
                // The destination pixel's center, in source pixels, between the centers of two source pixels.
                double center = ((i + 0.5) * scale) - 0.5;
                int left = (int)Math.Floor(center);
                float right = (float)(center - left);
                Add(ref count, Math.Clamp(left, 0, from - 1), 1 - right);
                Add(ref count, Math.Clamp(left + 1, 0, from - 1), right);
            }
        }

        _starts[to] = count;
    }

    private void Add(ref int count, int index, float weight)
    {
        if (count == _taps.Length)
        {
            Array.Resize(ref _taps, _taps.Length * 2);
        }

        _taps[count++] = new Tap(index, weight);
    }

    private readonly record struct Tap(int Index, float Weight);
}
