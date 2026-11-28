using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The style of a text leaf's text (design §3.2, "Text leaves"): the font asked for, the size, the language, the line height, and
/// the color. A change of the color alone lays nothing out again; it only draws the text again.
/// </summary>
/// <param name="Font">The font asked for (design §4.5).</param>
/// <param name="Size">The size in logical pixels per em; finite and greater than 0.</param>
/// <param name="Language">The language, which picks the glyphs of a Han character and the fallback fonts.</param>
/// <param name="LineHeight">
/// The line height as a multiple of <paramref name="Size"/>; <see cref="float.NaN"/> for the fonts' own (ascent, descent, and line
/// gap). Finite and greater than 0 when given.
/// </param>
/// <param name="Color">
/// The color of the text; <see langword="null"/> for the color the ancestors resolve (their <c>Foreground</c> and pseudo-states,
/// design §5.0, "The color of text").
/// </param>
public readonly record struct TextStyle(FontRequest Font, float Size, TextLanguage Language, float LineHeight, Color? Color = null)
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
