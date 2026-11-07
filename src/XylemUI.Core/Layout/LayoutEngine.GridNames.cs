using System.Collections.Immutable;

namespace XylemUI;

internal static partial class LayoutEngine
{
    [ThreadStatic]
    private static ScratchStack<long>? _gridNameScratch;

    // The named lines of an axis's template, as pairs of a name's number and a line (from 1 at the explicit grid's start),
    // sorted by the name and then the line, each pair once (Taffy's NamedLineResolver::new). A repetition names its lines in
    // every repetition, its last line on the same line as the first line of the next. pairs is empty to count them only.
    private static int LineNamePairs(ImmutableArray<GridTrack> template, int autoRepetitions, Span<long> pairs)
    {
        if (template.IsDefaultOrEmpty)
        {
            return 0;
        }

        bool write = !pairs.IsEmpty;
        int count = 0;
        int line = 1;
        foreach (GridTrack entry in template)
        {
            if (entry.IsLine)
            {
                foreach (int name in entry.LineNames)
                {
                    count = Add(pairs, write, count, name, line);
                }

                continue;
            }

            if (!entry.IsRepeat)
            {
                line++;
                continue;
            }

            int times = entry.IsAutoRepeat ? autoRepetitions : entry.Count;
            int tracks = entry.Repeated.Length;
            for (int i = 0; i < times; i++)
            {
                // A line past the largest number of tracks is never placed on: the explicit grid is cut there.
                if (line > Track.MaxTracks)
                {
                    break;
                }

                if (!entry.RepeatedNames.IsDefault)
                {
                    for (int j = 0; j <= tracks; j++)
                    {
                        foreach (int name in entry.RepeatedNames[j])
                        {
                            count = Add(pairs, write, count, name, line + j);
                        }
                    }
                }

                line += tracks;
            }
        }

        if (!write)
        {
            return count;
        }

        Span<long> written = pairs[..count];
        written.Sort();
        int unique = 0;
        for (int i = 0; i < written.Length; i++)
        {
            if (i == 0 || written[i] != written[unique - 1])
            {
                written[unique++] = written[i];
            }
        }

        return unique;

        static int Add(Span<long> pairs, bool write, int count, int name, int line)
        {
            if (write)
            {
                pairs[count] = ((long)name << 32) | (uint)line;
            }

            return count + 1;
        }
    }

    // The lines (from 1) named by a name's number, in order.
    private static ReadOnlySpan<long> LinesNamed(ReadOnlySpan<long> pairs, int name)
    {
        if (name < 0 || pairs.IsEmpty)
        {
            return [];
        }

        long first = (long)name << 32;
        int start = LowerBound(pairs, first);
        int end = LowerBound(pairs, first + (1L << 32));
        return pairs[start..end];

        static int LowerBound(ReadOnlySpan<long> pairs, long value)
        {
            int low = 0;
            int high = pairs.Length;
            while (low < high)
            {
                int middle = (low + high) >>> 1;
                if (pairs[middle] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }

    // A pair's line from the explicit grid's start, from 0.
    private static int OriginZeroOf(long pair) => (int)(pair & 0xFFFF_FFFF) - 1;

    // The line of a placement by name that is not a span (Taffy's find_named_line): the name alone takes the first line of the
    // edge of its area (`<name>-start` at a start, `<name>-end` at an end) when there is one; else, and with an index, the
    // index-th line of the name from the start, or back from the explicit grid's end for a negative index.
    private static int NamedLine(GridLineNames.Placement placement, bool start, ReadOnlySpan<long> names, int explicitCount)
    {
        if (placement.Index == 0)
        {
            ReadOnlySpan<long> edge = LinesNamed(names, start ? placement.StartEdge : placement.EndEdge);
            if (!edge.IsEmpty)
            {
                return Math.Clamp(OriginZeroOf(edge[0]), -Track.MaxTracks, Track.MaxTracks);
            }
        }

        ReadOnlySpan<long> lines = LinesNamed(names, placement.Name);
        return placement.Index >= 0
            ? NthLineForwards(lines, 0, Math.Max((int)placement.Index, 1), explicitCount)
            : NthLineBackwards(lines, explicitCount, -placement.Index);
    }

    // The n-th of the lines that is not before firstLine; with too few, the implicit lines after the explicit grid count as
    // named (Taffy's nth_line_forwards, CSS Grid §8.3.1).
    private static int NthLineForwards(ReadOnlySpan<long> lines, int firstLine, int n, int explicitCount)
    {
        firstLine = Math.Max(firstLine, 0);
        int skip = 0;
        while (skip < lines.Length && OriginZeroOf(lines[skip]) < firstLine)
        {
            skip++;
        }

        lines = lines[skip..];
        int line = n <= lines.Length ? OriginZeroOf(lines[n - 1]) : Math.Max(firstLine, explicitCount + 1) + (n - lines.Length) - 1;
        return Math.Clamp(line, -Track.MaxTracks, Track.MaxTracks);
    }

    // The n-th of the lines back from lastLine, not after it; with too few, the implicit lines before the explicit grid count
    // as named (Taffy's nth_line_backwards). Taffy first limits lastLine to the explicit grid's end, which changes nothing: no
    // named line is past it, and the implicit lines count from -1 at most.
    private static int NthLineBackwards(ReadOnlySpan<long> lines, int lastLine, int n)
    {
        int keep = lines.Length;
        while (keep > 0 && OriginZeroOf(lines[keep - 1]) > lastLine)
        {
            keep--;
        }

        int line = keep >= n ? OriginZeroOf(lines[keep - n]) : Math.Min(lastLine, -1) - (n - keep) + 1;
        return Math.Clamp(line, -Track.MaxTracks, Track.MaxTracks);
    }
}
