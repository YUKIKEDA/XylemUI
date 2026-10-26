using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// What a text leaf's layout reads of its style (design §3.2, "Text leaves"): the font asked for, the size, the language, and the
/// line height. The color is not here: a change of color needs no layout, and equal styles mean equal layouts.
/// </summary>
/// <param name="Font">The font asked for (design §4.5).</param>
/// <param name="Size">The size in logical pixels per em; finite and greater than 0.</param>
/// <param name="Language">The language, which picks the glyphs of a Han character and the fallback fonts.</param>
/// <param name="LineHeight">
/// The line height as a multiple of <paramref name="Size"/>; <see cref="float.NaN"/> for the fonts' own (ascent, descent, and line
/// gap). Finite and greater than 0 when given.
/// </param>
internal readonly record struct TextStyle(FontRequest Font, float Size, TextLanguage Language, float LineHeight)
{
    /// <summary>
    /// Throws when a value is out of its range.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <param name="name">The name of the argument the style came in.</param>
    /// <exception cref="ArgumentOutOfRangeException">The size, or the line height when given, is not finite and greater than 0.</exception>
    /// <exception cref="ArgumentException">The language is the default value.</exception>
    public static void Validate(in TextStyle style, string name)
    {
        if (!float.IsFinite(style.Size) || style.Size <= 0)
        {
            throw new ArgumentOutOfRangeException(name, style.Size, "The text size is not finite and greater than 0.");
        }

        if (!float.IsNaN(style.LineHeight) && (!float.IsFinite(style.LineHeight) || style.LineHeight <= 0))
        {
            throw new ArgumentOutOfRangeException(name, style.LineHeight, "The line height is not NaN, or finite and greater than 0.");
        }

        if (style.Language.Handle == 0)
        {
            throw new ArgumentException("The language is the default value; use TextLanguage.FromTag.", name);
        }
    }
}
