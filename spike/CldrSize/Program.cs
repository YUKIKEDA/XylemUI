namespace CldrSize;

/// <summary>
/// The spike of row LC2 (Issue #862): reads a few bytes of every blob the configuration references, so that no table is dead
/// code, and prints a checksum. The configuration (<c>-p:SpikeConfig=...</c>) decides which generated files are compiled.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        ulong sum = (ulong)args.Length;
#if CLDR_PLURAL
        for (int i = 0; i < Selected.Count; i++)
        {
            sum = Mix(sum, Selected.Plural(i));
        }
#endif
#if CLDR_NUMBER
        for (int i = 0; i < Selected.Count; i++)
        {
            sum = Mix(sum, Selected.Number(i));
        }
#endif
#if CLDR_DATE
        for (int i = 0; i < Selected.Count; i++)
        {
            sum = Mix(sum, Selected.Date(i));
        }
#endif
#if CLDR_LIKELY
        sum = Mix(sum, Selected.Likely);
#endif
        Console.WriteLine(sum);
        return 0;
    }

    private static ulong Mix(ulong sum, ReadOnlySpan<byte> data)
    {
        sum = (sum * 31) + (ulong)data.Length;
        if (!data.IsEmpty)
        {
            sum = (sum * 31) + data[0];
            sum = (sum * 31) + data[data.Length / 2];
            sum = (sum * 31) + data[^1];
        }

        return sum;
    }
}
