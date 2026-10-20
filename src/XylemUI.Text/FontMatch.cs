namespace XylemUI.Text;

/// <summary>
/// The font for the start of a text, as <see cref="IFontProvider.ResolveFont"/> finds it (design §4.5).
/// </summary>
/// <param name="Font">The font for the first <paramref name="Length"/> characters; <see langword="null"/> when no font of the OS
/// renders them.</param>
/// <param name="Length">The number of UTF-16 characters the match covers, at least 1.</param>
/// <param name="Scale">The factor to multiply the font's em size by (DirectWrite's <c>MapCharacters</c> scale); 1 when the font
/// is used at the size asked for.</param>
public readonly record struct FontMatch(Font? Font, int Length, float Scale);
