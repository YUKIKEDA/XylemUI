using System.Globalization;
using System.Text;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The number tables of the entries (design §4.11, "Numbers"): for each locale, the symbols and the standard patterns of its default
/// numbering system, its compact decimal patterns, and the currency symbols that differ from the currency's code; and the currencies'
/// fraction digits. Locales whose bytes are the same share one table. The bytes are read by <c>NumberSymbols</c>,
/// <c>CompactPattern</c>, and <c>NumberFormatter</c> in XylemUI.Core.
/// </summary>
/// <remarks>
/// A number table is the digit zero (2 bytes, little-endian), <c>minimumGroupingDigits</c> (1 byte), and the strings: the decimal and
/// group separators, the currency's decimal and group separators (the plain ones when the locale has none of its own), the minus,
/// plus, and percent signs, infinity, not a number, the decimal, percent, and currency patterns, and the currency pattern for a
/// symbol next to the digits that ends in a letter (<c>standard-alphaNextToNumber</c>, empty when the locale has none: 13 strings
/// in all). A string is a length byte and
/// that many bytes of UTF-8. A compact table is, for the short and then the long width, a count byte and the patterns, each an
/// exponent byte (3 for 1000), a category byte (0 to 5 for zero to other, 6 for the explicit 1), and the pattern, ordered by exponent
/// and category. A currency table is, for each currency in ordinal order of its code whose standard or narrow symbol differs from the
/// code, the code (3 ASCII bytes), the standard symbol (empty when it is the code), and the narrow symbol (empty when it is the
/// standard one). A symbol's length byte holds its UTF-8 length in its 6 low bits (at most 63) and, in its 2 high bits, whether the
/// grapheme cluster next to the digits starts with a letter: bit 7 for its last cluster (the symbol before the digits) and bit 6 for
/// its first (the symbol after them). An empty symbol is the byte 0.
/// </remarks>
internal sealed class NumberTables
{
    // The bits of a currency symbol's length byte: the last grapheme cluster starts with a letter, the first one does, and the
    // UTF-8 length (Issue #906).
    private const int LastClusterIsLetter = 0x80;

    private const int FirstClusterIsLetter = 0x40;

    private const int SymbolLengthMask = 0x3F;

    private static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other", "1"];

    private static readonly char[] NumberCharacters = ['#', '0', ',', '.'];

    private NumberTables(
        List<byte[]> numbers,
        List<byte[]> compact,
        List<byte[]> currencies,
        Dictionary<string, (int Numbers, int Compact, int Currencies)> byTag,
        byte[] currencyDigits,
        int defaultCurrencyDigits
    )
    {
        Numbers = numbers;
        Compact = compact;
        Currencies = currencies;
        ByTag = byTag;
        CurrencyDigits = currencyDigits;
        DefaultCurrencyDigits = defaultCurrencyDigits;
    }

    /// <summary>
    /// Gets the distinct number tables, in the order of their first use (root first, then the entries in the order of their tags).
    /// </summary>
    public IReadOnlyList<byte[]> Numbers { get; }

    /// <summary>
    /// Gets the distinct compact tables, in the same order.
    /// </summary>
    public IReadOnlyList<byte[]> Compact { get; }

    /// <summary>
    /// Gets the distinct currency tables, in the same order.
    /// </summary>
    public IReadOnlyList<byte[]> Currencies { get; }

    /// <summary>
    /// Gets, for the data tag of each entry and of root, the index of its number, compact, and currency tables.
    /// </summary>
    public IReadOnlyDictionary<string, (int Numbers, int Compact, int Currencies)> ByTag { get; }

    /// <summary>
    /// Gets the fraction digits of the currencies whose digits are not the default: a code (3 ASCII bytes) and the digits (1 byte)
    /// each, in ordinal order of the codes.
    /// </summary>
    public IReadOnlyList<byte> CurrencyDigits { get; }

    /// <summary>
    /// Gets the fraction digits of a currency that <see cref="CurrencyDigits"/> does not list (CLDR's <c>DEFAULT</c>).
    /// </summary>
    public int DefaultCurrencyDigits { get; }

    /// <summary>
    /// Reads the tables of every entry and of root.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <param name="locales">The set of locales.</param>
    /// <returns>The tables.</returns>
    public static NumberTables Read(CldrZip cldr, LocaleSet locales)
    {
        JsonElement systems = cldr.Supplemental("numberingSystems").GetProperty("numberingSystems");
        var numbers = new List<byte[]>();
        var compact = new List<byte[]>();
        var currencies = new List<byte[]>();
        var numberIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var compactIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var currencyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        foreach (LocaleEntry entry in locales.Entries.Prepend(locales.Root))
        {
            string tag = entry.DataTag;
            if (byTag.ContainsKey(tag))
            {
                continue;
            }

            JsonElement data = NumbersOf(cldr, tag);
            string system = data.GetProperty("defaultNumberingSystem").GetString() ?? string.Empty;
            byte[] numberTable = NumberTable(tag, data, system, systems);
            byte[] compactTable = CompactTable(tag, data, system);
            byte[] currencyTable = CurrencyTable(tag, cldr);
            int numberAt = Index(numberTable, numbers, numberIndex);
            int compactAt = Index(compactTable, compact, compactIndex);
            int currencyAt = Index(currencyTable, currencies, currencyIndex);
            byTag.Add(tag, (numberAt, compactAt, currencyAt));
        }

        (byte[] digits, int defaultDigits) = Digits(cldr);
        return new NumberTables(numbers, compact, currencies, byTag, digits, defaultDigits);
    }

    /// <summary>
    /// Returns the digit zero of a locale's default numbering system, which the date tables also write their digits from (design
    /// §4.11, "Default digits").
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <param name="tag">The locale's tag, as cldr-json names its folder.</param>
    /// <returns>The zero; the digits one to nine are the next UTF-16 code units.</returns>
    public static char Zero(CldrZip cldr, string tag)
    {
        string system = NumbersOf(cldr, tag).GetProperty("defaultNumberingSystem").GetString() ?? string.Empty;
        return Digits(tag, system, cldr.Supplemental("numberingSystems").GetProperty("numberingSystems"))[0];
    }

    private static int Index(byte[] table, List<byte[]> tables, Dictionary<string, int> index) => TableBytes.Index(table, tables, index);

    private static JsonElement NumbersOf(CldrZip cldr, string tag) =>
        cldr.Json("cldr-numbers-full/main/" + tag + "/numbers.json").GetProperty("main").GetProperty(tag).GetProperty("numbers");

    // The ten digits of a numbering system. The formats write a digit as the zero plus its value, so the ten digits must be
    // consecutive UTF-16 code units.
    private static string Digits(string tag, string system, JsonElement systems)
    {
        if (!systems.TryGetProperty(system, out JsonElement numbering) || numbering.GetProperty("_type").GetString() != "numeric")
        {
            throw new InvalidDataException("The default numbering system " + system + " of " + tag + " is not a numeric one.");
        }

        string digits = numbering.GetProperty("_digits").GetString() ?? string.Empty;
        if (digits.Length != 10 || Enumerable.Range(0, 10).Any(i => digits[i] != digits[0] + i))
        {
            throw new InvalidDataException("The digits of " + system + " are not ten consecutive UTF-16 code units.");
        }

        return digits;
    }

    private static byte[] NumberTable(string tag, JsonElement data, string system, JsonElement systems)
    {
        string digits = Digits(tag, system, systems);
        JsonElement symbols = data.GetProperty("symbols-numberSystem-" + system);
        CheckCurrencySpacing(tag, data.GetProperty("currencyFormats-numberSystem-" + system).GetProperty("currencySpacing"));
        var table = new List<byte> { (byte)digits[0], (byte)(digits[0] >> 8), (byte)Integer(tag, data.GetProperty("minimumGroupingDigits")) };
        string decimalSeparator = Text(tag, symbols, "decimal");
        string groupSeparator = Text(tag, symbols, "group");
        string currencyDecimal = symbols.TryGetProperty("currencyDecimal", out _) ? Text(tag, symbols, "currencyDecimal") : decimalSeparator;
        string currencyGroup = symbols.TryGetProperty("currencyGroup", out _) ? Text(tag, symbols, "currencyGroup") : groupSeparator;
        Add(table, decimalSeparator);
        Add(table, groupSeparator);
        Add(table, currencyDecimal);
        Add(table, currencyGroup);
        foreach (string name in new[] { "minusSign", "plusSign", "percentSign", "infinity", "nan" })
        {
            Add(table, Text(tag, symbols, name));
        }

        foreach (string kind in new[] { "decimalFormats", "percentFormats", "currencyFormats" })
        {
            string pattern = Text(tag, data.GetProperty(kind + "-numberSystem-" + system), "standard");
            CheckPattern(tag, pattern, compact: false);
            Add(table, pattern);
        }

        // The currency pattern for a symbol whose character next to the digits is a letter; empty when the locale has none, and the
        // runtime then applies currencySpacing (Issue #906).
        JsonElement currencyFormats = data.GetProperty("currencyFormats-numberSystem-" + system);
        string alpha = currencyFormats.TryGetProperty("standard-alphaNextToNumber", out _)
            ? Text(tag, currencyFormats, "standard-alphaNextToNumber")
            : string.Empty;
        if (alpha.Length > 0)
        {
            CheckPattern(tag, alpha, compact: false);
        }

        Add(table, alpha);
        return [.. table];
    }

    private static byte[] CompactTable(string tag, JsonElement data, string system)
    {
        JsonElement formats = data.GetProperty("decimalFormats-numberSystem-" + system);
        var table = new List<byte>();
        foreach (string width in new[] { "short", "long" })
        {
            // A locale with no long patterns takes its short ones (UTS #35 part 3, "Compact Number Formats").
            JsonElement patterns = (formats.TryGetProperty(width, out JsonElement style) ? style : formats.GetProperty("short")).GetProperty(
                "decimalFormat"
            );
            var entries = new List<(int Exponent, int Category, string Pattern)>();
            foreach (JsonProperty property in patterns.EnumerateObject())
            {
                entries.Add(CompactEntry(tag, property));
            }

            entries.Sort((left, right) => (left.Exponent, left.Category).CompareTo((right.Exponent, right.Category)));
            int first = entries.Count == 0 ? 0 : entries[0].Exponent;
            int last = entries.Count == 0 ? -1 : entries[^1].Exponent;
            for (int exponent = first; exponent <= last; exponent++)
            {
                if (!entries.Any(entry => entry.Exponent == exponent && entry.Category == 5))
                {
                    string power = "10^" + Output.Number(exponent);
                    throw new InvalidDataException("The " + width + " compact patterns of " + tag + " have no other pattern for " + power + ".");
                }
            }

            if (entries.Count is 0 or > 255)
            {
                throw new InvalidDataException("The " + width + " compact patterns of " + tag + " are " + Output.Number(entries.Count) + ".");
            }

            table.Add((byte)entries.Count);
            foreach ((int exponent, int category, string pattern) in entries)
            {
                table.Add((byte)exponent);
                table.Add((byte)category);
                Add(table, pattern);
            }
        }

        return [.. table];
    }

    // A key of a compact pattern is a power of ten and a count: "10000-count-one", or "1000-count-1" for the explicit 1.
    private static (int Exponent, int Category, string Pattern) CompactEntry(string tag, JsonProperty property)
    {
        string key = property.Name;
        int dash = key.IndexOf("-count-", StringComparison.Ordinal);
        string power = dash < 0 ? string.Empty : key[..dash];
        int category = dash < 0 ? -1 : Array.IndexOf(Categories, key[(dash + 7)..]);
        if (power.Length is < 4 or > 255 || power[0] != '1' || power[1..].Any(digit => digit != '0') || category < 0)
        {
            throw new InvalidDataException("The compact pattern key " + key + " of " + tag + " is not a power of ten and a count.");
        }

        string pattern = property.Value.GetString() ?? string.Empty;
        CheckPattern(tag, pattern, compact: true);
        return (power.Length - 1, category, pattern);
    }

    private static byte[] CurrencyTable(string tag, CldrZip cldr)
    {
        JsonElement currencies = cldr.Json("cldr-numbers-full/main/" + tag + "/currencies.json")
            .GetProperty("main")
            .GetProperty(tag)
            .GetProperty("numbers")
            .GetProperty("currencies");
        var codes = currencies.EnumerateObject().Select(property => property.Name).ToList();
        codes.Sort(StringComparer.Ordinal);
        var table = new List<byte>();
        foreach (string code in codes)
        {
            if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            {
                throw new InvalidDataException("The currency code " + code + " of " + tag + " is not three upper-case ASCII letters.");
            }

            JsonElement currency = currencies.GetProperty(code);
            string? symbol = currency.TryGetProperty("symbol", out JsonElement value) ? value.GetString() : null;
            string? narrow = currency.TryGetProperty("symbol-alt-narrow", out value) ? value.GetString() : null;
            if (symbol == string.Empty || narrow == string.Empty)
            {
                throw new InvalidDataException("A symbol of " + code + " in " + tag + " is empty.");
            }

            string standard = symbol ?? code;
            if (standard == code && (narrow is null || narrow == standard))
            {
                continue;
            }

            table.AddRange(Encoding.ASCII.GetBytes(code));
            AddSymbol(table, tag, code, standard == code ? string.Empty : standard);
            AddSymbol(table, tag, code, narrow is null || narrow == standard ? string.Empty : narrow);
        }

        return [.. table];
    }

    private static (byte[] Digits, int Default) Digits(CldrZip cldr)
    {
        JsonElement fractions = cldr.Supplemental("currencyData").GetProperty("currencyData").GetProperty("fractions");
        int defaultDigits = -1;
        var digits = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonProperty property in fractions.EnumerateObject())
        {
            // Only the digits are read: a rounding increment (other than 0) would change how a currency rounds, so it stops the tool.
            if (Integer(property.Name, property.Value.GetProperty("_rounding")) != 0)
            {
                throw new InvalidDataException("The currency " + property.Name + " has a rounding increment, which the formats do not apply.");
            }

            int count = Integer(property.Name, property.Value.GetProperty("_digits"));
            if (property.Name == "DEFAULT")
            {
                defaultDigits = count;
            }
            else
            {
                digits.Add(property.Name, count);
            }
        }

        if (defaultDigits < 0)
        {
            throw new InvalidDataException("currencyData.json has no DEFAULT fraction digits.");
        }

        var table = new List<byte>();
        foreach ((string code, int count) in digits)
        {
            if (count == defaultDigits)
            {
                continue;
            }

            if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
            {
                throw new InvalidDataException("The currency code " + code + " of currencyData.json is not three upper-case ASCII letters.");
            }

            table.AddRange(Encoding.ASCII.GetBytes(code));
            table.Add((byte)count);
        }

        return ([.. table], defaultDigits);
    }

    // The runtime writes CLDR's currencySpacing, for a locale with no alphaNextToNumber pattern, as one rule, so a locale with
    // another one stops the tool.
    private static void CheckCurrencySpacing(string tag, JsonElement spacing)
    {
        foreach (string side in new[] { "beforeCurrency", "afterCurrency" })
        {
            JsonElement rule = spacing.GetProperty(side);
            if (
                rule.GetProperty("currencyMatch").GetString() != "[[:^S:]&[:^Z:]]"
                || rule.GetProperty("surroundingMatch").GetString() != "[:digit:]"
                || rule.GetProperty("insertBetween").GetString() is not [(char)0x00A0]
            )
            {
                throw new InvalidDataException("The " + side + " currency spacing of " + tag + " is not the one the formats write.");
            }
        }
    }

    // A pattern the runtime reads: a prefix, digits, a suffix, and an optional negative subpattern after ';'. A standard pattern's
    // digits are '#', '0', ',', and '.', and its affixes have no letters or digits outside quotes; a compact pattern's digits are
    // zeros, or none, and its affixes may have letters. Anything else (an exponent, a significant digit '@', a padding '*', a
    // per mille sign) stops the tool.
    private static void CheckPattern(string tag, string pattern, bool compact)
    {
        foreach (string part in Unquoted(pattern).Split(';'))
        {
            int start = part.IndexOfAny(NumberCharacters);
            int end = start < 0 ? start : part.LastIndexOfAny(NumberCharacters) + 1;
            string number = start < 0 ? string.Empty : part[start..end];
            string affixes = start < 0 ? part : part[..start] + part[end..];
            bool valid = compact
                ? number.All(digit => digit == '0')
                    && !affixes.Any(character => char.IsAsciiDigit(character) || "#@*.,‰%¤".Contains(character, StringComparison.Ordinal))
                : number.Contains('0', StringComparison.Ordinal)
                    && number.All(character => Array.IndexOf(NumberCharacters, character) >= 0)
                    && number.Count(character => character == '.') <= 1
                    && !number.SkipWhile(character => character != '.').Any(character => character == ',')
                    && !affixes.Any(character => char.IsAsciiLetterOrDigit(character) || "#@*.,‰".Contains(character, StringComparison.Ordinal));
            if (!valid)
            {
                throw new InvalidDataException("The pattern " + pattern + " of " + tag + " has a form the formats do not read.");
            }
        }
    }

    // The pattern with each quoted part replaced by a space, so that its characters are not taken for special ones.
    private static string Unquoted(string pattern)
    {
        var text = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\'')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                {
                    text.Append(' ');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else
            {
                text.Append(quoted ? ' ' : pattern[i]);
            }
        }

        return text.ToString();
    }

    private static string Text(string tag, JsonElement element, string name) => TableBytes.Text(tag, element, name);

    private static int Integer(string tag, JsonElement element) =>
        int.TryParse(element.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value <= 255
            ? value
            : throw new InvalidDataException("A count of " + tag + " is not a number from 0 to 255.");

    private static void Add(List<byte> table, string text) => TableBytes.Add(table, text);

    // A currency symbol: its UTF-8 length and the two letter bits in one byte, and its bytes. The tool judges the clusters, so the
    // runtime carries no grapheme break tables: judging them there pulled about 14 KB of .NET's into every NativeAOT app that
    // formats a currency (Issue #906).
    private static void AddSymbol(List<byte> table, string tag, string code, string symbol)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(symbol);
        if (bytes.Length > SymbolLengthMask)
        {
            throw new InvalidDataException("The symbol " + symbol + " of " + code + " in " + tag + " is longer than 63 bytes of UTF-8.");
        }

        int letters = 0;
        if (symbol.Length > 0)
        {
            letters |= StartsWithLetter(symbol, LastCluster(symbol)) ? LastClusterIsLetter : 0;
            letters |= StartsWithLetter(symbol, 0) ? FirstClusterIsLetter : 0;
        }

        table.Add((byte)(bytes.Length | letters));
        table.AddRange(bytes);
    }

    // The index of a symbol's last extended grapheme cluster (UAX #29): Kannada's ಲೀ, U+0CB2 and the vowel sign U+0CC0, is one
    // cluster that starts at 0.
    private static int LastCluster(string symbol)
    {
        int last = 0;
        for (int at = 0; at < symbol.Length; at += StringInfo.GetNextTextElementLength(symbol, at))
        {
            last = at;
        }

        return last;
    }

    // Whether the code point at an index has General Category L, which UTS #35 part 3 judges for the alphaNextToNumber pattern.
    private static bool StartsWithLetter(string symbol, int index)
    {
        _ = Rune.DecodeFromUtf16(symbol.AsSpan(index), out Rune rune, out _);
        return Rune.IsLetter(rune);
    }
}
