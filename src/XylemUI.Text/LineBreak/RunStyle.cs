namespace XylemUI.Text.LineBreak;

/// <summary>
/// What a font run of a <see cref="ShapingResult"/> is shaped with besides its font (design §4.8): the layout's fields of the
/// range of a text leaf it is in, resolved with the leaf's style.
/// </summary>
/// <param name="Size">The size, in logical pixels per em; the run's <see cref="FontRun.Scale"/> multiplies it.</param>
/// <param name="Language">The language.</param>
/// <param name="LetterSpacing">Logical pixels added to the advance of each cluster.</param>
/// <param name="Features">The OpenType features; <see langword="null"/> for the font's defaults.</param>
internal readonly record struct RunStyle(float Size, TextLanguage Language, float LetterSpacing, FontFeatures? Features);
