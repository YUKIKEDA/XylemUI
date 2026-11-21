using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace XylemUI.Tests;

// The generator of the CLDR tables, tools/CldrTableGen (LC2, design §4.11, "How the tables are made" and "Tests"), run on a small
// zip of the json-full shape that the tests make: two runs write the same bytes, a locale takes the plural rules of its plural parent
// or of its truncated tag, a zip whose SHA-256 is not the lock's stops it, and so does a language of row LC4 with neither data nor
// an alias; the number tables and the date tables hold the bytes of the json, and a skeleton or a pattern the formats cannot take stops
// it. The tables it wrote from the real zip are checked in LocaleDataTests, PluralRulesTests, NumberFormatterTests,
// DateFormatterTests, and DateNamesTests.
public sealed class CldrTableGenTests
{
    // The characters of the number data that are not ASCII, written by code so that the source shows them.
    private const char NoBreakSpace = (char)0x00A0;
    private const char Infinity = (char)0x221E;
    private const char CurrencySign = (char)0x00A4;
    private const char YenSign = (char)0x00A5;

    // currencyData.json: BHD has 3 fraction digits, JPY 0, CHF the default 2 with a cash rounding the tool does not read.
    private const string CurrencyData =
        "{ \"supplemental\": { \"currencyData\": { \"fractions\": { \"BHD\": { \"_rounding\": \"0\", \"_digits\": \"3\" }, "
        + "\"CHF\": { \"_rounding\": \"0\", \"_digits\": \"2\", \"_cashRounding\": \"5\" }, "
        + "\"DEFAULT\": { \"_rounding\": \"0\", \"_digits\": \"2\" }, \"JPY\": { \"_rounding\": \"0\", \"_digits\": \"0\" } } } } }";

    // The lines of CldrNumberTables.g.cs and of CldrData that the zip of these tests gives (Tool_NumberTables_HoldTheBytesOfTheData),
    // with the bytes counted by hand from the strings of NumbersJson, CurrenciesJson, and CurrencyData: 48, 0 is the digit zero, 1
    // the minimum grouping digits, then each string's UTF-8 length and bytes (infinity is E2 88 9E, the currency sign C2 A4, the
    // yen sign C2 A5, the no-break space C2 A0). The last string is the alphaNextToNumber pattern, empty (0) for de. The long
    // compact patterns are ordered by category: one (1), other (5), the explicit 1 (6).
    private static readonly string[] NumberLines =
    [
        "    public const int NumberTableCount = 2;",
        "    public const int CompactTableCount = 1;",
        "    public const int CurrencyTableCount = 1;",
        "    public const int DefaultCurrencyDigits = 2;",
        "    public static ReadOnlySpan<byte> CurrencyDigits => [66, 72, 68, 3, 74, 80, 89, 0];",
        "    public static ReadOnlySpan<byte> Numbers0 => [48, 0, 1, 1, 46, 1, 44, 1, 46, 1, 44, 1, 45, 1, 43, 1, 37, 3, 226, 136, 158, 3, 78, 97, "
            + "78, 9, 35, 44, 35, 35, 48, 46, 35, 35, 35, 6, 35, 44, 35, 35, 48, 37, 10, 194, 164, 35, 44, 35, 35, 48, 46, 48, 48, 12, 194, 164, "
            + "194, 160, 35, 44, 35, 35, 48, 46, 48, 48];",
        "    public static ReadOnlySpan<byte> Numbers1 => [48, 0, 1, 1, 44, 1, 46, 1, 44, 1, 46, 1, 45, 1, 43, 1, 37, 3, 226, 136, 158, 3, 78, 97, "
            + "78, 9, 35, 44, 35, 35, 48, 46, 35, 35, 35, 6, 35, 44, 35, 35, 48, 37, 10, 194, 164, 35, 44, 35, 35, 48, 46, 48, 48, 0];",
        "    public static ReadOnlySpan<byte> Compact0 => [1, 3, 5, 2, 48, 75, 3, 3, 1, 10, 48, 32, 116, 104, 111, 117, 115, 97, 110, 100, 3, 5, "
            + "10, 48, 32, 116, 104, 111, 117, 115, 97, 110, 100, 3, 6, 10, 97, 32, 116, 104, 111, 117, 115, 97, 110, 100];",
        "    public static ReadOnlySpan<byte> Currencies0 => [74, 80, 89, 0, 2, 194, 165, 85, 83, 68, 1, 36, 0];",
    ];

    // The 82 languages of row LC4: Flutter 3.47.8's kMaterialSupportedLanguages
    // (packages/flutter_localizations/lib/src/l10n/generated_material_localizations.dart, lines 46518 to 46601).
    private static readonly string[] Lc4Languages = (
        "af am ar as az be bg bn bo bs ca cs cy da de el en es et eu fa fi fil fr ga gl gsw gu he hi hr hu hy id is it ja ka kk km kn ko ky "
        + "lo lt lv mk ml mn mr ms my nb ne nl no or pa pl ps pt ro ru si sk sl sq sr sv sw ta te th tl tr ug uk ur uz vi zh zu"
    ).Split(' ');

    // The available formats of en in the zip of these tests: enough for each of the 41 skeletons, with an alternative, a count, and a
    // day the generator does not read (MMMddd), and without E with EEEE, LLL, LLLL, MMMM, QQQ, QQQQ, hmz, hz, m, or s.
    private static readonly (string Key, string Pattern)[] EnFormats =
    [
        ("d", "d"),
        ("E", "ccc"),
        ("Ed", "d E"),
        ("H", "HH"),
        ("Hm", "HH:mm"),
        ("Hms", "HH:mm:ss"),
        ("h", "h a"),
        ("hm", "h:mm a"),
        ("hm-alt-ascii", "h:mm a"),
        ("hms", "h:mm:ss a"),
        ("hmv", "h:mm a v"),
        ("Hmv", "HH:mm v"),
        ("hv", "h a v"),
        ("Hv", "HH v"),
        ("M", "L"),
        ("Md", "M/d"),
        ("MEd", "E, M/d"),
        ("MMM", "LLL"),
        ("MMMd", "MMM d"),
        ("MMMddd", "MMM ddd"),
        ("MMMEd", "E, MMM d"),
        ("MMMMd", "MMMM d"),
        ("MMMMEd", "E, MMMM d"),
        ("ms", "mm:ss"),
        ("y", "y"),
        ("yM", "M/y"),
        ("yMd", "M/d/y"),
        ("yMEd", "E, M/d/y"),
        ("yMMM", "MMM y"),
        ("yMMMd", "MMM d, y"),
        ("yMMMEd", "E, MMM d, y"),
        ("yMMMM", "MMMM y"),
        ("yMMMMd", "MMMM d, y"),
        ("yMMMMEd", "E, MMMM d, y"),
        ("yQQQ", "QQQ y"),
        ("yQQQQ", "QQQQ y"),
        ("yw-count-one", "'week' w 'of' Y"),
    ];

    // ja's: en's with the hours and the month of Japanese, whose MMM is a number (M月).
    private static readonly (string Key, string Pattern)[] JaFormats =
    [
        .. EnFormats.Where(format => format.Key is not ("H" or "M" or "MMM")),
        ("H", "H時"),
        ("M", "M月"),
        ("MMM", "M月"),
    ];

    // zh's: en's with the 12-hour formats of the flexible day periods, as zh-Hant's (Bh:mm).
    private static readonly (string Key, string Pattern)[] ZhFormats =
    [
        .. EnFormats.Where(format => format.Key is not ("h" or "hm" or "hms" or "hmv")),
        ("h", "Bh時"),
        ("hm", "Bh:mm"),
        ("hms", "Bh:mm:ss"),
        ("hmv", "Bh:mm [v]"),
    ];

    // The patterns the generator finds for en's 41 skeletons (in the order of Flutter's validDateFormats), and its 24-hour patterns
    // of the 7 with j, worked out by hand with the rules of tools/CldrTableGen/SkeletonMatcher.cs.
    private static readonly string[] EnPatterns =
    [
        "d",
        "ccc",
        "cccc",
        "LLL",
        "LLLL",
        "L",
        "M/d",
        "E, M/d",
        "LLL",
        "MMM d",
        "E, MMM d",
        "LLLL",
        "MMMM d",
        "EEEE, MMMM d",
        "QQQ",
        "QQQQ",
        "y",
        "M/y",
        "M/d/y",
        "E, M/d/y",
        "MMM y",
        "MMM d, y",
        "E, MMM d, y",
        "MMMM y",
        "MMMM d, y",
        "EEEE, MMMM d, y",
        "QQQ y",
        "QQQQ y",
        "HH",
        "HH:mm",
        "HH:mm:ss",
        "h a",
        "h:mm a",
        "h:mm:ss a",
        "h:mm a v",
        "h:mm a z",
        "h a v",
        "h a z",
        "m",
        "mm:ss",
        "s",
        "HH",
        "HH:mm",
        "HH:mm:ss",
        "HH:mm v",
        "HH:mm z",
        "HH v",
        "HH z",
    ];

    // The lines of CldrLikelyScripts.g.cs that the zip of these tests gives (Tool_LikelyScripts_HoldTheKeysWithoutAScript).
    private static readonly string[] LikelyLines =
    [
        "    public const int SourceKeyCount = 6;",
        "    public const int KeysWithScript = 1;",
        "    public const int RegionKeysLeftOut = 1;",
        "    public const int EntryCount = 4;",
        "    public const int ScriptCount = 3;",
        "    public static ReadOnlySpan<byte> Scripts => [72, 97, 110, 115, 72, 97, 110, 116, 74, 112, 97, 110];",
        "    public static ReadOnlySpan<byte> Keys => [0, 0, 149, 28, 5, 2, 75, 61, 0, 0, 226, 74, 5, 2, 226, 74];",
        "    public static ReadOnlySpan<byte> Values => [2, 1, 0, 1];",
    ];

    /// <summary>
    /// Gets the cases of Tool_NumberDataTheFormatsDoNotRead_Stops: the entry of the zip, the text replaced in it, the text put in its
    /// place, and the message the tool writes.
    /// </summary>
    public static TheoryData<string, string, string, string> UnreadNumberData
    {
        get
        {
            const string Systems = "cldr-core/supplemental/numberingSystems.json";
            const string Fractions = "cldr-core/supplemental/currencyData.json";
            const string Numbers = "cldr-numbers-full/main/de/numbers.json";
            const string Currencies = "cldr-numbers-full/main/de/currencies.json";
            string letters = new('N', 256);
            return new()
            {
                { Systems, "\"_type\": \"numeric\"", "\"_type\": \"algorithmic\"", "The default numbering system latn of und is not a numeric one." },
                {
                    Systems,
                    "\"_digits\": \"0123456789\"",
                    "\"_digits\": \"0123456780\"",
                    "The digits of latn are not ten consecutive UTF-16 code units."
                },
                {
                    Fractions,
                    "\"BHD\": { \"_rounding\": \"0\"",
                    "\"BHD\": { \"_rounding\": \"5\"",
                    "The currency BHD has a rounding increment, which the formats do not apply."
                },
                {
                    Numbers,
                    "\"currencyMatch\": \"[[:^S:]&[:^Z:]]\"",
                    "\"currencyMatch\": \"[:^S:]\"",
                    "The beforeCurrency currency spacing of de is not the one the formats write."
                },
                { Currencies, "\"symbol\": \"$\"", "\"symbol\": \"\"", "A symbol of USD in de is empty." },
                {
                    Numbers,
                    "\"1000-count-other\": \"0K\"",
                    "\"1000-count-one\": \"0K\"",
                    "The short compact patterns of de have no other pattern for 10^3."
                },
                {
                    Numbers,
                    "\"1000-count-other\": \"0K\"",
                    "\"1500-count-other\": \"0K\"",
                    "The compact pattern key 1500-count-other of de is not a power of ten and a count."
                },
                { Numbers, "\"nan\": \"NaN\"", "\"nan\": \"" + letters + "\"", "The string " + letters + " is longer than 255 bytes of UTF-8." },
            };
        }
    }

    /// <summary>
    /// Two runs of the generator from the same zip write the same files, byte for byte.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A zip of the json-full shape: the languages of row LC4 as modern locales with number and date files, but bo, which has the
    ///   files and no coverage level, and tl, which has neither and an alias to fil; ca-ES-valencia, modern with a variant; root.
    /// - A lock file whose cldr-json line has the zip's SHA-256.
    ///
    /// Steps:
    /// - Run the tool into two empty directories.
    ///
    /// Expected:
    /// - Both runs end with 0, and write the same paths with the same bytes.
    /// - The entries hold bo (added from its data) and tl (the alias), and not ca-ES-valencia.
    /// </remarks>
    [Fact]
    public void Tool_SameZipTwice_WritesTheSameBytes()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string first = Path.Combine(directory, "first");
            string second = Path.Combine(directory, "second");

            (int firstExit, string firstOutput) = RunTool("--zip", zip, "--lock", lockFile, "--out", first);
            (int secondExit, string secondOutput) = RunTool("--zip", zip, "--lock", lockFile, "--out", second);

            Assert.True(firstExit == 0, firstOutput);
            Assert.True(secondExit == 0, secondOutput);
            string[] firstFiles = Files(first);
            Assert.Equal(firstFiles, Files(second));
            Assert.Contains("src/XylemUI.Core/Localization/Cldr/CldrTables.g.cs", firstFiles);
            Assert.Contains("tests/XylemUI.Core.Tests/CldrPluralSamples.g.cs", firstFiles);
            Assert.Contains("src/XylemUI.Core/Localization/Cldr/CldrNumberTables.g.cs", firstFiles);
            Assert.Contains("src/XylemUI.Core/Localization/Cldr/CldrDateTables.g.cs", firstFiles);
            foreach (string file in firstFiles)
            {
                Assert.True(File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, file))), file);
            }

            string locales = File.ReadAllText(Path.Combine(first, "src/XylemUI.Core/Localization/Cldr/CldrLocales.g.cs"));
            Assert.Contains("public static LocaleData Bo => CldrBo.Instance;", locales, StringComparison.Ordinal);
            Assert.Contains("public static LocaleData Tl => CldrTl.Instance;", locales, StringComparison.Ordinal);
            Assert.DoesNotContain("CaESValencia", locales, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A locale takes the plural rules of the parent that the plurals component of parentLocales.json names, and with none it
    /// truncates its tag; the main parent locales are not read (UTS #35 part 1, "Parent Locales": component parents stand alone).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: pt-AO has no rules of its own, plurals.json has pt and pt-PT, and the main parent of pt-AO is
    ///   pt-PT. The plurals component's parents are the row's JSON object.
    ///
    /// Steps:
    /// - Run the tool, and read the line of pt-AO in the table of samples it wrote for the tests.
    ///
    /// Expected:
    /// - With no plural parents, pt-AO takes pt's rules (its tag truncated), not pt-PT's; with pt-PT as its plural parent, pt-PT's;
    ///   with root, und's. Its ordinal rules are und's in every row (ordinals.json has only und).
    /// </remarks>
    /// <param name="pluralParents">The plurals component's parent locales, as a JSON object.</param>
    /// <param name="key">The key of plurals.json whose rules pt-AO takes.</param>
    [Theory]
    [InlineData("{}", "pt")]
    [InlineData("{ \"pt-AO\": \"pt-PT\" }", "pt-PT")]
    [InlineData("{ \"pt-AO\": \"root\" }", "und")]
    public void Tool_PluralParents_AreFollowed_AndTruncatedWithout(string pluralParents, string key)
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, pluralParents);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            string samples = File.ReadAllText(Path.Combine(output, "tests/XylemUI.Core.Tests/CldrPluralSamples.g.cs"));
            string line = "        (\"pt-AO\", \"" + key + "\", \"und\"),";
            Assert.Contains(line, samples, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The likely scripts the generator writes are the scripts of the keys of likelySubtags without a script, less a key of a
    /// language and a region whose script is the language's own, packed and in ascending order (design §4.11, "Where the table
    /// lives").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test, whose likelySubtags has 6 keys: ja (Jpan), zh (Hans), zh-TW (Hant), zh-SG (Hans, the script of
    ///   zh), zh-Hant (a key with a script), und-TW (Hant; und has no key). Packed by hand: ja is 7,317 (10 × 27² + 1 × 27), und
    ///   15,691, zh 19,170, each shifted 16 bits up; TW is 1 + 19 × 26 + 22 = 517.
    ///
    /// Steps:
    /// - Run the tool, and read CldrLikelyScripts.g.cs.
    ///
    /// Expected:
    /// - 6 keys, 1 with a script, 1 with a region left out, 4 entries, 3 scripts; the scripts Hans, Hant, Jpan; the keys ja, und-TW,
    ///   zh, zh-TW as little-endian bytes; their scripts 2, 1, 0, 1.
    /// </remarks>
    [Fact]
    public void Tool_LikelyScripts_HoldTheKeysWithoutAScript()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            string table = File.ReadAllText(Path.Combine(output, "src/XylemUI.Core/Localization/Cldr/CldrLikelyScripts.g.cs"));
            foreach (string line in LikelyLines)
            {
                Assert.Contains(line, table, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The number tables the generator writes hold the symbols, the standard patterns, the compact patterns, and the currency symbols
    /// of the json in the order the formats read them; locales with the same data share one table, and each locale's class returns
    /// its own (design §4.11, "Numbers" and "Declaration and reference").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: every locale has the same number data but de, whose decimal and group separators are swapped
    ///   and which has no alphaNextToNumber pattern;
    ///   every locale has the same currencies; currencyData.json lists BHD (3), CHF (2, with a cash rounding), JPY (0), and DEFAULT
    ///   (2).
    ///
    /// Steps:
    /// - Run the tool, and read CldrNumberTables.g.cs and CldrTables.g.cs.
    ///
    /// Expected:
    /// - 2 number tables, 1 compact table, 1 currency table, and the default of 2 fraction digits.
    /// - The bytes of the lines of NumberLines, counted by hand: root's number table (the first used, ending with the
    ///   alphaNextToNumber pattern), de's (ending with an empty string), the compact table with
    ///   the long patterns ordered one, other, explicit 1, and the currency table without EUR, whose symbol is its code, with JPY's
    ///   narrow symbol alone and USD's standard symbol alone. The currency digits list BHD and JPY and not CHF.
    /// - The class of de returns the number table 1, the class of en the number table 0, and both the compact and currency table 0.
    /// </remarks>
    [Fact]
    public void Tool_NumberTables_HoldTheBytesOfTheData()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            string folder = Path.Combine(output, "src/XylemUI.Core/Localization/Cldr");
            string tables = File.ReadAllText(Path.Combine(folder, "CldrTables.g.cs"));
            string numbers = File.ReadAllText(Path.Combine(folder, "CldrNumberTables.g.cs"));
            foreach (string line in NumberLines)
            {
                Assert.Contains(line, tables + numbers, StringComparison.Ordinal);
            }

            string de = ClassOf(tables, "CldrDe");
            string en = ClassOf(tables, "CldrEn");
            Assert.Contains("    internal override ReadOnlySpan<byte> NumberTable => CldrNumberTables.Numbers1;", de, StringComparison.Ordinal);
            Assert.Contains("    internal override ReadOnlySpan<byte> NumberTable => CldrNumberTables.Numbers0;", en, StringComparison.Ordinal);
            foreach (string entry in new[] { de, en })
            {
                Assert.Contains(
                    "    internal override ReadOnlySpan<byte> CompactTable => CldrNumberTables.Compact0;",
                    entry,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    "    internal override ReadOnlySpan<byte> CurrencyTable => CldrNumberTables.Currencies0;",
                    entry,
                    StringComparison.Ordinal
                );
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The date patterns the generator writes are the closest available format of each of Flutter's 41 skeletons with its fields made
    /// as long as the skeleton's (UTS #35 part 4, "Matching Skeletons"), the hour of the locale's region for j and the 24-hour form
    /// after them, then the joining patterns, the Time-Day-Of-Week append item, gmtFormat, and hourFormat (design §4.11, "Skeleton
    /// formats").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: en's formats are EnFormats, and its region is the world's, whose hour is h; ja's are JaFormats,
    ///   whose MMM is M月, and Japan's hour is H; de's are EnFormats with MMMEEEEd, which is as near MMMMEEEEd as MMMMEd is; pt's
    ///   yMMMEEEEd (EEEE, d/MM/y) is as near yMMMMEEEEd as its yMMMMEd (E, d 'de' MMMM 'de' y) is.
    ///
    /// Steps:
    /// - Run the tool, and read the pattern tables of en, ja, and de from CldrDateTables.g.cs.
    ///
    /// Expected:
    /// - en: the digit zero 0; the 48 patterns of EnPatterns (EEEE takes E's ccc as cccc, LLLL and MMMM take MMM's LLL as LLLL,
    ///   MMMMEEEEd takes MMMMEd's E as EEEE, QQQ, m, and s are the symbol alone, jmz takes hmv's v as z, and the alternative, the
    ///   count, and MMMddd are not read); then {1}, {0} three times, {1} {0}, {0} ({1}), GMT{0}, +HH:mm;-HH:mm, and no day period.
    /// - ja: LLL, LLLL, and MMMM are M月, a number that is not made a name; j is H時, and the 7 24-hour patterns are empty.
    /// - de: MMMMEEEEd takes MMMEEEEd, first in ordinal order of the two at the same distance whose adjusted patterns are as near:
    ///   EEEE, d. MMMM.
    /// - pt: yMMMMEEEEd takes yMMMMEd, whose adjusted pattern writes the wide month, not yMMMEEEEd, first in ordinal order, which
    ///   keeps its numeric month: EEEE, d 'de' MMMM 'de' y.
    /// </remarks>
    [Fact]
    public void Tool_DatePatterns_AreTheClosestFormats()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            (char enZero, List<string> en, int enPeriods) = PatternTable(output, "CldrEn");
            Assert.Equal('0', enZero);
            Assert.Equal(EnPatterns, en.Take(48));
            Assert.Equal(["{1}, {0}", "{1}, {0}", "{1}, {0}", "{1} {0}", "{0} ({1})", "GMT{0}", "+HH:mm;-HH:mm"], en.Skip(48));
            Assert.Equal(0, enPeriods);
            (_, List<string> ja, _) = PatternTable(output, "CldrJa");
            Assert.Equal(["M月", "M月", "M月", "H時"], new[] { ja[3], ja[4], ja[11], ja[31] });
            Assert.All(ja.Skip(41).Take(7), pattern => Assert.True(pattern.Length == 0, pattern));
            (_, List<string> de, _) = PatternTable(output, "CldrDe");
            Assert.Equal("EEEE, d. MMMM", de[13]);
            (_, List<string> pt, _) = PatternTable(output, "CldrPt");
            Assert.Equal("EEEE, d 'de' MMMM 'de' y", pt[25]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A pattern with a flexible day period (B) is written with the periods of the locale's rule set in dayPeriods.json, by their
    /// start, and their names; the periods at one time (midnight) are not written.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: zh's 12-hour formats have B (ZhFormats), and dayPeriods.json gives zh night1 from 00:00,
    ///   morning1 from 06:00, afternoon1 from 12:00, evening1 from 18:00 (written out of order), and midnight at 00:00.
    ///
    /// Steps:
    /// - Run the tool, and read zh's pattern table.
    ///
    /// Expected:
    /// - jm is Bh:mm and jmz Bh:mm [z].
    /// - 4 periods, starting at 0, 360, 720, and 1080 minutes, named aPnight1, wPnight1, nPnight1 and so on.
    /// </remarks>
    [Fact]
    public void Tool_FlexibleDayPeriods_AreWrittenByTheirStart()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            byte[] table = TableBytesOf(output, "CldrZh", "DatePatternTable");
            (_, List<string> zh, int count) = PatternTable(output, "CldrZh");
            Assert.Equal(("Bh:mm", "Bh:mm [z]"), (zh[32], zh[35]));
            Assert.Equal(4, count);
            var reader = new CldrTableReader(table);
            _ = reader.ReadChar();
            for (int i = 0; i < 55; i++)
            {
                reader.SkipString();
            }

            _ = reader.ReadByte();
            var periods = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int start = reader.ReadByte() | (reader.ReadByte() << 8);
                periods.Add(
                    start.ToString(CultureInfo.InvariantCulture) + " " + reader.ReadString() + " " + reader.ReadString() + " " + reader.ReadString()
                );
            }

            Assert.True(reader.End);
            string[] expected =
            [
                "0 aPnight1 wPnight1 nPnight1",
                "360 aPmorning1 wPmorning1 nPmorning1",
                "720 aPafternoon1 wPafternoon1 nPafternoon1",
                "1080 aPevening1 wPevening1 nPevening1",
            ];
            Assert.Equal(expected, periods);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The name table the generator writes holds the months, the weekdays, the quarters, and am and pm of the format context, then
    /// of the stand-alone context with each name that is the format one empty, then the eras (design §4.11, "Names and the week").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test: en's names are those of GregorianJson, whose stand-alone names are the format ones but the wide
    ///   January, wM1alone.
    ///
    /// Steps:
    /// - Run the tool, and read en's name table string by string.
    ///
    /// Expected:
    /// - 170 strings: aM1 to aM12, wM1 to wM12, nM1 to nM12; aDsun to aDsat, wD…, nD…, sD…; aQ1 to aQ4, wQ…, nQ…; aPam, aPpm,
    ///   wPam, wPpm, nPam, nPpm; then the same count of the stand-alone context, all empty but wM1alone in the place of wM1; then BC,
    ///   AD, Before Christ, Anno Domini, B, A.
    /// </remarks>
    [Fact]
    public void Tool_DateNames_WriteTheStandAloneNamesThatAreTheFormatOnesEmpty()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.True(exit == 0, text);
            var reader = new CldrTableReader(TableBytesOf(output, "CldrEn", "DateNameTable"));
            var names = new List<string>();
            while (!reader.End)
            {
                names.Add(reader.ReadString());
            }

            List<string> format =
            [
                .. Kind("M", ["a", "w", "n"], Enumerable.Range(1, 12).Select(month => month.ToString(CultureInfo.InvariantCulture))),
                .. Kind("D", ["a", "w", "n", "s"], ["sun", "mon", "tue", "wed", "thu", "fri", "sat"]),
                .. Kind("Q", ["a", "w", "n"], ["1", "2", "3", "4"]),
                .. Kind("P", ["a", "w", "n"], ["am", "pm"]),
            ];
            List<string> standAlone = [.. format.Select(name => name == "wM1" ? "wM1alone" : string.Empty)];
            Assert.Equal([.. format, .. standAlone, "BC", "AD", "Before Christ", "Anno Domini", "B", "A"], names);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static IEnumerable<string> Kind(string kind, string[] widths, IEnumerable<string> keys) =>
            widths.SelectMany(width => keys.Select(key => width + kind + key));
    }

    /// <summary>
    /// A skeleton that no available format holds without a field missing or added stops the generator: the tool does not append
    /// fields (appendItems), which no skeleton of the 41 needs in CLDR 49.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test with de's yQQQ and yQQQQ left out, and a lock with its SHA-256.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1 and names de and the skeleton yQQQ.
    /// </remarks>
    [Fact]
    public void Tool_SkeletonWithoutAFormat_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, deYearQuarters: false);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains("No format of de has the fields of the skeleton yQQQ", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A date pattern with a field the formats do not write stops the generator: the hours of 1 to 24 (k).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test with de's Hm as kk:mm, and a lock with its SHA-256.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1 and names the pattern and de.
    /// </remarks>
    [Fact]
    public void Tool_DatePatternWithAFieldTheFormatsDoNotWrite_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, deHm: "kk:mm");
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains("The pattern kk:mm of de has a field the formats do not write", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A number pattern of a form the formats do not read stops the generator: an exponent, which the formats do not write.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test with de's decimal pattern #E0, and a lock with its SHA-256.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1 and names the pattern and de.
    /// </remarks>
    [Fact]
    public void Tool_PatternWithAnExponent_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, deDecimalPattern: "#E0");
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains("The pattern #E0 of de", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Number data the formats do not read stops the generator with a message that names it (design §4.11, "How the tables are
    /// made"; PR #944's review).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test with one text of one entry replaced, and a lock with its SHA-256. The cases: latn's type
    ///   algorithmic, not numeric (und, read first, stops); latn's digits 0123456780, not consecutive; BHD's rounding increment 5;
    ///   de's currencyMatch [:^S:] on both sides (the before side is checked first); de's USD symbol empty; de's short compact key
    ///   1000-count-other made 1000-count-one, so 10^3 has no other pattern; that key made 1500-count-other, not a power of ten; de's
    ///   NaN symbol of 256 ASCII letters, one byte past the 255 a length byte holds.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1, and its output holds the message of each case.
    /// </remarks>
    /// <param name="entry">The entry of the zip whose text is replaced.</param>
    /// <param name="from">The text replaced.</param>
    /// <param name="to">The text put in its place.</param>
    /// <param name="message">The message the tool writes.</param>
    [Theory]
    [MemberData(nameof(UnreadNumberData))]
    public void Tool_NumberDataTheFormatsDoNotRead_Stops(string entry, string from, string to, string message)
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true, edit: (entry, from, to));
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains(message, text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A zip whose SHA-256 is not the one the lock pins stops the generator before it writes anything.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the test above, and a lock file whose cldr-json line names another SHA-256 (the zip's with its last digit
    ///   changed).
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1, says the zip's SHA-256 is not the lock's, and the directory stays empty.
    /// </remarks>
    [Fact]
    public void Tool_ZipOfAnotherSha256_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: true);
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
            string other = actual[..^1] + (actual[^1] == '0' ? '1' : '0');
            string lockFile = WriteLock(directory, other);
            string output = Path.Combine(directory, "out");

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", output);

            Assert.Equal(1, exit);
            Assert.Contains("SHA-256 " + actual + ", not " + other, text, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A language of row LC4 that has neither number and date files nor an alias to a locale of the set stops the generator: the
    /// widgets' translations would have a language with no tables (design §4.11, "The set of locales").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The zip of the first test without bo's files, and a lock with its SHA-256.
    ///
    /// Steps:
    /// - Run the tool into an empty directory.
    ///
    /// Expected:
    /// - It ends with 1 and names bo.
    /// </remarks>
    [Fact]
    public void Tool_Lc4LanguageWithoutData_Stops()
    {
        string directory = Directory.CreateTempSubdirectory("xylemui-cldr-").FullName;
        try
        {
            string zip = WriteZip(directory, withBo: false);
            string lockFile = WriteLock(directory, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip))));

            (int exit, string text) = RunTool("--zip", zip, "--lock", lockFile, "--out", Path.Combine(directory, "out"));

            Assert.Equal(1, exit);
            Assert.Contains("The language bo of row LC4", text, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A zip of the json-full shape, with the files the generator reads and nothing more. Every locale has the number data of
    // NumbersJson and the currencies of CurrenciesJson, but de, whose separators are swapped and whose decimal pattern is the given
    // one. Every locale has the date data of GregorianJson with the formats of EnFormats, but ja (JaFormats), zh (ZhFormats), pt
    // (EnFormats with yMMMMEd as E, d 'de' MMMM 'de' y and yMMMEEEEd as EEEE, d/MM/y, pt-PT's), and de (EnFormats with MMMEEEEd, the
    // given Hm, and without yQQQ and yQQQQ when deYearQuarters is false), and the time zone data of
    // TimeZoneJson. timeData gives the world (001), the region of en, h, and Japan H; dayPeriods.json has rules for zh alone. pt-AO
    // is a modern locale with no plural rules of its own; plurals.json has rules for pt and for pt-PT, and the parent locales of the
    // plurals component are the given JSON object. An edit replaces a text in one entry, which must hold it.
    private static string WriteZip(
        string directory,
        bool withBo,
        string pluralParents = "{}",
        string deDecimalPattern = "#,##0.###",
        string deHm = "HH:mm",
        bool deYearQuarters = true,
        (string Entry, string From, string To)? edit = null
    )
    {
        string path = Path.Combine(directory, "cldr-test-json-full.zip");
        using (var zip = new EditedZip(ZipFile.Open(path, ZipArchiveMode.Create), edit))
        {
            Add(zip, "LICENSE", "Test notice.\n");
            Add(zip, "cldr-core/package.json", "{ \"version\": \"49.0.0\" }");
            var levels = new StringBuilder(
                "{ \"effectiveCoverageLevels\": { \"und\": \"modern\", \"ca-ES-valencia\": \"modern\", \"pt-AO\": \"modern\""
            );
            foreach (string language in Lc4Languages.Where(language => language is not ("tl" or "bo")))
            {
                levels.Append(", \"").Append(language).Append("\": \"modern\"");
            }

            levels.Append(" } }");
            Add(zip, "cldr-core/coverageLevels.json", levels.ToString());
            foreach (
                string tag in Lc4Languages
                    .Where(language => language != "tl" && (withBo || language != "bo"))
                    .Append("und")
                    .Append("ca-ES-valencia")
                    .Append("pt-AO")
            )
            {
                string numbers = tag == "de" ? NumbersJson(tag, ",", ".", deDecimalPattern, false) : NumbersJson(tag, ".", ",", "#,##0.###", true);
                Add(zip, "cldr-numbers-full/main/" + tag + "/numbers.json", numbers);
                Add(zip, "cldr-numbers-full/main/" + tag + "/currencies.json", CurrenciesJson(tag));
                IEnumerable<(string, string)> formats = tag switch
                {
                    "ja" => JaFormats,
                    "zh" => ZhFormats,
                    "pt" => EnFormats
                        .Select(format => format.Key == "yMMMMEd" ? (format.Key, "E, d 'de' MMMM 'de' y") : format)
                        .Append(("yMMMEEEEd", "EEEE, d/MM/y")),
                    "de" => EnFormats
                        .Where(format => deYearQuarters || format.Key is not ("yQQQ" or "yQQQQ"))
                        .Select(format => format.Key == "Hm" ? (format.Key, deHm) : format)
                        .Append(("MMMEEEEd", "EEEE, d. MMM")),
                    _ => EnFormats,
                };
                Add(zip, "cldr-dates-full/main/" + tag + "/ca-gregorian.json", GregorianJson(tag, formats));
                Add(zip, "cldr-dates-full/main/" + tag + "/timeZoneNames.json", TimeZoneJson(tag));
            }

            Add(
                zip,
                "cldr-core/supplemental/numberingSystems.json",
                "{ \"supplemental\": { \"numberingSystems\": { \"latn\": { \"_digits\": \"0123456789\", \"_type\": \"numeric\" } } } }"
            );
            Add(zip, "cldr-core/supplemental/currencyData.json", CurrencyData);

            Add(
                zip,
                "cldr-core/supplemental/aliases.json",
                "{ \"supplemental\": { \"metadata\": { \"alias\": { \"languageAlias\": { \"tl\": { \"_reason\": \"legacy\", \"_replacement\": \"fil\" } } } } } }"
            );
            const string Likely =
                "{ \"supplemental\": { \"likelySubtags\": { \"ja\": \"ja-Jpan-JP\", \"zh\": \"zh-Hans-CN\", \"zh-TW\": \"zh-Hant-TW\", "
                + "\"zh-SG\": \"zh-Hans-SG\", \"zh-Hant\": \"zh-Hant-TW\", \"und-TW\": \"zh-Hant-TW\" } } }";
            Add(zip, "cldr-core/supplemental/likelySubtags.json", Likely);
            const string Weeks =
                "{ \"supplemental\": { \"weekData\": { \"minDays\": { \"001\": \"1\" }, \"firstDay\": { \"001\": \"mon\", \"JP\": \"sun\" }, "
                + "\"weekendStart\": { \"001\": \"sat\" }, \"weekendEnd\": { \"001\": \"sun\" } } } }";
            Add(zip, "cldr-core/supplemental/weekData.json", Weeks);
            const string Hours =
                "{ \"supplemental\": { \"timeData\": { \"001\": { \"_allowed\": \"h H\", \"_preferred\": \"h\" }, "
                + "\"JP\": { \"_allowed\": \"H h\", \"_preferred\": \"H\" } } } }";
            Add(zip, "cldr-core/supplemental/timeData.json", Hours);
            const string Periods =
                "{ \"supplemental\": { \"dayPeriodRuleSet\": { \"zh\": { \"midnight\": { \"_at\": \"00:00\" }, "
                + "\"afternoon1\": { \"_from\": \"12:00\", \"_before\": \"18:00\" }, \"night1\": { \"_from\": \"00:00\", \"_before\": \"06:00\" }, "
                + "\"morning1\": { \"_from\": \"06:00\", \"_before\": \"12:00\" }, \"evening1\": { \"_from\": \"18:00\", \"_before\": \"24:00\" } } } } }";
            Add(zip, "cldr-core/supplemental/dayPeriods.json", Periods);
            const string Cardinals =
                "{ \"supplemental\": { \"plurals-type-cardinal\": { \"und\": { \"pluralRule-count-other\": \" @integer 0~15\" }, "
                + "\"en\": { \"pluralRule-count-one\": \"i = 1 and v = 0 @integer 1\", \"pluralRule-count-other\": \" @integer 0, 2~16\" }, "
                + "\"pt\": { \"pluralRule-count-one\": \"i = 0..1 @integer 0, 1\", \"pluralRule-count-other\": \" @integer 2~17\" }, "
                + "\"pt-PT\": { \"pluralRule-count-one\": \"i = 1 and v = 0 @integer 1\", \"pluralRule-count-other\": \" @integer 0, 2~16\" } } } }";
            Add(zip, "cldr-core/supplemental/plurals.json", Cardinals);
            string parents =
                "{ \"supplemental\": { \"parentLocales\": { \"parentLocale\": { \"pt-AO\": \"pt-PT\" }, \"plurals\": " + pluralParents + " } } }";
            Add(zip, "cldr-core/supplemental/parentLocales.json", parents);
            Add(
                zip,
                "cldr-core/supplemental/ordinals.json",
                "{ \"supplemental\": { \"plurals-type-ordinal\": { \"und\": { \"pluralRule-count-other\": \" @integer 0~15\" } } } }"
            );
        }

        return path;
    }

    // The number data of a locale: Latin digits, the given separators and decimal pattern, one short compact pattern, and three
    // long ones (one, other, and the explicit 1). The currency spacing is CLDR's, with its no-break space; with alpha, the locale
    // also has the alphaNextToNumber pattern, the currency sign and a no-break space before #,##0.00.
    private static string NumbersJson(string tag, string decimalSeparator, string groupSeparator, string decimalPattern, bool alpha)
    {
        string spacing =
            "{ \"currencyMatch\": \"[[:^S:]&[:^Z:]]\", \"surroundingMatch\": \"[:digit:]\", \"insertBetween\": \"" + NoBreakSpace + "\" }";
        string alphaPattern = alpha ? ", \"standard-alphaNextToNumber\": \"" + CurrencySign + NoBreakSpace + "#,##0.00\"" : string.Empty;
        return "{ \"main\": { \""
            + tag
            + "\": { \"numbers\": { \"defaultNumberingSystem\": \"latn\", \"minimumGroupingDigits\": \"1\", "
            + "\"symbols-numberSystem-latn\": { \"decimal\": \""
            + decimalSeparator
            + "\", \"group\": \""
            + groupSeparator
            + "\", \"minusSign\": \"-\", \"plusSign\": \"+\", \"percentSign\": \"%\", \"infinity\": \""
            + Infinity
            + "\", \"nan\": \"NaN\" }, "
            + "\"decimalFormats-numberSystem-latn\": { \"standard\": \""
            + decimalPattern
            + "\", \"short\": { \"decimalFormat\": { \"1000-count-other\": \"0K\" } }, "
            + "\"long\": { \"decimalFormat\": { \"1000-count-other\": \"0 thousand\", \"1000-count-one\": \"0 thousand\", "
            + "\"1000-count-1\": \"a thousand\" } } }, "
            + "\"percentFormats-numberSystem-latn\": { \"standard\": \"#,##0%\" }, "
            + "\"currencyFormats-numberSystem-latn\": { \"currencySpacing\": { \"beforeCurrency\": "
            + spacing
            + ", \"afterCurrency\": "
            + spacing
            + " }, \"standard\": \""
            + CurrencySign
            + "#,##0.00\""
            + alphaPattern
            + " } } } } }";
    }

    // The Gregorian data of a locale: names made of their width's letter, their kind, and their key (aM1 is the abbreviated January,
    // wDtue the wide Tuesday, nQ4 the narrow fourth quarter, aPpm the abbreviated pm, aPnight1 the abbreviated night1), the same in
    // the stand-alone context but the wide January (wM1alone); the eras BC, AD, Before Christ, Anno Domini, B, A; the joining patterns
    // {1}, {0} but the short one {1} {0}; the Time-Day-Of-Week append item {0} ({1}); and the given formats.
    private static string GregorianJson(string tag, IEnumerable<(string Key, string Pattern)> formats)
    {
        string[] months = [.. Enumerable.Range(1, 12).Select(month => month.ToString(CultureInfo.InvariantCulture))];
        string[] quarters = ["1", "2", "3", "4"];
        string[] weekdays = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];
        string[] periods = ["am", "pm", "night1", "morning1", "afternoon1", "evening1"];
        string[] widths = ["abbreviated", "wide", "narrow"];
        string[] weekdayWidths = ["abbreviated", "wide", "narrow", "short"];
        string available = JsonObject(formats.Select(format => (format.Key, JsonText(format.Pattern))));
        return "{ \"main\": { \""
            + tag
            + "\": { \"dates\": { \"calendars\": { \"gregorian\": { \"months\": "
            + Contexts(widths, "M", months, "wM1alone")
            + ", \"days\": "
            + Contexts(weekdayWidths, "D", weekdays, null)
            + ", \"quarters\": "
            + Contexts(widths, "Q", quarters, null)
            + ", \"dayPeriods\": "
            + Contexts(widths, "P", periods, null)
            + ", \"eras\": { \"eraNames\": { \"0\": \"Before Christ\", \"1\": \"Anno Domini\" }, \"eraAbbr\": { \"0\": \"BC\", \"1\": \"AD\" }, "
            + "\"eraNarrow\": { \"0\": \"B\", \"1\": \"A\" } }, \"dateTimeFormats\": { \"full\": \"{1}, {0}\", \"long\": \"{1}, {0}\", "
            + "\"medium\": \"{1}, {0}\", \"short\": \"{1} {0}\", \"appendItems\": { \"Time-Day-Of-Week\": \"{0} ({1})\" }, "
            + "\"availableFormats\": "
            + available
            + " } } } } } } }";
    }

    // The format and the stand-alone names of one kind; the stand-alone wide name of the first key is the given one when there is one.
    private static string Contexts(string[] widths, string kind, string[] keys, string? standAloneFirstWide)
    {
        string format = JsonObject(widths.Select(width => (width, Names(width, kind, keys, null))));
        string standAlone = JsonObject(widths.Select(width => (width, Names(width, kind, keys, width == "wide" ? standAloneFirstWide : null))));
        return "{ \"format\": " + format + ", \"stand-alone\": " + standAlone + " }";
    }

    // The names of one width: its letter, the kind, and the key; the first is the given one when there is one.
    private static string Names(string width, string kind, string[] keys, string? first) =>
        JsonObject(keys.Select((key, index) => (key, JsonText(index == 0 && first is not null ? first : width[0] + kind + key))));

    // The time zone data of a locale: the offset from GMT as GMT+09:00.
    private static string TimeZoneJson(string tag) =>
        "{ \"main\": { \"" + tag + "\": { \"dates\": { \"timeZoneNames\": { \"hourFormat\": \"+HH:mm;-HH:mm\", \"gmtFormat\": \"GMT{0}\" } } } } }";

    private static string JsonObject(IEnumerable<(string Key, string Value)> members) =>
        "{ " + string.Join(", ", members.Select(member => "\"" + member.Key + "\": " + member.Value)) + " }";

    private static string JsonText(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // The currencies of a locale: EUR's symbol is its code, JPY's is its code with a narrow one, and USD's symbol is its narrow one.
    private static string CurrenciesJson(string tag) =>
        "{ \"main\": { \""
        + tag
        + "\": { \"numbers\": { \"currencies\": { \"EUR\": { \"symbol\": \"EUR\" }, "
        + "\"JPY\": { \"symbol\": \"JPY\", \"symbol-alt-narrow\": \""
        + YenSign
        + "\" }, \"USD\": { \"displayName\": \"US Dollar\", \"symbol\": \"$\", \"symbol-alt-narrow\": \"$\" } } } } } }";

    private static void Add(EditedZip zip, string name, string text)
    {
        if (zip.Edit is { } edit && edit.Entry == name)
        {
            Assert.Contains(edit.From, text, StringComparison.Ordinal);
            text = text.Replace(edit.From, edit.To, StringComparison.Ordinal);
        }

        using var writer = new StreamWriter(zip.Archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string WriteLock(string directory, string sha256)
    {
        string path = Path.Combine(directory, "versions.lock");
        File.WriteAllText(path, "# test\ncldr-json  49.0.0  " + sha256 + "  https://example.invalid/cldr-test-json-full.zip\n");
        return path;
    }

    // The text of one generated class, from its declaration to the brace that closes it.
    private static string ClassOf(string tables, string name)
    {
        int start = tables.IndexOf("internal sealed class " + name + " : LocaleData", StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        int end = tables.IndexOf("\n}\n", start, StringComparison.Ordinal);
        return tables[start..end];
    }

    // The bytes of a date table that a locale's class returns: the class names the table in CldrTables.g.cs, and
    // CldrDateTables.g.cs holds its bytes.
    private static byte[] TableBytesOf(string output, string className, string property)
    {
        string folder = Path.Combine(output, "src/XylemUI.Core/Localization/Cldr");
        string entry = ClassOf(File.ReadAllText(Path.Combine(folder, "CldrTables.g.cs")), className);
        string prefix = "    internal override ReadOnlySpan<byte> " + property + " => CldrDateTables.";
        string line = entry.Split('\n').Single(text => text.StartsWith(prefix, StringComparison.Ordinal));
        string declaration = "    public static ReadOnlySpan<byte> " + line[prefix.Length..^1] + " => [";
        string bytes = File.ReadAllLines(Path.Combine(folder, "CldrDateTables.g.cs"))
            .Single(text => text.StartsWith(declaration, StringComparison.Ordinal));
        return [.. bytes[declaration.Length..^2].Split(", ").Select(value => byte.Parse(value, CultureInfo.InvariantCulture))];
    }

    // A locale's date pattern table: the digit zero, the 55 strings, and the count of flexible day periods.
    private static (char Zero, List<string> Strings, int Periods) PatternTable(string output, string className)
    {
        var reader = new CldrTableReader(TableBytesOf(output, className, "DatePatternTable"));
        char zero = reader.ReadChar();
        var strings = new List<string>();
        for (int i = 0; i < 55; i++)
        {
            strings.Add(reader.ReadString());
        }

        return (zero, strings, reader.ReadByte());
    }

    private static string[] Files(string root) =>
        [
            .. Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

    private static (int ExitCode, string Output) RunTool(params string[] arguments)
    {
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        string tool = Path.Combine(RepositoryRoot(), "tools", "CldrTableGen", "bin", Configuration, "net10.0", "CldrTableGen.dll");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(tool);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error.Result);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "XylemUI.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("The tests run outside the repository.");
    }

    // A zip being written, and the edit of the text of one of its entries.
    private sealed class EditedZip(ZipArchive archive, (string Entry, string From, string To)? edit) : IDisposable
    {
        public ZipArchive Archive { get; } = archive;

        public (string Entry, string From, string To)? Edit { get; } = edit;

        public void Dispose() => Archive.Dispose();
    }
}
