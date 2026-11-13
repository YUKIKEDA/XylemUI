using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace XylemUI;

/// <summary>
/// Represents a locale: a language, a script, and a region, read from a BCP 47 language tag (design §4.11, "Locale").
/// </summary>
/// <remarks>
/// Two locales are equal when their three fields are. The <see langword="default"/> value is the undetermined language
/// <c>und</c>, with no script and no region.
/// </remarks>
public readonly struct Locale : IEquatable<Locale>
{
    private const string Undetermined = "und";

    // The longest tag ToLanguageTag writes: a language of 8 letters, a script of 4, and a region of 3 digits, with two hyphens.
    private const int LongestLanguageTag = 8 + 1 + 4 + 1 + 3;

    // Null stands for "und", so that the default value and Parse("und") are the same value.
    private readonly string? _language;
    private readonly string? _script;
    private readonly string? _region;

    private Locale(string? language, string? script, string? region)
    {
        _language = language;
        _script = script;
        _region = region;
    }

    /// <summary>
    /// Gets the language subtag in lower case (<c>ja</c>), or <c>und</c> for the <see langword="default"/> value.
    /// </summary>
    public string Language => _language ?? Undetermined;

    /// <summary>
    /// Gets the script subtag with its first letter in upper case (<c>Hant</c>), or <see langword="null"/> when the tag has none.
    /// </summary>
    public string? Script => _script;

    /// <summary>
    /// Gets the region subtag in upper case (<c>TW</c>) or as 3 digits (<c>419</c>), or <see langword="null"/> when the tag has none.
    /// </summary>
    public string? Region => _region;

    /// <summary>
    /// Returns a value indicating whether two locales have the same language, script, and region.
    /// </summary>
    /// <param name="left">The first locale.</param>
    /// <param name="right">The second locale.</param>
    /// <returns><see langword="true"/> when the three fields are equal.</returns>
    public static bool operator ==(Locale left, Locale right) => left.Equals(right);

    /// <summary>
    /// Returns a value indicating whether two locales differ in their language, script, or region.
    /// </summary>
    /// <param name="left">The first locale.</param>
    /// <param name="right">The second locale.</param>
    /// <returns><see langword="true"/> when one of the three fields differs.</returns>
    public static bool operator !=(Locale left, Locale right) => !left.Equals(right);

    /// <summary>
    /// Reads a locale from a BCP 47 language tag.
    /// </summary>
    /// <remarks>
    /// The tag is read without regard to case, and its fields are put in their canonical case. A grandfathered or redundant tag
    /// that the IANA registry replaces, a deprecated language or region, and an extended language whose prefix is the tag's
    /// language are replaced by their preferred values (<c>i-klingon</c> is <c>tlh</c>, <c>iw</c> is <c>he</c>, <c>zh-yue</c> is
    /// <c>yue</c>). Variants, extensions, and private use are read and dropped. A tag is well-formed when it matches the
    /// <c>langtag</c> syntax of RFC 5646 §2.1 or is a grandfathered tag the registry replaces; whether its subtags are registered
    /// is not checked.
    /// </remarks>
    /// <param name="tag">The language tag, such as <c>zh-Hant-TW</c>.</param>
    /// <returns>The locale.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="tag"/> is not a well-formed language tag.</exception>
    public static Locale Parse(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!TryParse(tag, out Locale locale))
        {
            ThrowMalformed(tag);
        }

        return locale;
    }

    /// <summary>
    /// Reads a locale from a BCP 47 language tag, as <see cref="Parse"/> does, without throwing.
    /// </summary>
    /// <param name="tag">The language tag, such as <c>zh-Hant-TW</c>.</param>
    /// <param name="locale">The locale, or the <see langword="default"/> value when the tag is not read.</param>
    /// <returns><see langword="true"/> when <paramref name="tag"/> is a well-formed language tag; <see langword="false"/> for a
    /// malformed tag and for <see langword="null"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? tag, out Locale locale)
    {
        if (tag is null || !HasSubtagShape(tag))
        {
            locale = default;
            return false;
        }

        // A grandfathered or redundant tag is replaced as a whole before its subtags are read (RFC 5646 §4.5, step 2): i-klingon
        // does not match langtag, and sgn-BR would otherwise read as the language sgn in Brazil.
        // The whole tag is matched first, then the tag without its extensions and private use, which are read and dropped: with
        // them, sgn-BR-u-nu-latn is still bzs. Variants stay in what is matched: en-GB-oed and zh-min-nan mean what they do by
        // the subtag in a variant's place.
        ReadOnlySpan<char> whole = tag;
        if (TryReplaceWholeTag(whole, out string? replacement) || TryReplaceWholeTag(whole[..EndOfLanguageRange(whole)], out replacement))
        {
            return TryParseLangtag(replacement, out locale);
        }

        return TryParseLangtag(tag, out locale);
    }

    /// <summary>
    /// Returns the language tag of the three fields, such as <c>zh-Hant-TW</c>.
    /// </summary>
    /// <returns>The language, then the script and the region when the locale has them, joined with hyphens.</returns>
    public string ToLanguageTag()
    {
        string language = Language;
        if (_script is null && _region is null)
        {
            return language;
        }

        Span<char> buffer = stackalloc char[LongestLanguageTag];
        language.CopyTo(buffer);
        int length = language.Length;
        if (_script is not null)
        {
            buffer[length++] = '-';
            _script.CopyTo(buffer[length..]);
            length += _script.Length;
        }

        if (_region is not null)
        {
            buffer[length++] = '-';
            _region.CopyTo(buffer[length..]);
            length += _region.Length;
        }

        return new string(buffer[..length]);
    }

    /// <inheritdoc />
    public bool Equals(Locale other) =>
        string.Equals(_language, other._language, StringComparison.Ordinal)
        && string.Equals(_script, other._script, StringComparison.Ordinal)
        && string.Equals(_region, other._region, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Locale other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_language, _script, _region);

    /// <summary>
    /// Returns the language tag of the three fields, as <see cref="ToLanguageTag"/> does.
    /// </summary>
    /// <returns>The language tag.</returns>
    public override string ToString() => ToLanguageTag();

    private static bool TryReplaceWholeTag(ReadOnlySpan<char> tag, [NotNullWhen(true)] out string? replacement)
    {
        replacement = null;
        if (tag.Length > LocaleSubtags.LongestTag)
        {
            return false;
        }

        Span<char> lower = stackalloc char[LocaleSubtags.LongestTag];
        for (int i = 0; i < tag.Length; i++)
        {
            lower[i] = ToLowerAscii(tag[i]);
        }

        return LocaleSubtags.TryGetTagReplacement(lower[..tag.Length], out replacement);
    }

    // The length of a tag up to its first extension or private use: the first subtag of one character after the first subtag
    // (the i of i-klingon is a first subtag).
    private static int EndOfLanguageRange(ReadOnlySpan<char> tag)
    {
        for (int i = tag.IndexOf('-'); i >= 0 && i + 1 < tag.Length; )
        {
            int next = tag[(i + 1)..].IndexOf('-');
            if (next == 1 || (next < 0 && tag.Length - i - 1 == 1))
            {
                return i;
            }

            if (next < 0)
            {
                break;
            }

            i += next + 1;
        }

        return tag.Length;
    }

    // Every subtag is 1 to 8 ASCII letters or digits, and subtags are joined by single hyphens (RFC 5646 §2.1). The langtag reader
    // relies on it: an empty subtag means the end of the tag.
    private static bool HasSubtagShape(ReadOnlySpan<char> tag)
    {
        int run = 0;
        foreach (char c in tag)
        {
            if (c == '-')
            {
                if (run == 0)
                {
                    return false;
                }

                run = 0;
            }
            else if (!char.IsAsciiLetterOrDigit(c) || ++run > 8)
            {
                return false;
            }
        }

        return run > 0;
    }

    // Reads the langtag production of RFC 5646 §2.1 from a tag that HasSubtagShape accepted, and makes its canonical fields.
    private static bool TryParseLangtag(ReadOnlySpan<char> tag, out Locale locale)
    {
        locale = default;
        int position = 0;

        // language = 2*3ALPHA ["-" extlang] / 4ALPHA / 5*8ALPHA
        ReadOnlySpan<char> language = NextSubtag(tag, ref position);
        if (language.Length < 2 || !IsAlpha(language))
        {
            return false;
        }

        ReadOnlySpan<char> subtag = NextSubtag(tag, ref position);

        // extlang = 3ALPHA *2("-" 3ALPHA): only the first names a language; the other two are permanently reserved (§2.2.2) and
        // are read and dropped.
        ReadOnlySpan<char> extlang = default;
        if (language.Length <= 3)
        {
            for (int count = 0; count < 3 && subtag.Length == 3 && IsAlpha(subtag); count++)
            {
                if (count == 0)
                {
                    extlang = subtag;
                }

                subtag = NextSubtag(tag, ref position);
            }
        }

        // script = 4ALPHA
        ReadOnlySpan<char> script = default;
        if (subtag.Length == 4 && IsAlpha(subtag))
        {
            script = subtag;
            subtag = NextSubtag(tag, ref position);
        }

        // region = 2ALPHA / 3DIGIT
        ReadOnlySpan<char> region = default;
        if ((subtag.Length == 2 && IsAlpha(subtag)) || (subtag.Length == 3 && IsDigits(subtag)))
        {
            region = subtag;
            subtag = NextSubtag(tag, ref position);
        }

        // variant = 5*8alphanum / (DIGIT 3alphanum), read and dropped.
        while (subtag.Length >= 5 || (subtag.Length == 4 && char.IsAsciiDigit(subtag[0])))
        {
            subtag = NextSubtag(tag, ref position);
        }

        // extension = singleton 1*("-" (2*8alphanum)), read and dropped. Any letter or digit but x is a singleton.
        while (subtag.Length == 1 && !IsPrivateUseSingleton(subtag[0]))
        {
            subtag = NextSubtag(tag, ref position);
            if (subtag.Length < 2)
            {
                return false;
            }

            while (subtag.Length >= 2)
            {
                subtag = NextSubtag(tag, ref position);
            }
        }

        // privateuse = "x" 1*("-" (1*8alphanum)), read and dropped: it runs to the end of the tag, and HasSubtagShape has
        // checked the shape of each of its subtags.
        if (subtag.Length == 1)
        {
            if (NextSubtag(tag, ref position).IsEmpty)
            {
                return false;
            }
        }
        else if (!subtag.IsEmpty)
        {
            return false;
        }

        locale = Canonicalize(language, extlang, script, region);
        return true;
    }

    // Puts the fields in their canonical form (RFC 5646 §4.5, step 3, and the case of §2.1.1).
    private static Locale Canonicalize(ReadOnlySpan<char> language, ReadOnlySpan<char> extlang, ReadOnlySpan<char> script, ReadOnlySpan<char> region)
    {
        Span<char> languageBuffer = stackalloc char[8];
        Span<char> extlangBuffer = stackalloc char[3];
        Span<char> scriptBuffer = stackalloc char[4];
        Span<char> regionBuffer = stackalloc char[3];

        ReadOnlySpan<char> lowerLanguage = ToLowerAscii(language, languageBuffer);

        // An extended language replaces the language only under the one prefix the registry gives it: zh-yue is yue, while
        // zh-min stays zh, since min extends ms, and an unregistered extlang-shaped subtag (zh-abc) is dropped.
        if (!extlang.IsEmpty)
        {
            ReadOnlySpan<char> lowerExtlang = ToLowerAscii(extlang, extlangBuffer);
            if (LocaleSubtags.TryGetExtlangPrefix(lowerExtlang, out string? prefix) && lowerLanguage.SequenceEqual(prefix))
            {
                lowerLanguage = lowerExtlang;
            }
        }

        string? languageValue;
        if (LocaleSubtags.TryGetLanguageReplacement(lowerLanguage, out string? languageReplacement))
        {
            languageValue = languageReplacement;
        }
        else
        {
            languageValue = lowerLanguage.SequenceEqual(Undetermined) ? null : new string(lowerLanguage);
        }

        string? scriptValue = null;
        if (!script.IsEmpty)
        {
            scriptBuffer[0] = ToUpperAscii(script[0]);
            _ = ToLowerAscii(script[1..], scriptBuffer[1..]);
            scriptValue = new string(scriptBuffer);
        }

        string? regionValue = null;
        if (!region.IsEmpty)
        {
            Span<char> upperRegion = regionBuffer[..region.Length];
            for (int i = 0; i < region.Length; i++)
            {
                upperRegion[i] = ToUpperAscii(region[i]);
            }

            regionValue = LocaleSubtags.TryGetRegionReplacement(upperRegion, out string? regionReplacement)
                ? regionReplacement
                : new string(upperRegion);
        }

        return new Locale(languageValue, scriptValue, regionValue);
    }

    // Returns the next subtag and moves past its hyphen, or an empty span at the end of the tag. The position is scoped: the
    // result points into the tag only, so a subtag can be kept in the reader's span locals.
    private static ReadOnlySpan<char> NextSubtag(ReadOnlySpan<char> tag, scoped ref int position)
    {
        ReadOnlySpan<char> rest = tag[position..];
        int end = rest.IndexOf('-');
        if (end < 0)
        {
            position = tag.Length;
            return rest;
        }

        position += end + 1;
        return rest[..end];
    }

    private static bool IsAlpha(ReadOnlySpan<char> subtag)
    {
        foreach (char c in subtag)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDigits(ReadOnlySpan<char> subtag) => !subtag.ContainsAnyExceptInRange('0', '9');

    private static bool IsPrivateUseSingleton(char c) => c is 'x' or 'X';

    // ASCII case mapping on purpose: the case of a language tag is ASCII's (RFC 5646 §2.1.1), whatever the culture.
    private static char ToLowerAscii(char c) => char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;

    private static char ToUpperAscii(char c) => char.IsAsciiLetterLower(c) ? (char)(c & ~0x20) : c;

    private static ReadOnlySpan<char> ToLowerAscii(ReadOnlySpan<char> source, Span<char> destination)
    {
        Span<char> lower = destination[..source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            lower[i] = ToLowerAscii(source[i]);
        }

        return lower;
    }

    [DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowMalformed(string tag) => throw new FormatException("The language tag '" + tag + "' is not well-formed.");
}
