using XylemUI.Primitives;

namespace XylemUI.Primitives.Tests;

// The icon's arguments (design §5.0.4, "Icons", row IC1). An icon is a text node, which these tests do not draw: a bad argument is
// refused before any node is made, so no tree is needed. The drawn icon is tested with the Fluent fonts in
// XylemUI.Theme.Fluent.Tests.
public sealed class IconTests
{
    /// <summary>
    /// A glyph with no family, a code point that is not a Unicode scalar value, and a size that is not a finite number greater than
    /// 0 are refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No window's root function runs.
    ///
    /// Steps:
    /// - Make an icon of a glyph with an empty family; of U+D800 (a surrogate); of U+110000; of a size of 0 in the glyph; of a size
    ///   of NaN given.
    ///
    /// Expected:
    /// - <see cref="ArgumentException"/> three times, then <see cref="ArgumentOutOfRangeException"/> twice: each before the missing
    ///   root function is noticed.
    /// </remarks>
    [Fact]
    public void Icon_BadGlyphOrSize_IsRefused()
    {
        _ = Assert.Throws<ArgumentException>(() => Primitive.Icon(new IconGlyph(string.Empty, 0xF10A, 24)));
        _ = Assert.Throws<ArgumentException>(() => Primitive.Icon(new IconGlyph("Icons", 0xD800, 24)));
        _ = Assert.Throws<ArgumentException>(() => Primitive.Icon(new IconGlyph("Icons", 0x110000, 24)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Primitive.Icon(new IconGlyph("Icons", 0xF10A, 0)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => Primitive.Icon(new IconGlyph("Icons", 0xF10A, 24), float.NaN));
    }
}
