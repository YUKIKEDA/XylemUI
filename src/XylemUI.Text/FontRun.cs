namespace XylemUI.Text;

/// <summary>
/// A range of a text that one font renders, to shape and draw as one glyph run (design §4.1, step E).
/// </summary>
/// <param name="Font">The font.</param>
/// <param name="Start">The index of the run's first UTF-16 character in the text.</param>
/// <param name="Length">The number of UTF-16 characters in the run.</param>
/// <param name="Scale">The factor to multiply the size asked for by when the run is shaped and drawn.</param>
public readonly record struct FontRun(Font Font, int Start, int Length, float Scale);
