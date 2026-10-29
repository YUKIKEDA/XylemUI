using System.Reflection;

namespace XylemUI.Text;

/// <summary>
/// The end of the font fallback (design §4.5): the bundled font for what no font of the OS renders, and the split of a text
/// into font runs.
/// </summary>
public static class FontFallback
{
    // Opened once, whichever thread asks first: with `??=`, two threads asking together each opened one, and the font that
    // lost had an id of its own (Issue #303).
    private static readonly Lazy<Font> BundledFont = new(OpenBundled, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets the bundled fallback font, opened from the assembly the first time: printable ASCII and a <c>.notdef</c> glyph one em
    /// wide (<c>Fonts/README.md</c>). It is opened once, when threads ask for it together too. It lives as long as the process
    /// and is shared by every caller, so do not dispose it.
    /// </summary>
    public static Font Bundled => BundledFont.Value;

    /// <summary>
    /// Returns the font for the characters of <paramref name="text"/> from <paramref name="start"/>: the provider's, or the
    /// bundled font where no font of the OS renders them. The fallback always ends: every match covers at least one character.
    /// </summary>
    /// <param name="provider">The fonts of the OS.</param>
    /// <param name="text">The text.</param>
    /// <param name="start">The index of the first character to match.</param>
    /// <param name="language">The language of the text.</param>
    /// <param name="request">The font the text asks for.</param>
    /// <returns>A match whose font is never <see langword="null"/>.</returns>
    public static FontMatch ResolveWithFallback(
        IFontProvider provider,
        ReadOnlySpan<char> text,
        int start,
        TextLanguage language,
        in FontRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(provider);
        FontMatch match = provider.ResolveFont(text, start, language, request);
        return match.Font is null ? match with { Font = Bundled, Scale = 1 } : match;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into the runs of the fonts that render it, in order, and writes them into
    /// <paramref name="runs"/>, which is cleared first. Neighbouring matches of the same font and scale join one run. A run never
    /// ends inside a grapheme cluster (UAX #29): a match that does is taken on to the end of the cluster, so the font of the
    /// cluster's base character draws the whole cluster (design §4.1, "Line breaking and grapheme clusters").
    /// </summary>
    /// <param name="provider">The fonts of the OS.</param>
    /// <param name="text">The text.</param>
    /// <param name="language">The language of the text.</param>
    /// <param name="request">The font the text asks for.</param>
    /// <param name="runs">The list to fill; reused, so a text split again into as many runs allocates nothing.</param>
    public static void Split(IFontProvider provider, ReadOnlySpan<char> text, TextLanguage language, in FontRequest request, List<FontRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        runs.Clear();
        SplitRange(provider, text, 0, text.Length, language, request, runs);
    }

    /// <summary>
    /// Splits the part of <paramref name="text"/> from <paramref name="start"/> to <paramref name="end"/> into font runs, as
    /// <see cref="Split"/> splits a whole text, and adds them to <paramref name="runs"/>: a range of a text leaf with a font or a
    /// language of its own (design §4.8). The provider sees the whole text; a run ends at the part's end, and does not join the
    /// runs before the part.
    /// </summary>
    /// <param name="provider">The fonts of the OS.</param>
    /// <param name="text">The whole text.</param>
    /// <param name="start">The part's first character.</param>
    /// <param name="end">The character after the part.</param>
    /// <param name="language">The language of the part.</param>
    /// <param name="request">The font the part asks for.</param>
    /// <param name="runs">The list to add to.</param>
    internal static void SplitRange(
        IFontProvider provider,
        ReadOnlySpan<char> text,
        int start,
        int end,
        TextLanguage language,
        in FontRequest request,
        List<FontRun> runs
    )
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(runs);
        int first = runs.Count;
        while (start < end)
        {
            FontMatch match = ResolveWithFallback(provider, text, start, language, request);
            if (match.Length <= 0 || match.Length > text.Length - start)
            {
                throw new InvalidOperationException($"The font provider matched {match.Length} characters at {start} of {text.Length}.");
            }

            Font font = match.Font!;

            // Checked at the run's end alone, not by walking the run: most texts are one run, which ends at the text's end.
            int stop = Math.Min(end, start + match.Length);
            while (stop < end && !Unicode.Graphemes.IsBoundary(text, stop))
            {
                stop += char.IsHighSurrogate(text[stop]) && stop + 1 < end && char.IsLowSurrogate(text[stop + 1]) ? 2 : 1;
            }

            int length = stop - start;
            if (runs.Count > first && runs[^1] is var last && ReferenceEquals(last.Font, font) && last.Scale == match.Scale)
            {
                runs[^1] = last with { Length = last.Length + length };
            }
            else
            {
                runs.Add(new FontRun(font, start, length, match.Scale));
            }

            start = stop;
        }
    }

    private static Font OpenBundled()
    {
        using Stream stream =
            typeof(FontFallback).Assembly.GetManifestResourceStream("XylemUIFallback.ttf")
            ?? throw new InvalidOperationException("The bundled fallback font is not in the assembly.");
        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return new Font(data);
    }
}
