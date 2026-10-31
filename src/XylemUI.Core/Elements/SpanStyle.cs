using System.Numerics;
using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The style of a range of a text node's text (design §4.8, row T12), as Flutter's <c>TextStyle</c> has it. A field left
/// <see langword="null"/> takes the range around it, and around them all the text node's own style and color.
/// </summary>
/// <param name="Font">The font: its family, weight, slant, and stretch.</param>
/// <param name="Size">The size, in logical pixels per em.</param>
/// <param name="Language">The language, which picks the font's forms and the line breaking.</param>
/// <param name="LetterSpacing">Logical pixels added to each grapheme cluster's advance.</param>
/// <param name="Features">The OpenType features. Make it once and use it again in each write.</param>
/// <param name="Color">The color of the text.</param>
/// <param name="Background">The color behind the text, from the line's top to its bottom.</param>
/// <param name="Decoration">The lines drawn with the text.</param>
/// <param name="Shadow">The shadow of the text. It is kept, and drawn from row T14 on.</param>
public readonly record struct SpanStyle(
    FontRequest? Font = null,
    float? Size = null,
    TextLanguage? Language = null,
    float? LetterSpacing = null,
    FontFeatures? Features = null,
    Color? Color = null,
    Color? Background = null,
    TextDecoration? Decoration = null,
    TextShadow? Shadow = null
)
{
    /// <summary>
    /// Returns this style laid over the style of the range around it: this inner range's fields where it sets them, the outer's where it
    /// does not.
    /// </summary>
    /// <param name="outer">The style of the range around this one.</param>
    /// <returns>The combined style.</returns>
    internal SpanStyle Over(in SpanStyle outer) =>
        new(
            Font ?? outer.Font,
            Size ?? outer.Size,
            Language ?? outer.Language,
            LetterSpacing ?? outer.LetterSpacing,
            Features ?? outer.Features,
            Color ?? outer.Color,
            Background ?? outer.Background,
            Decoration ?? outer.Decoration,
            Shadow ?? outer.Shadow
        );
}

/// <summary>
/// Where the lines of a <see cref="TextDecoration"/> go (design §4.8).
/// </summary>
[Flags]
public enum DecorationLines : byte
{
    /// <summary>
    /// No line.
    /// </summary>
    None = 0,

    /// <summary>
    /// Below the baseline, at the font's underline position.
    /// </summary>
    Underline = 1,

    /// <summary>
    /// At the font's ascent.
    /// </summary>
    Overline = 2,

    /// <summary>
    /// Through the text, at the font's strikeout position.
    /// </summary>
    LineThrough = 4,
}

/// <summary>
/// The shape of a <see cref="TextDecoration"/>'s lines (design §4.8). A wavy line comes with the curves of row R5.
/// </summary>
public enum DecorationStyle : byte
{
    /// <summary>
    /// One solid line.
    /// </summary>
    Solid,

    /// <summary>
    /// Two solid lines, the second 3 logical pixels below the first (Flutter's spacing).
    /// </summary>
    Double,

    /// <summary>
    /// Short dashes of one fourteenth of the size, gaps one and a half times as long.
    /// </summary>
    Dotted,

    /// <summary>
    /// Dashes of four fourteenths of the size, gaps half as long.
    /// </summary>
    Dashed,
}

/// <summary>
/// Where an inline object sits on its line, up and down (design §4.8, Flutter's <c>PlaceholderAlignment</c>). The line grows to
/// hold the object.
/// </summary>
public enum PlaceholderAlignment : byte
{
    /// <summary>
    /// The object's first baseline on the line's baseline; an object with no baseline has its bottom there.
    /// </summary>
    Baseline,

    /// <summary>
    /// The object's bottom on the line's baseline.
    /// </summary>
    AboveBaseline,

    /// <summary>
    /// The object's top on the line's baseline.
    /// </summary>
    BelowBaseline,

    /// <summary>
    /// The object's top at the top of the line's text: the largest ascent of its fonts above the baseline.
    /// </summary>
    Top,

    /// <summary>
    /// The object's bottom at the bottom of the line's text: the largest descent of its fonts below the baseline.
    /// </summary>
    Bottom,

    /// <summary>
    /// The object's middle at the middle of the line's text, between its ascent and its descent.
    /// </summary>
    Middle,
}

/// <summary>
/// The lines drawn with a range of text (design §4.8): an underline and an overline skip the glyphs they cross; a line through
/// does not.
/// </summary>
/// <param name="Lines">Where the lines go.</param>
/// <param name="Style">Their shape.</param>
/// <param name="Color">Their color; <see langword="null"/> takes the range's text color.</param>
/// <param name="Thickness">A factor on the font's thickness for the line.</param>
public readonly record struct TextDecoration(
    DecorationLines Lines,
    DecorationStyle Style = DecorationStyle.Solid,
    Color? Color = null,
    float Thickness = 1
)
{
    // The bit of Lines that marks a clause's underline: a bit no line uses, so that the mark takes no byte of its own and the
    // ranges keep design §4.8's sizes.
    private const DecorationLines ClauseBit = (DecorationLines)0x80;

    /// <summary>
    /// Gets a value indicating whether the lines underline a clause of an IME's composition (design §4.7): shortened by 1 logical
    /// pixel at each end of the range and of each line the range is broken across, so that two clauses side by side show a gap,
    /// and not cut where the glyphs cross them (Blink's <c>styleable_marker_painter.cc</c> lines 163 to 170 shorten its composition
    /// underlines, and draw them at the bottom of the line, under the glyphs). No decoration an app makes is one.
    /// </summary>
    internal bool Composition => (Lines & ClauseBit) != 0;

    /// <summary>
    /// Returns the underline of a clause of an IME's composition (<see cref="Composition"/>).
    /// </summary>
    /// <param name="style">The shape of the line.</param>
    /// <param name="color">Its color; <see langword="null"/> takes the range's text color.</param>
    /// <param name="thickness">A factor on the font's thickness for the line.</param>
    /// <returns>The decoration.</returns>
    internal static TextDecoration Clause(DecorationStyle style, Color? color, float thickness) =>
        new(DecorationLines.Underline | ClauseBit, style, color, thickness);
}

/// <summary>
/// The shadow of a range of text (design §4.8). Kept by the style from row T12 on; drawn from row T14 on.
/// </summary>
/// <param name="Color">The shadow's color.</param>
/// <param name="Offset">Where it falls from the text, in logical pixels.</param>
/// <param name="BlurRadius">How far it blurs, in logical pixels.</param>
public readonly record struct TextShadow(Color Color, Vector2 Offset, float BlurRadius);

/// <summary>
/// A range a text writer opened (<see cref="NodeTextWriterExtensions.Style"/>, <see cref="NodeTextWriterExtensions.Link"/>), closed
/// by <c>using</c>. It refers to its writer, so it lives on the stack while the writer writes.
/// </summary>
public ref struct TextSpanScope
{
    private ref NodeTextWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextSpanScope"/> struct.
    /// </summary>
    /// <param name="writer">The writer whose range this closes.</param>
    internal TextSpanScope(ref NodeTextWriter writer) => _writer = ref writer;

    /// <summary>
    /// Closes the range at what the writer has written so far.
    /// </summary>
    public readonly void Dispose() => _writer.CloseSpan();
}
