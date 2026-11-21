using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace XylemUI;

/// <summary>
/// Formats and parses numbers with a locale's CLDR tables (design §4.11, "Numbers"): decimal, percent, currency, and compact. The
/// symbols and the digits are those of the locale's default numbering system, the integer digits are grouped as its pattern and its
/// <c>minimumGroupingDigits</c> say, and a value is rounded half to even. An instance does not change after it is made, so any thread
/// may call it.
/// </summary>
/// <remarks>
/// Making a format reads the tables and allocates; <see cref="TryFormat(decimal, Span{char}, out int)"/>, its overloads,
/// <see cref="Round"/>, <see cref="Of(decimal)"/>, and <see cref="TryParse(ReadOnlySpan{char}, out decimal)"/> of up to 255 characters
/// allocate nothing. <see cref="Format(decimal)"/> and its overloads make a string. A locale's decimal and percent formats are
/// <see cref="LocaleData.DecimalFormat"/> and <see cref="LocaleData.PercentFormat"/>.
/// </remarks>
public sealed class NumberFormatter
{
    // The digits of a decimal (29) or of a double's shortest round-trip text (17), each held once.
    private const int DigitCapacity = 32;

    // The invariant text of a decimal (29 digits, a point, and the zeros of a scale of 28) or of a double's shortest text, with its
    // exponent.
    private const int TextCapacity = 40;

    // Text this long or longer is parsed in a rented array; shorter text in a buffer on the stack.
    private const int ParseCapacity = 256;

    // The most integer or fraction digits the options take: ICU's kMaxIntFracSig in icu4c's number_types.h (Issue #906).
    private const int MaxDigits = 999;

    private const char NoBreakSpace = (char)0x00A0;

    private const char NarrowNoBreakSpace = (char)0x202F;

    private const char FullWidthZero = (char)0xFF10;

    private const char FullWidthNine = (char)0xFF19;

    private const char MinusSign = (char)0x2212;

    private const char LeftToRightMark = (char)0x200E;

    private const char RightToLeftMark = (char)0x200F;

    private const char ArabicLetterMark = (char)0x061C;

    private static readonly NumberOptions DefaultOptions = new();

    private readonly NumberKind _kind;
    private readonly NumberSymbols _symbols;
    private readonly string _decimalSeparator;
    private readonly string _groupSeparator;
    private readonly NumberAffixes _positive;
    private readonly NumberAffixes _negative;
    private readonly int _primaryGrouping;
    private readonly int _secondaryGrouping;
    private readonly int _minimumGrouping;
    private readonly int _minimumIntegerDigits;
    private readonly int _minimumFractionDigits;
    private readonly int _maximumFractionDigits;
    private readonly int _shift;
    private readonly CompactPattern[] _compact;
    private readonly PluralRules? _plurals;
    private readonly string _parseMinus;
    private readonly string _parsePlus;
    private readonly string _parsePercent;

    private NumberFormatter(
        NumberKind kind,
        NumberSymbols symbols,
        NumberPattern pattern,
        NumberOptions options,
        int minimumFractionDigits,
        int maximumFractionDigits,
        CompactPattern[] compact,
        PluralRules? plurals
    )
    {
        Check(options);
        bool currency = kind == NumberKind.Currency;
        _kind = kind;
        _symbols = symbols;
        _decimalSeparator = currency ? symbols.CurrencyDecimal : symbols.Decimal;
        _groupSeparator = currency ? symbols.CurrencyGroup : symbols.Group;
        _positive = pattern.Positive;
        _negative = pattern.Negative;
        _primaryGrouping = options.UseGrouping ? pattern.PrimaryGrouping : 0;
        _secondaryGrouping = pattern.SecondaryGrouping;

        // A compact number groups only from 5 integer digits (1200T, 12,000T), as ICU's compact notation does.
        _minimumGrouping = kind == NumberKind.Compact ? Math.Max(2, symbols.MinimumGroupingDigits) : symbols.MinimumGroupingDigits;
        _minimumIntegerDigits = options.MinimumIntegerDigits;
        (_minimumFractionDigits, _maximumFractionDigits) = FractionDigits(options, minimumFractionDigits, maximumFractionDigits);
        _shift = kind == NumberKind.Percent ? 2 : 0;
        _compact = compact;
        _plurals = plurals;

        // Parsing reads the signs without the bidi marks CLDR puts in them (Issue #906).
        _parseMinus = WithoutBidiMarks(symbols.Minus);
        _parsePlus = WithoutBidiMarks(symbols.Plus);
        _parsePercent = WithoutBidiMarks(symbols.Percent);
    }

    private enum NumberKind : byte
    {
        Decimal,
        Percent,
        Currency,
        Compact,
    }

    // The fraction digits a decimal is rounded at before the point moves for a percent: decimal.Round takes at most 28.
    private int DecimalRoundDigits => Math.Min(_maximumFractionDigits, 28 - _shift) + _shift;

    /// <summary>
    /// Makes a decimal format with the locale's standard decimal pattern (<c>#,##0.###</c>: up to 3 fraction digits).
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="options">The digits and the grouping; <see langword="null"/> takes the pattern's.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> has a negative number of digits, no integer digit,
    /// more than 999 integer or fraction digits, or fewer maximum than minimum fraction digits.</exception>
    public static NumberFormatter Decimal(LocaleData locale, NumberOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        NumberSymbols symbols = locale.Numbers;
        NumberPattern pattern = NumberPattern.Parse(symbols.DecimalPattern, symbols, null, false);
        return new NumberFormatter(
            NumberKind.Decimal,
            symbols,
            pattern,
            options ?? DefaultOptions,
            pattern.MinimumFractionDigits,
            pattern.MaximumFractionDigits,
            [],
            null
        );
    }

    /// <summary>
    /// Makes a percent format with the locale's standard percent pattern (<c>#,##0%</c>): the value is multiplied by 100, so 0.25 is
    /// <c>25%</c>.
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="options">The digits and the grouping; <see langword="null"/> takes the pattern's.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> has a negative number of digits, no integer digit,
    /// more than 999 integer or fraction digits, or fewer maximum than minimum fraction digits.</exception>
    public static NumberFormatter Percent(LocaleData locale, NumberOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        NumberSymbols symbols = locale.Numbers;
        NumberPattern pattern = NumberPattern.Parse(symbols.PercentPattern, symbols, null, false);
        return new NumberFormatter(
            NumberKind.Percent,
            symbols,
            pattern,
            options ?? DefaultOptions,
            pattern.MinimumFractionDigits,
            pattern.MaximumFractionDigits,
            [],
            null
        );
    }

    /// <summary>
    /// Makes a currency format with the locale's standard currency pattern and the currency's standard symbol in that locale
    /// (<c>$</c> in English, <c>$US</c> in French), or its narrow symbol when <see cref="NumberOptions.NarrowCurrencySymbol"/> is set.
    /// A currency the locale has no symbol for shows its code. A symbol whose grapheme cluster next to the digits starts with a letter
    /// (<c>CHF</c>, and Kannada's <c>ಲೀ</c>, a letter and a vowel sign) takes the locale's <c>alphaNextToNumber</c> pattern
    /// (English: <c>CHF</c>, a no-break space, <c>1,234.50</c>); a locale with no such pattern puts a no-break space between the digits
    /// and a symbol that is neither a symbol nor a space there, by CLDR's <c>currencySpacing</c>. The locale's <c>accounting</c>
    /// pattern is not read: a negative amount takes the standard pattern's minus sign, not parentheses. The fraction digits are the
    /// currency's in CLDR's <c>currencyData</c> (0 for <c>JPY</c>, 2 for most).
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="isoCode">The currency's ISO 4217 code, three upper-case letters (<c>JPY</c>).</param>
    /// <param name="options">The digits, the grouping, and the symbol's width; <see langword="null"/> takes the currency's digits and
    /// the standard symbol.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> or <paramref name="isoCode"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="isoCode"/> is not three upper-case ASCII letters.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> has a negative number of digits, no integer digit,
    /// more than 999 integer or fraction digits, or fewer maximum than minimum fraction digits.</exception>
    public static NumberFormatter Currency(LocaleData locale, string isoCode, NumberOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(isoCode);
        if (!IsCurrencyCode(isoCode))
        {
            ThrowHelper.ThrowNotACurrencyCode(isoCode);
        }

        options ??= DefaultOptions;
        NumberSymbols symbols = locale.Numbers;
        string symbol = CurrencySymbol(locale.CurrencyTable, isoCode, options.NarrowCurrencySymbol);

        // The pattern for a symbol whose grapheme cluster next to the digits starts with a letter (CHF) is the locale's
        // alphaNextToNumber one; currencySpacing applies only where the locale has none (Issue #906).
        bool spacing = symbols.CurrencyAlphaPattern.Length == 0;
        bool letter = IsLetterNextToNumber(symbol, NumberPattern.CurrencyBeforeNumber(symbols.CurrencyPattern));
        string text = !spacing && letter ? symbols.CurrencyAlphaPattern : symbols.CurrencyPattern;
        NumberPattern pattern = NumberPattern.Parse(text, symbols, symbol, spacing);
        int digits = CurrencyDigits(isoCode);
        return new NumberFormatter(NumberKind.Currency, symbols, pattern, options, digits, digits, [], null);
    }

    /// <summary>
    /// Makes a compact format with the locale's compact decimal patterns (<c>1.2K</c>, <c>1.2万</c>, <c>1.2 thousand</c>). The pattern
    /// is chosen by the power of ten and by the plural category of the shown number, with a compact exponent of 0 (1.5 for
    /// <c>1.5K</c>, as ICU selects; design §4.11, "Numbers"), or is the pattern of 1 alone when the shown number is 1 (French
    /// <c>mille</c>). The shown number keeps two significant digits
    /// when its integer part has one digit, and no fraction digit otherwise (<c>1.2K</c>, <c>12K</c>, <c>123K</c>); a number the
    /// locale does not compact (below 1000, or a pattern of <c>0</c>) is written in full with the same rounding.
    /// </summary>
    /// <param name="locale">The locale's tables.</param>
    /// <param name="style">The width.</param>
    /// <returns>The format.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a member of <see cref="CompactStyle"/>.</exception>
    public static NumberFormatter Compact(LocaleData locale, CompactStyle style)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((byte)style, (byte)CompactStyle.Long, nameof(style));
        NumberSymbols symbols = locale.Numbers;
        NumberPattern pattern = NumberPattern.Parse(symbols.DecimalPattern, symbols, null, false);
        CompactPattern[] compact = CompactPattern.Read(locale.CompactTable, style, symbols);
        return new NumberFormatter(NumberKind.Compact, symbols, pattern, DefaultOptions, 0, 0, compact, locale.Plurals);
    }

    /// <summary>
    /// Writes a whole number. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(long value, Span<char> destination, out int charsWritten) => TryFormat((decimal)value, destination, out charsWritten);

    /// <summary>
    /// Writes a whole number that is not negative. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(ulong value, Span<char> destination, out int charsWritten) => TryFormat((decimal)value, destination, out charsWritten);

    /// <summary>
    /// Writes a decimal, rounded half to even at the format's most fraction digits. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(decimal value, Span<char> destination, out int charsWritten)
    {
        bool negative = value < 0;
        Span<char> digits = stackalloc char[DigitCapacity];
        if (_kind == NumberKind.Compact)
        {
            int unrounded = DigitsOf(Math.Abs(value), 28, digits, out int unroundedPoint);
            return TryWriteCompact(digits, unrounded, unroundedPoint, negative, destination, out charsWritten);
        }

        int length = DigitsOf(Math.Abs(value), DecimalRoundDigits, digits, out int point);

        // Zero, or a value that rounds to zero, has no digit: shifting its point would count the percent's two places as integer
        // digits and write "00%" (PR #944's review).
        int shifted = length == 0 ? point : point + _shift;
        return TryWrite(digits[..length], shifted, negative ? _negative : _positive, _minimumGrouping, destination, out charsWritten);
    }

    /// <summary>
    /// Writes a double: the shortest decimal digits that read back as it, rounded half to even at the format's most fraction digits,
    /// so that it writes as the decimal of those digits does (1.015 in a format of 2 fraction digits is <c>1.02</c>, as
    /// <c>1.015m</c> is). A percent format moves the point of those digits by 2. Negative zero is written as zero; not a number and
    /// the infinities are written with the locale's symbols. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="destination">The characters to write to.</param>
    /// <param name="charsWritten">The characters written; 0 when <paramref name="destination"/> is too short.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too short.</returns>
    public bool TryFormat(double value, Span<char> destination, out int charsWritten)
    {
        if (double.IsNaN(value))
        {
            return TryWriteSymbol(_symbols.NaN, _positive, destination, out charsWritten);
        }

        // -0.0 is not less than 0, so it writes as 0, as a decimal's zero does (Issue #906, after PR #944's review).
        bool negative = value < 0.0;
        double magnitude = Math.Abs(value);
        if (double.IsInfinity(magnitude))
        {
            return TryWriteSymbol(_symbols.Infinity, negative ? _negative : _positive, destination, out charsWritten);
        }

        Span<char> digits = stackalloc char[DigitCapacity];
        int length = DigitsOf(magnitude, digits, out int point);
        if (_kind == NumberKind.Compact)
        {
            return TryWriteCompact(digits, length, point, negative, destination, out charsWritten);
        }

        // The point moves on the digits, not in binary: 4.525 as a percent is 452.5, which rounds to 452 (Issue #906). Zero has no
        // digit, and its point stays at 0, as on the decimal path.
        point = length == 0 ? point : point + _shift;
        length = RoundHalfEven(digits, length, ref point, _maximumFractionDigits);
        return TryWrite(digits[..length], point, negative ? _negative : _positive, _minimumGrouping, destination, out charsWritten);
    }

    /// <summary>
    /// Returns a whole number with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number and the format.</returns>
    public FormattedNumber Of(long value) => new(this, (decimal)value);

    /// <summary>
    /// Returns a whole number that is not negative with this format, for an interpolation of a <see cref="NodeTextWriter"/>.
    /// Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number and the format.</returns>
    public FormattedNumber Of(ulong value) => new(this, (decimal)value);

    /// <summary>
    /// Returns a decimal with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number and the format.</returns>
    public FormattedNumber Of(decimal value) => new(this, value);

    /// <summary>
    /// Returns a double with this format, for an interpolation of a <see cref="NodeTextWriter"/>. Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number and the format.</returns>
    public FormattedNumber Of(double value) => new(this, value);

    /// <summary>
    /// Returns a whole number formatted as a new string; not for the frame path.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The formatted number.</returns>
    public string Format(long value) => Of(value).ToString();

    /// <summary>
    /// Returns a whole number that is not negative formatted as a new string; not for the frame path.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The formatted number.</returns>
    public string Format(ulong value) => Of(value).ToString();

    /// <summary>
    /// Returns a decimal formatted as a new string; not for the frame path.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The formatted number.</returns>
    public string Format(decimal value) => Of(value).ToString();

    /// <summary>
    /// Returns a double formatted as a new string, as <see cref="TryFormat(double, Span{char}, out int)"/> writes it; not for the
    /// frame path.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The formatted number.</returns>
    public string Format(double value) => Of(value).ToString();

    /// <summary>
    /// Returns the number this format shows for a decimal, for <see cref="PluralRules.Select(decimal)"/>: rounded as the format
    /// rounds it, with a scale of the fraction digits shown (<c>1.50</c> for 1.5 in a format with 2 fraction digits). A percent
    /// format returns the number before the percent sign (25 for 0.25); a compact format, the number the compact form stands for
    /// (1200 for <c>1.2K</c>). Allocates nothing.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The number shown.</returns>
    /// <exception cref="OverflowException">The number is outside the range of <see cref="decimal"/>: a percent of a value over
    /// 7.9 × 10^26, or a compact number that rounds up past <see cref="decimal.MaxValue"/>.</exception>
    public decimal Round(decimal value)
    {
        bool negative = value < 0;
        Span<char> digits = stackalloc char[DigitCapacity];
        int length;
        int point;
        int fraction;
        if (_kind == NumberKind.Compact)
        {
            length = DigitsOf(Math.Abs(value), 28, digits, out point);
            length = RoundCompact(digits, length, ref point, out _, out int divisor, out _);
            point += divisor;
            fraction = 0;
        }
        else
        {
            length = DigitsOf(Math.Abs(value), DecimalRoundDigits, digits, out point);
            point += _shift;
            fraction = _minimumFractionDigits;
        }

        if (!TryDecimal(digits[..length], point, fraction, negative, out decimal rounded))
        {
            ThrowHelper.ThrowRoundedNumberOverflow();
        }

        return rounded;
    }

    /// <summary>
    /// Reads a number a decimal or a percent format writes: the locale's digits, ASCII digits, and full-width digits (U+FF10 to
    /// U+FF19); the locale's decimal separator; its group separator between integer digits, where it is skipped and its place is not
    /// checked, so that German reads <c>3.14</c> as 314 (and, where the separator is U+00A0 or U+202F, U+0020 and the other of the
    /// two are read as it too); its minus or plus sign first (and an ASCII hyphen-minus where its minus sign is U+2212); and, for a
    /// percent format, its percent sign before or after the digits, which is optional. White space at either end and the bidi marks
    /// U+200E, U+200F, and U+061C anywhere are skipped. A percent format divides the number by 100, with or without its sign.
    /// Allocates nothing for text of up to 255 characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number; 0 when the text is not read.</param>
    /// <returns><see langword="false"/> when the text has another character, no digit, or a number outside the range of
    /// <see cref="decimal"/>.</returns>
    /// <exception cref="InvalidOperationException">The format is a currency or a compact format.</exception>
    public bool TryParse(ReadOnlySpan<char> text, out decimal value) => TryParseNumber(text, out value);

    /// <summary>
    /// Reads a number a decimal or a percent format writes, as <see cref="TryParse(ReadOnlySpan{char}, out decimal)"/> does, into a
    /// double. Not a number and the infinities are not read, and neither are digits past the range of <see cref="double"/>.
    /// Allocates nothing for text of up to 255 characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The number; 0 when the text is not read.</param>
    /// <returns><see langword="false"/> when the text has another character, no digit, or a number past the range of
    /// <see cref="double"/>.</returns>
    /// <exception cref="InvalidOperationException">The format is a currency or a compact format.</exception>
    public bool TryParse(ReadOnlySpan<char> text, out double value) => TryParseNumber(text, out value);

    private static void Check(NumberOptions options)
    {
        if (options.MinimumIntegerDigits < 1)
        {
            ThrowHelper.ThrowNumberOptionOutOfRange(nameof(options), "MinimumIntegerDigits is less than 1.");
        }

        if (options.MinimumFractionDigits < 0 || options.MaximumFractionDigits < 0)
        {
            ThrowHelper.ThrowNumberOptionOutOfRange(nameof(options), "A number of fraction digits is negative.");
        }

        // Without a bound, the digit loops of TryFormat and the buffer FormattedNumber.ToString grows would follow any number.
        bool tooMany = options.MinimumIntegerDigits > MaxDigits || options.MinimumFractionDigits > MaxDigits;
        if (tooMany || options.MaximumFractionDigits > MaxDigits)
        {
            ThrowHelper.ThrowNumberOptionOutOfRange(nameof(options), "A number of digits is greater than 999.");
        }

        if (options.MinimumFractionDigits > options.MaximumFractionDigits)
        {
            ThrowHelper.ThrowNumberOptionOutOfRange(nameof(options), "MinimumFractionDigits is greater than MaximumFractionDigits.");
        }
    }

    // The options' fraction digits; one given alone moves the pattern's other one past it, as ICU's setters do.
    private static (int Minimum, int Maximum) FractionDigits(NumberOptions options, int minimum, int maximum) =>
        (options.MinimumFractionDigits, options.MaximumFractionDigits) switch
        {
            (int least, int most) => (least, most),
            (int least, null) => (least, Math.Max(least, maximum)),
            (null, int most) => (Math.Min(minimum, most), most),
            _ => (minimum, maximum),
        };

    // Whether the symbol's grapheme cluster next to the digits (its last before them, its first after them) starts with a letter,
    // General Category L: UTS #35 part 3 uses the alphaNextToNumber pattern then. It judges the cluster, not the last code point, so
    // Kannada's ಲೀ (U+0CB2 and the vowel sign U+0CC0) is a letter (PR #944's review). It runs when the format is made.
    private static bool IsLetterNextToNumber(string symbol, bool before)
    {
        int cluster = 0;
        if (before)
        {
            for (int at = 0; at < symbol.Length; at += StringInfo.GetNextTextElementLength(symbol, at))
            {
                cluster = at;
            }
        }

        _ = Rune.DecodeFromUtf16(symbol.AsSpan(cluster), out Rune rune, out _);
        return Rune.IsLetter(rune);
    }

    // ISO 4217's form of a code: three upper-case ASCII letters.
    private static bool IsCurrencyCode(string isoCode) =>
        isoCode.Length == 3 && char.IsAsciiLetterUpper(isoCode[0]) && char.IsAsciiLetterUpper(isoCode[1]) && char.IsAsciiLetterUpper(isoCode[2]);

    // The currency's symbol in the locale's table: entries of a code and two strings, the standard symbol (empty when it is the
    // code) and the narrow one (empty when it is the standard one). A currency the table does not list shows its code.
    private static string CurrencySymbol(ReadOnlySpan<byte> table, string isoCode, bool narrow)
    {
        var reader = new CldrTableReader(table);
        while (!reader.End)
        {
            if (!reader.ReadCode(isoCode))
            {
                reader.SkipString();
                reader.SkipString();
                continue;
            }

            string symbol = reader.ReadString();
            string narrowSymbol = reader.ReadString();
            return narrow && narrowSymbol.Length > 0 ? narrowSymbol
                : symbol.Length > 0 ? symbol
                : isoCode;
        }

        return isoCode;
    }

    // The currency's fraction digits in CLDR's currencyData: entries of a code and its digits, for the currencies whose digits are
    // not the default.
    private static int CurrencyDigits(string isoCode)
    {
        ReadOnlySpan<byte> table = CldrNumberTables.CurrencyDigits;
        for (int i = 0; i < table.Length; i += 4)
        {
            if (table[i] == isoCode[0] && table[i + 1] == isoCode[1] && table[i + 2] == isoCode[2])
            {
                return table[i + 3];
            }
        }

        return CldrNumberTables.DefaultCurrencyDigits;
    }

    // The digits of a decimal rounded half to even at a number of fraction digits.
    private static int DigitsOf(decimal magnitude, int fractionDigits, Span<char> digits, out int point)
    {
        decimal rounded = decimal.Round(magnitude, fractionDigits, MidpointRounding.ToEven);
        Span<char> text = stackalloc char[TextCapacity];
        _ = rounded.TryFormat(text, out int written, default, CultureInfo.InvariantCulture);
        return Normalize(text[..written], digits, out point);
    }

    // The digits of a double's shortest text that reads back as the double: the default format of double, which .NET Core 3.0 and
    // later make the shortest round-trippable text ("Standard numeric format strings", the "G" and "R" specifiers), written into a
    // span with no string.
    private static int DigitsOf(double magnitude, Span<char> digits, out int point)
    {
        Span<char> text = stackalloc char[TextCapacity];
        _ = magnitude.TryFormat(text, out int written, default, CultureInfo.InvariantCulture);
        return Normalize(text[..written], digits, out point);
    }

    // The significant digits of an invariant number text ("1234.500", "1.5E+20", "1E-05"), without leading or trailing zeros, and
    // the point: the number is 0.d1d2... × 10^point. Zero has no digit.
    private static int Normalize(ReadOnlySpan<char> text, Span<char> digits, out int point)
    {
        int length = 0;
        point = 0;
        bool fraction = false;
        int exponent = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character == '.')
            {
                fraction = true;
            }
            else if (character == 'E')
            {
                exponent = int.Parse(text[(i + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
            else if (length == 0 && character == '0')
            {
                point -= fraction ? 1 : 0;
            }
            else
            {
                digits[length++] = character;
                point += fraction ? 0 : 1;
            }
        }

        while (length > 0 && digits[length - 1] == '0')
        {
            length--;
        }

        point = length == 0 ? 0 : point + exponent;
        return length;
    }

    // Rounds digits half to even at a number of fraction digits, as decimal.Round rounds a decimal's, and drops the trailing zeros.
    private static int RoundHalfEven(Span<char> digits, int length, ref int point, int fractionDigits)
    {
        if (length - point <= fractionDigits)
        {
            return length;
        }

        int keep = point + fractionDigits;
        if (keep < 0)
        {
            point = 0;
            return 0;
        }

        // A dropped part over half rounds up; one of exactly half (a 5 and nothing after it) rounds to the even digit.
        bool up = digits[keep] > '5' || (digits[keep] == '5' && (keep + 1 < length || (keep > 0 && (digits[keep - 1] - '0') % 2 == 1)));
        length = keep;
        if (up)
        {
            int at = keep - 1;
            while (at >= 0 && digits[at] == '9')
            {
                at--;
            }

            if (at < 0)
            {
                // Every kept digit was 9, or none was kept: the next power of ten.
                digits[0] = '1';
                length = 1;
                point++;
            }
            else
            {
                digits[at]++;
                length = at + 1;
            }
        }

        while (length > 0 && digits[length - 1] == '0')
        {
            length--;
        }

        if (length == 0)
        {
            point = 0;
        }

        return length;
    }

    // The decimal of digits and a point, with at least a number of fraction digits as its scale (at most 28); false past the range
    // of decimal.
    private static bool TryDecimal(ReadOnlySpan<char> digits, int point, int minimumFractionDigits, bool negative, out decimal value)
    {
        int integerDigits = Math.Max(point, 0);
        if (integerDigits > 29)
        {
            value = 0;
            return false;
        }

        int fraction = Math.Min(Math.Max(Math.Max(digits.Length - point, 0), minimumFractionDigits), 28);
        Span<char> text = stackalloc char[64];
        int at = 0;
        if (negative)
        {
            text[at++] = '-';
        }

        if (integerDigits == 0)
        {
            text[at++] = '0';
        }

        for (int i = 0; i < integerDigits; i++)
        {
            text[at++] = i < digits.Length ? digits[i] : '0';
        }

        if (fraction > 0)
        {
            text[at++] = '.';
            for (int f = 0; f < fraction; f++)
            {
                int i = point + f;
                text[at++] = i >= 0 && i < digits.Length ? digits[i] : '0';
            }
        }

        return decimal.TryParse(text[..at], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryWriteSymbol(string symbol, in NumberAffixes affixes, Span<char> destination, out int charsWritten)
    {
        var writer = new CharWriter(destination);
        writer.Append(affixes.Prefix);
        writer.Append(symbol);
        writer.Append(affixes.Suffix);
        return writer.TryFinish(out charsWritten);
    }

    // Writes the affixes and the digits: the integer digits padded to the fewest and grouped, then the fraction digits padded to the
    // fewest, in the locale's digits.
    private bool TryWrite(
        ReadOnlySpan<char> digits,
        int point,
        in NumberAffixes affixes,
        int minimumGrouping,
        Span<char> destination,
        out int charsWritten
    )
    {
        var writer = new CharWriter(destination);
        writer.Append(affixes.Prefix);
        if (affixes.SpaceAfterPrefix)
        {
            writer.Append(NoBreakSpace);
        }

        int integerDigits = Math.Max(point, 0);
        int total = Math.Max(integerDigits, _minimumIntegerDigits);
        int padding = total - integerDigits;
        bool grouped = _primaryGrouping > 0 && total >= _primaryGrouping + minimumGrouping;
        for (int j = 0; j < total; j++)
        {
            if (grouped && j > 0 && IsGroupStart(total - j))
            {
                writer.Append(_groupSeparator);
            }

            int i = j - padding;
            writer.Append(Digit(i >= 0 && i < digits.Length ? digits[i] : '0'));
        }

        int fraction = Math.Max(Math.Max(digits.Length - point, 0), _minimumFractionDigits);
        if (fraction > 0)
        {
            writer.Append(_decimalSeparator);
            for (int f = 0; f < fraction; f++)
            {
                int i = point + f;
                writer.Append(Digit(i >= 0 && i < digits.Length ? digits[i] : '0'));
            }
        }

        if (affixes.SpaceBeforeSuffix)
        {
            writer.Append(NoBreakSpace);
        }

        writer.Append(affixes.Suffix);
        return writer.TryFinish(out charsWritten);
    }

    // Whether a group separator goes before the integer digit with this many digits from it to the decimal separator, itself
    // included: after the primary group, and after each secondary group left of it.
    private bool IsGroupStart(int digitsToTheRight) =>
        digitsToTheRight == _primaryGrouping
        || (digitsToTheRight > _primaryGrouping && (digitsToTheRight - _primaryGrouping) % _secondaryGrouping == 0);

    private char Digit(char ascii) => (char)(_symbols.Zero + (ascii - '0'));

    private bool TryWriteCompact(Span<char> digits, int length, int point, bool negative, Span<char> destination, out int charsWritten)
    {
        length = RoundCompact(digits, length, ref point, out int type, out _, out bool compacted);
        NumberAffixes affixes = negative ? _negative : _positive;
        if (compacted)
        {
            CompactPattern pattern = Select(type, digits[..length], point);
            if (!pattern.Uncompacted)
            {
                affixes = negative ? pattern.Pattern.Negative : pattern.Pattern.Positive;
                if (!pattern.Pattern.HasNumber)
                {
                    return TryWriteSymbol(string.Empty, affixes, destination, out charsWritten);
                }
            }
        }

        return TryWrite(digits[..length], point, affixes, _minimumGrouping, destination, out charsWritten);
    }

    // Rounds digits, in place, as the compact format shows them: the type (the power of ten whose patterns apply, at most the
    // largest), the exponent of the power of ten the shown number is divided by, and whether a pattern compacts it. The shown number
    // keeps two significant digits, or all its integer digits when it has two or more, as ICU's compact notation rounds.
    private int RoundCompact(Span<char> digits, int length, ref int point, out int type, out int divisor, out bool compacted)
    {
        Span<char> source = stackalloc char[DigitCapacity];
        digits[..length].CopyTo(source);
        int sourcePoint = point;
        int exponent = length == 0 ? 0 : point - 1;
        for (int attempt = 0; ; attempt++)
        {
            type = Math.Min(exponent, _compact[^1].Exponent);
            CompactPattern? other = Find(type, (int)PluralCategory.Other);
            compacted = other is { Uncompacted: false };
            divisor = compacted ? type - other!.Pattern.IntegerZeros + 1 : 0;
            source[..length].CopyTo(digits);
            point = sourcePoint - divisor;
            int fraction = Math.Max(0, 2 - point);
            int shown = RoundHalfEven(digits, length, ref point, fraction);
            if (attempt > 0 || shown == 0 || point + divisor - 1 <= exponent)
            {
                return shown;
            }

            // Rounding carried into the next power of ten (999,999 to 1000K): the next type's patterns show it (1M).
            exponent++;
        }
    }

    // The pattern for a shown number: the one for 1 alone when the shown number is the whole number 1 and the locale has one, else
    // the one of the plural category of the shown number with the compact exponent c = 0 (1.5 for 1.5K, 2 for 2M), else the one of
    // other. ICU's CompactHandler::processQuantity selects with the scaled number and sets the exponent only after (Issue #906, after
    // PR #944's review).
    private CompactPattern Select(int type, ReadOnlySpan<char> shown, int point)
    {
        if (shown.Length == 1 && shown[0] == '1' && point == 1 && Find(type, CompactPattern.ExplicitOne) is { } one)
        {
            return one;
        }

        PluralCategory category = _plurals!.Select(PluralValue(shown, point));
        return Find(type, (int)category) ?? Find(type, (int)PluralCategory.Other)!;
    }

    private CompactPattern? Find(int type, int category)
    {
        foreach (CompactPattern pattern in _compact)
        {
            if (pattern.Exponent == type && pattern.Category == category)
            {
                return pattern;
            }
        }

        return null;
    }

    // The shown number of a compact form (1.5 for 1.5K), with the scale of its fraction digits. One past the range of decimal (a
    // double whose shown number has more than 29 integer digits at the largest pattern, such as 1.2345e50 in English's 000T) is
    // replaced by a number of 28 digits with the same last seven: CLDR's plural rules read no more of a large whole number than
    // i % 1000000.
    private static decimal PluralValue(ReadOnlySpan<char> digits, int point)
    {
        if (TryDecimal(digits, point, 0, false, out decimal value))
        {
            return value;
        }

        Span<char> text = stackalloc char[28];
        for (int k = 0; k < text.Length; k++)
        {
            int i = point - text.Length + k;
            text[k] =
                k == 0 ? '1'
                : k >= 21 && i < digits.Length ? digits[i]
                : '0';
        }

        return decimal.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private bool TryParseNumber<T>(ReadOnlySpan<char> text, out T value)
        where T : INumberBase<T>
    {
        if (_kind is NumberKind.Currency or NumberKind.Compact)
        {
            ThrowHelper.ThrowNumberParseNotSupported();
        }

        // The first half holds the text without its bidi marks, the second the ASCII text; each is at most as long as the text.
        char[]? rented = null;
        Span<char> buffer =
            text.Length < ParseCapacity ? stackalloc char[2 * ParseCapacity] : (rented = ArrayPool<char>.Shared.Rent(2 * (text.Length + 1)));
        try
        {
            Span<char> plain = buffer[..(buffer.Length / 2)];
            Span<char> ascii = buffer[(buffer.Length / 2)..];
            int plainLength = WithoutBidiMarks(text, plain);

            // double.TryParse gives true and an infinity for digits past the range of double (PR #944's review: a 1 and 400 zeros);
            // the design reads no infinity.
            if (
                TryAscii(plain[..plainLength], ascii, out int length)
                && T.TryParse(
                    ascii[..length],
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out T? parsed
                )
                && T.IsFinite(parsed!)
            )
            {
                value = parsed!;
                if (_kind == NumberKind.Percent)
                {
                    value /= T.CreateTruncating(100);
                }

                return true;
            }

            value = T.Zero;
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    // Writes the text as the invariant parser reads it: a minus sign, ASCII digits, and a point. False for text the format does not
    // read (design §4.11, "Parsing").
    private bool TryAscii(ReadOnlySpan<char> text, Span<char> ascii, out int length)
    {
        length = 0;
        text = text.Trim();
        int at = 0;
        int minus = MinusLength(text);
        if (minus > 0)
        {
            ascii[length++] = '-';
            at += minus;
        }
        else if (StartsWith(text, at, _parsePlus))
        {
            at += _parsePlus.Length;
        }

        bool percent = _kind == NumberKind.Percent;
        bool percentRead = false;
        if (percent && StartsWith(text, at, _parsePercent))
        {
            percentRead = true;
            at = SkipWhiteSpace(text, at + _parsePercent.Length);
        }

        bool anyDigit = false;
        bool fraction = false;
        while (at < text.Length)
        {
            int digit = DigitValue(text[at]);
            if (digit >= 0)
            {
                ascii[length++] = (char)('0' + digit);
                anyDigit = true;
                at++;
                continue;
            }

            int group = fraction || at == 0 || DigitValue(text[at - 1]) < 0 ? 0 : GroupLength(text, at);
            if (group > 0 && at + group < text.Length && DigitValue(text[at + group]) >= 0)
            {
                at += group;
                continue;
            }

            if (!fraction && StartsWith(text, at, _decimalSeparator))
            {
                ascii[length++] = '.';
                fraction = true;
                at += _decimalSeparator.Length;
                continue;
            }

            break;
        }

        if (percent && !percentRead && anyDigit)
        {
            int after = SkipWhiteSpace(text, at);
            if (StartsWith(text, after, _parsePercent))
            {
                at = after + _parsePercent.Length;
            }
        }

        return anyDigit && at == text.Length;
    }

    // The length of the minus sign at the start: the locale's, without its bidi marks, or an ASCII hyphen-minus where the locale's
    // is U+2212, which a user types as a hyphen-minus (Issue #906).
    private int MinusLength(ReadOnlySpan<char> text)
    {
        if (StartsWith(text, 0, _parseMinus))
        {
            return _parseMinus.Length;
        }

        return _parseMinus is [MinusSign] && text.Length > 0 && text[0] == '-' ? 1 : 0;
    }

    // The text without the bidi marks CLDR puts in symbols (U+200E, U+200F, U+061C), which parsing skips: UTS #35 part 3 says that
    // lenient parsing should ignore bidi format characters.
    private static int WithoutBidiMarks(ReadOnlySpan<char> text, Span<char> destination)
    {
        int length = 0;
        foreach (char character in text)
        {
            if (!IsBidiMark(character))
            {
                destination[length++] = character;
            }
        }

        return length;
    }

    private static string WithoutBidiMarks(string text)
    {
        Span<char> buffer = text.Length <= 64 ? stackalloc char[64] : new char[text.Length];
        return new string(buffer[..WithoutBidiMarks(text, buffer)]);
    }

    private static bool IsBidiMark(char character) => character is LeftToRightMark or RightToLeftMark or ArabicLetterMark;

    // The value of a locale's digit, an ASCII digit, or a full-width digit (U+FF10 to U+FF19); -1 for another character.
    private int DigitValue(char character)
    {
        if (char.IsAsciiDigit(character))
        {
            return character - '0';
        }

        if (character is >= FullWidthZero and <= FullWidthNine)
        {
            return character - FullWidthZero;
        }

        int offset = character - _symbols.Zero;
        return offset is >= 0 and <= 9 ? offset : -1;
    }

    // The length of the group separator at a position, or 0. Where the separator is U+00A0 or U+202F, U+0020 and the other of the two
    // stand for it: a user types a space, and text pasted from elsewhere may have either no-break space (fr groups with U+202F, ru and
    // pl with U+00A0), and none of the three means anything else in a number (PR #944's review).
    private int GroupLength(ReadOnlySpan<char> text, int at)
    {
        if (StartsWith(text, at, _groupSeparator))
        {
            return _groupSeparator.Length;
        }

        bool spaceSeparator = _groupSeparator is [NoBreakSpace] or [NarrowNoBreakSpace];
        return spaceSeparator && text[at] is ' ' or NoBreakSpace or NarrowNoBreakSpace ? 1 : 0;
    }

    private static bool StartsWith(ReadOnlySpan<char> text, int at, string symbol) =>
        symbol.Length > 0 && at <= text.Length && text[at..].StartsWith(symbol, StringComparison.Ordinal);

    private static int SkipWhiteSpace(ReadOnlySpan<char> text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        return at;
    }

    // Writes into a span and remembers whether anything did not fit.
    private ref struct CharWriter
    {
        private readonly Span<char> _destination;
        private int _length;
        private bool _full;

        public CharWriter(Span<char> destination)
        {
            _destination = destination;
        }

        public void Append(char value)
        {
            if (_length < _destination.Length)
            {
                _destination[_length++] = value;
            }
            else
            {
                _full = true;
            }
        }

        public void Append(ReadOnlySpan<char> value)
        {
            if (value.TryCopyTo(_destination[_length..]))
            {
                _length += value.Length;
            }
            else
            {
                _full = true;
            }
        }

        public readonly bool TryFinish(out int charsWritten)
        {
            charsWritten = _full ? 0 : _length;
            return !_full;
        }
    }
}
