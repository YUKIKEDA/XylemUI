using XylemUI.Text;

namespace XylemUI.Theme.Fluent;

/// <summary>
/// One of Fluent's text styles (<c>body1</c>, <c>title1</c>, and the others of <see cref="FluentTypography"/>): its size, line height,
/// and weight. The family is the OS's UI font, not Fluent's web list (design §13.2, "使わないもの").
/// </summary>
/// <param name="FontSize">The size of the text, in logical pixels.</param>
/// <param name="LineHeight">The height of a line, in logical pixels.</param>
/// <param name="Weight">The weight.</param>
public readonly record struct FluentTextStyle(float FontSize, float LineHeight, FontWeight Weight);
