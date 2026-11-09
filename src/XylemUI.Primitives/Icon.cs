using XylemUI.Text;
using static XylemUI.Elements;

namespace XylemUI.Primitives;

/// <summary>
/// A glyph of an icon font (design §5.0.4, "Icons"): the family of the font that draws it, the code point of the glyph, and the size
/// the glyph was drawn for, which <see cref="Primitive.Icon"/> takes when no size is given. Flutter's <c>IconData</c>, with a size:
/// an icon font that draws each icon for several sizes (Fluent's 16, 20, 24) has a glyph of its own for each.
/// </summary>
/// <param name="Family">The family of the font, registered with <see cref="AppFonts"/> or one of the OS's.</param>
/// <param name="CodePoint">The code point of the glyph, a Unicode scalar value (usually in a private use area).</param>
/// <param name="Size">The size the glyph was drawn for, in logical pixels.</param>
public readonly record struct IconGlyph(string Family, int CodePoint, float Size);

/// <content>
/// The icon (design §5.0.4, "Icons").
/// </content>
public static partial class Primitive
{
    /// <summary>
    /// Makes an icon: a text node that draws one glyph of an icon font (Flutter's <c>Icon</c>), in the color of the text it is
    /// in unless <paramref name="color"/> is given. Without <paramref name="label"/> it is decorative: it is left out of the
    /// accessibility tree and out of the name of the button it is in (<see cref="AccessibilityInfo.Excluded"/>). With one, it is
    /// an image of that name (design §4.4.1).
    /// </summary>
    /// <param name="glyph">The glyph.</param>
    /// <param name="size">The size in logical pixels; <see langword="null"/> for the size the glyph was drawn for.</param>
    /// <param name="color">The color; <see langword="null"/> takes the color of the text around it.</param>
    /// <param name="label">The name assistive technology reads; <see langword="null"/> for a decorative icon.</param>
    /// <returns>The icon.</returns>
    /// <exception cref="ArgumentException">The glyph has no family, or its code point is not a Unicode scalar value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is not a finite number greater than 0.</exception>
    /// <exception cref="InvalidOperationException">No window's root function is running, or there is no current scope.</exception>
    public static Element Icon(IconGlyph glyph, float? size = null, Color? color = null, string? label = null)
    {
        if (string.IsNullOrEmpty(glyph.Family))
        {
            throw new ArgumentException("The icon's glyph has no font family.", nameof(glyph));
        }

        if (!System.Text.Rune.IsValid(glyph.CodePoint))
        {
            throw new ArgumentException("The icon's code point is not a Unicode scalar value.", nameof(glyph));
        }

        float drawn = size ?? glyph.Size;
        if (!float.IsFinite(drawn) || drawn <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), drawn, "The icon's size is a finite number greater than 0.");
        }

        Element icon = Elements.Text(char.ConvertFromUtf32(glyph.CodePoint));
        UiTree tree = icon.Tree!;
        TextStyle style = tree.Nodes.GetRef(icon.Node).Text!.Style;

        // A line as high as the size, as Flutter's Icon sets (height 1.0): the font's line gap would make the box taller than the
        // icon (Fluent's typographic gap is 45 of 500 units).
        tree.SetTextStyle(icon.Node, style with { Font = FontRequest.Default with { Family = glyph.Family }, Size = drawn, LineHeight = 1 });
        if (color is { } c)
        {
            _ = icon.Foreground(c);
        }

        return icon.Accessible(
            label is null ? new AccessibilityInfo { Excluded = true } : new AccessibilityInfo { Role = SemanticsRole.Image, Name = label }
        );
    }
}
