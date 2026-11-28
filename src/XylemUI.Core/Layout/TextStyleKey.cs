using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// What a text leaf's shaping and layout read of its style (design §3.2, "Text leaves", row T15): the fields of a
/// <see cref="TextStyle"/> but its color. Equal keys mean equal layouts, so a change of color alone keeps a leaf's caches; the leaf
/// keeps its color apart (<see cref="TextLeaf.Color"/>).
/// </summary>
/// <param name="Font">The font asked for (design §4.5).</param>
/// <param name="Size">The size in logical pixels per em; finite and greater than 0.</param>
/// <param name="Language">The language, which picks the glyphs of a Han character and the fallback fonts.</param>
/// <param name="LineHeight">
/// The line height as a multiple of <paramref name="Size"/>; <see cref="float.NaN"/> for the fonts' own. Finite and greater than 0
/// when given.
/// </param>
internal readonly record struct TextStyleKey(FontRequest Font, float Size, TextLanguage Language, float LineHeight)
{
    /// <summary>
    /// Returns the key of a style.
    /// </summary>
    /// <param name="style">The style.</param>
    /// <returns>Its font, size, language, and line height.</returns>
    public static TextStyleKey Of(in TextStyle style) => new(style.Font, style.Size, style.Language, style.LineHeight);

    /// <summary>
    /// Returns what writing a style over a key and a color changes, in one comparison (design §3.2, row T15).
    /// </summary>
    /// <param name="key">The key there.</param>
    /// <param name="color">The color there; <see langword="null"/> for the color the ancestors resolve.</param>
    /// <param name="style">The style written.</param>
    /// <returns>
    /// <see cref="TextStyleChange.Layout"/> when a field of the key differs, whatever the color;
    /// <see cref="TextStyleChange.PaintOnly"/> when only the color differs; otherwise <see cref="TextStyleChange.Same"/>.
    /// </returns>
    public static TextStyleChange Compare(in TextStyleKey key, Color? color, in TextStyle style)
    {
        // The fields are compared as the record's equality compares them (EqualityComparer<T>.Default), so NaN line heights are
        // equal, as they were when the whole style was compared.
        if (key.Font != style.Font || !key.Size.Equals(style.Size) || key.Language != style.Language || !key.LineHeight.Equals(style.LineHeight))
        {
            return TextStyleChange.Layout;
        }

        return color != style.Color ? TextStyleChange.PaintOnly : TextStyleChange.Same;
    }
}
