using System.Text;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// The language of a text run, for shaping (design §4.1): a BCP 47 tag parsed once by HarfBuzz.
/// </summary>
/// <remarks>
/// HarfBuzz keeps each parsed language for the life of the process and returns the same value for the same tag, so a language is
/// made once (for example when the UI language is set) and passed to every <see cref="TextShaper.Shape"/> without a cost.
/// </remarks>
public readonly unsafe struct TextLanguage : IEquatable<TextLanguage>
{
    private TextLanguage(nint handle) => Handle = handle;

    /// <summary>
    /// Gets the tag as HarfBuzz keeps it: lower case, with <c>-</c> between subtags.
    /// </summary>
    public string Tag => Handle == 0 ? string.Empty : new string((sbyte*)HarfBuzz.LanguageToString(Handle));

    internal nint Handle { get; }

    /// <summary>
    /// Returns the language of a BCP 47 tag, such as <c>ja</c>, <c>en-US</c>, or <c>zh-Hans</c>.
    /// </summary>
    /// <param name="tag">The tag, ASCII.</param>
    /// <returns>The language.</returns>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is empty, blank, or not ASCII.</exception>
    public static TextLanguage FromTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (!Ascii.IsValid(tag))
        {
            throw new ArgumentException("A language tag is ASCII.", nameof(tag));
        }

        Span<byte> bytes = tag.Length <= 64 ? stackalloc byte[tag.Length] : new byte[tag.Length];
        _ = Encoding.ASCII.GetBytes(tag, bytes);
        fixed (byte* text = bytes)
        {
            return new TextLanguage(HarfBuzz.LanguageFromString(text, bytes.Length));
        }
    }

    /// <summary>Compares two languages.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether they are the same language.</returns>
    public static bool operator ==(TextLanguage left, TextLanguage right) => left.Equals(right);

    /// <summary>Compares two languages.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether they are different languages.</returns>
    public static bool operator !=(TextLanguage left, TextLanguage right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(TextLanguage other) => Handle == other.Handle;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TextLanguage other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Handle.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Tag;
}
