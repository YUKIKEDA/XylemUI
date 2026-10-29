using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// Where the outline of a glyph crosses a horizontal band: what an underline or an overline skips (design §4.8). The outline of a
/// (font, glyph) is read once from HarfBuzz (<c>hb_font_draw_glyph</c>), in ems, and the crossing of each band asked for is kept
/// on it, so the same glyph and band are not worked out again. Skia keeps its intercepts the same way, on the outline of a glyph
/// at a fixed size in its strike (<c>src/core/SkGlyph.cpp</c> lines 567 to 610 at fae1d2bd).
/// </summary>
/// <remarks>
/// It keeps at most <see cref="Capacity"/> outlines, and a new one takes the place of the one used longest ago. One belongs to one
/// thread, as the shaper does; a steady state of glyphs and bands it keeps allocates nothing.
/// </remarks>
internal sealed unsafe class GlyphIntercepts
{
    /// <summary>
    /// The number of outlines kept. Skia's strike cache keeps 2 MB of glyphs by default (<c>SK_DEFAULT_FONT_CACHE_LIMIT</c>); an
    /// outline of a Han character here is about 3 KB (some 200 segments of 16 bytes), so 1,024 outlines stay within 3 MB. A window of
    /// Japanese text whose every character is underlined shows fewer distinct characters than that.
    /// </summary>
    public const int Capacity = 1024;

    // The pieces a curve is cut into. At 4, a quadratic's line is within 1/64 of its control point's distance from the chord: a
    // fraction of a font unit, far below a pixel at the sizes text is drawn at.
    private const int CurvePieces = 4;

    private static readonly nint Funcs = CreateFuncs();

    private readonly Dictionary<(int Font, uint Glyph), int> _index;
    private readonly Outline?[] _outlines;
    private float[] _scratch = new float[256];
    private int _count;
    private long _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphIntercepts"/> class.
    /// </summary>
    /// <param name="capacity">The number of outlines kept: <see cref="Capacity"/>, or fewer in a test.</param>
    public GlyphIntercepts(int capacity = Capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _index = new Dictionary<(int Font, uint Glyph), int>(capacity);
        _outlines = new Outline?[capacity];
    }

    /// <summary>
    /// Gets the number of outlines kept.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Returns whether the outline of a glyph is kept.
    /// </summary>
    /// <param name="font">The glyph's font.</param>
    /// <param name="glyph">The glyph.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Holds(Font font, uint glyph) => _index.ContainsKey((font.Id, glyph));

    /// <summary>
    /// Returns where a glyph's outline crosses a band, in ems from the glyph's origin.
    /// </summary>
    /// <param name="font">The glyph's font.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="top">The band's top, in ems below the baseline (negative above it).</param>
    /// <param name="bottom">The band's bottom, in ems below the baseline; greater than <paramref name="top"/>.</param>
    /// <param name="left">The leftmost point of the outline in the band.</param>
    /// <param name="right">The rightmost point of the outline in the band.</param>
    /// <returns><see langword="true"/> when the outline crosses the band.</returns>
    public bool Intercept(Font font, uint glyph, float top, float bottom, out float left, out float right)
    {
        ArgumentNullException.ThrowIfNull(font);
        Outline outline = Get(font, glyph);
        outline.Used = ++_clock;
        for (int i = 0; i < outline.BandCount; i++)
        {
            ref Band band = ref outline.Bands[i];
            if (band.Top == top && band.Bottom == bottom)
            {
                left = band.Left;
                right = band.Right;
                return left <= right;
            }
        }

        Band crossing = Cross(outline, top, bottom);
        int slot = outline.BandCount < Outline.BandCapacity ? outline.BandCount++ : outline.NextBand++ % Outline.BandCapacity;
        outline.Bands[slot] = crossing;
        left = crossing.Left;
        right = crossing.Right;
        return left <= right;
    }

    // The leftmost and rightmost points of the segments' parts inside the band: an empty band (left > right) when none is.
    private static Band Cross(Outline outline, float top, float bottom)
    {
        float left = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        ReadOnlySpan<float> segments = outline.Segments.AsSpan(0, outline.Count);
        for (int i = 0; i < segments.Length; i += 4)
        {
            float x0 = segments[i];
            float y0 = segments[i + 1];
            float x1 = segments[i + 2];
            float y1 = segments[i + 3];
            if (MathF.Max(y0, y1) < top || MathF.Min(y0, y1) > bottom)
            {
                continue;
            }

            if (y0 == y1)
            {
                left = MathF.Min(left, MathF.Min(x0, x1));
                right = MathF.Max(right, MathF.Max(x0, x1));
                continue;
            }

            // The part of the segment between the band's lines: the points where it crosses them, or its ends inside.
            float ta = Math.Clamp((top - y0) / (y1 - y0), 0, 1);
            float tb = Math.Clamp((bottom - y0) / (y1 - y0), 0, 1);
            float xa = x0 + ((x1 - x0) * ta);
            float xb = x0 + ((x1 - x0) * tb);
            left = MathF.Min(left, MathF.Min(xa, xb));
            right = MathF.Max(right, MathF.Max(xa, xb));
        }

        return new Band(top, bottom, left, right);
    }

    // The outline of a glyph: kept, or read into a new slot, or into the one used longest ago.
    private Outline Get(Font font, uint glyph)
    {
        if (_index.TryGetValue((font.Id, glyph), out int kept))
        {
            return _outlines[kept]!;
        }

        int slot;
        if (_count < _outlines.Length)
        {
            slot = _count++;
        }
        else
        {
            slot = 0;
            for (int i = 1; i < _outlines.Length; i++)
            {
                if (_outlines[i]!.Used < _outlines[slot]!.Used)
                {
                    slot = i;
                }
            }

            _ = _index.Remove((_outlines[slot]!.Font, _outlines[slot]!.Glyph));
        }

        Outline outline = _outlines[slot] ??= new Outline();
        outline.Font = font.Id;
        outline.Glyph = glyph;
        outline.BandCount = 0;
        outline.NextBand = 0;
        Read(font, glyph, outline);
        _index[(font.Id, glyph)] = slot;
        return outline;
    }

    // The glyph's outline as line segments in ems, y down. The callbacks write into the scratch array, pinned for the call, and
    // count what did not fit; then the array grows and the glyph is read again.
    private void Read(Font font, uint glyph, Outline outline)
    {
        nint harfBuzzFont = font.HarfBuzzFont;
        Sink sink;
        while (true)
        {
            // Safe: HarfBuzz calls the callbacks during this call only, and they write through the sink inside its capacity.
            fixed (float* buffer = _scratch)
            {
                sink = new Sink { Buffer = buffer, Capacity = _scratch.Length };
                HarfBuzz.FontDrawGlyph(harfBuzzFont, glyph, Funcs, &sink);
            }

            if (sink.Count <= _scratch.Length)
            {
                break;
            }

            _scratch = new float[Math.Max(sink.Count, _scratch.Length * 2)];
        }

        float em = font.UnitsPerEm;
        if (outline.Segments.Length < sink.Count)
        {
            outline.Segments = new float[sink.Count];
        }

        for (int i = 0; i < sink.Count; i += 2)
        {
            outline.Segments[i] = _scratch[i] / em;
            outline.Segments[i + 1] = -_scratch[i + 1] / em;
        }

        outline.Count = sink.Count;
    }

    private static nint CreateFuncs()
    {
        nint funcs = HarfBuzz.DrawFuncsCreate();
        HarfBuzz.DrawFuncsSetLineToFunc(
            funcs,
            (void*)(delegate* unmanaged[Cdecl]<void*, void*, void*, float, float, void*, void>)&LineTo,
            null,
            null
        );
        HarfBuzz.DrawFuncsSetQuadraticToFunc(
            funcs,
            (void*)(delegate* unmanaged[Cdecl]<void*, void*, void*, float, float, float, float, void*, void>)&QuadraticTo,
            null,
            null
        );
        HarfBuzz.DrawFuncsSetCubicToFunc(
            funcs,
            (void*)(delegate* unmanaged[Cdecl]<void*, void*, void*, float, float, float, float, float, float, void*, void>)&CubicTo,
            null,
            null
        );
        HarfBuzz.DrawFuncsMakeImmutable(funcs);
        return funcs;
    }

    // The callbacks only read their arguments and write through the sink inside its capacity: they call nothing that throws and
    // allocate nothing, so they have no catch (.cursor/rules/interop.mdc). HarfBuzz closes each contour with a line to its start
    // (hb-draw.hh lines 166 to 173), so no callback closes it. The pen is still at the segment's start while a callback runs.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void LineTo(void* funcs, void* data, void* state, float x, float y, void* user)
    {
        var pen = (HarfBuzz.DrawState*)state;
        ((Sink*)data)->Add(pen->CurrentX, pen->CurrentY, x, y);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void QuadraticTo(void* funcs, void* data, void* state, float cx, float cy, float x, float y, void* user)
    {
        var sink = (Sink*)data;
        var pen = (HarfBuzz.DrawState*)state;
        float sx = pen->CurrentX;
        float sy = pen->CurrentY;
        float px = sx;
        float py = sy;
        for (int i = 1; i <= CurvePieces; i++)
        {
            float t = (float)i / CurvePieces;
            float u = 1 - t;
            float qx = (u * u * sx) + (2 * u * t * cx) + (t * t * x);
            float qy = (u * u * sy) + (2 * u * t * cy) + (t * t * y);
            sink->Add(px, py, qx, qy);
            px = qx;
            py = qy;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CubicTo(void* funcs, void* data, void* state, float c1x, float c1y, float c2x, float c2y, float x, float y, void* user)
    {
        var sink = (Sink*)data;
        var pen = (HarfBuzz.DrawState*)state;
        float sx = pen->CurrentX;
        float sy = pen->CurrentY;
        float px = sx;
        float py = sy;
        for (int i = 1; i <= CurvePieces; i++)
        {
            float t = (float)i / CurvePieces;
            float u = 1 - t;
            float a = u * u * u;
            float b = 3 * u * u * t;
            float c = 3 * u * t * t;
            float d = t * t * t;
            float qx = (a * sx) + (b * c1x) + (c * c2x) + (d * x);
            float qy = (a * sy) + (b * c1y) + (c * c2y) + (d * y);
            sink->Add(px, py, qx, qy);
            px = qx;
            py = qy;
        }
    }

    // What the callbacks write into: a buffer and its capacity, and how many floats the outline needs.
    private struct Sink
    {
        public float* Buffer;
        public int Capacity;
        public int Count;

        public void Add(float x0, float y0, float x1, float y1)
        {
            if (Count + 4 <= Capacity)
            {
                Buffer[Count] = x0;
                Buffer[Count + 1] = y0;
                Buffer[Count + 2] = x1;
                Buffer[Count + 3] = y1;
            }

            Count += 4;
        }
    }

    // A band and where an outline crosses it; left > right when it does not.
    private readonly record struct Band(float Top, float Bottom, float Left, float Right);

    private sealed class Outline
    {
        // The bands kept on one glyph: an underline, an overline, and the second lines of double ones, at a few sizes.
        public const int BandCapacity = 4;

        public int Font;
        public uint Glyph;
        public float[] Segments = [];
        public int Count;
        public long Used;
        public Bands Bands;
        public int BandCount;
        public int NextBand;
    }

    [InlineArray(Outline.BandCapacity)]
    private struct Bands
    {
        private Band _first;
    }
}
