using System.Reflection;

namespace XylemUI.Text;

/// <summary>
/// The end of the font fallback (design §4.5): the bundled font for what no font of the OS renders, and the split of a text
/// into font runs.
/// </summary>
public static class FontFallback
{
    private static Font? _bundled;

    /// <summary>
    /// Gets the bundled fallback font, opened from the assembly the first time: printable ASCII and a <c>.notdef</c> glyph one em
    /// wide (<c>Fonts/README.md</c>). It lives as long as the process and is shared by every caller, so do not dispose it.
    /// </summary>
    public static Font Bundled => _bundled ??= OpenBundled();

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
    /// <paramref name="runs"/>, which is cleared first. Neighbouring matches of the same font and scale join one run.
    /// </summary>
    /// <param name="provider">The fonts of the OS.</param>
    /// <param name="text">The text.</param>
    /// <param name="language">The language of the text.</param>
    /// <param name="request">The font the text asks for.</param>
    /// <param name="runs">The list to fill; reused, so a text split again into as many runs allocates nothing.</param>
    public static void Split(IFontProvider provider, ReadOnlySpan<char> text, TextLanguage language, in FontRequest request, List<FontRun> runs)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(runs);
        runs.Clear();
        int start = 0;
        while (start < text.Length)
        {
            FontMatch match = ResolveWithFallback(provider, text, start, language, request);
            if (match.Length <= 0 || match.Length > text.Length - start)
            {
                throw new InvalidOperationException($"The font provider matched {match.Length} characters at {start} of {text.Length}.");
            }

            Font font = match.Font!;
            if (runs.Count > 0 && runs[^1] is var last && ReferenceEquals(last.Font, font) && last.Scale == match.Scale)
            {
                runs[^1] = last with { Length = last.Length + match.Length };
            }
            else
            {
                runs.Add(new FontRun(font, start, match.Length, match.Scale));
            }

            start += match.Length;
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
