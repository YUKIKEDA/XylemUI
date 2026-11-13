using System.Diagnostics;
using static LinuxProbe.Report;

namespace LinuxProbe;

/// <summary>fontconfig's matches for a sans and a Japanese pattern (question 4 of the report; design §4.5).</summary>
internal static unsafe class FontconfigProbe
{
    // FcResultMatch, FcMatchPattern, and FcSetSystem are 0 in fontconfig.h.
    private const int FcResultMatch = 0;
    private const int FcMatchPattern = 0;
    private const int FcSetSystem = 0;

    private static readonly (string Key, string Pattern)[] Patterns =
    [
        ("sans", "sans"),
        ("sans_lang_ja", "sans-serif:lang=ja"),
        ("lang_ja", ":lang=ja"),
        ("noto_sans_cjk_jp", "Noto Sans CJK JP"),
    ];

    public static void Run()
    {
        nint library = Dl.Open("libfontconfig.so.1");
        var getVersion = (delegate* unmanaged<int>)Dl.Symbol(library, "FcGetVersion");
        var init = (delegate* unmanaged<nint>)Dl.Symbol(library, "FcInitLoadConfigAndFonts");
        var getFonts = (delegate* unmanaged<nint, int, int*>)Dl.Symbol(library, "FcConfigGetFonts");

        int version = getVersion();
        Probe("fontconfig.version", $"{version / 10000}.{version / 100 % 100}.{version % 100}");

        long start = Stopwatch.GetTimestamp();
        nint config = init();
        long end = Stopwatch.GetTimestamp();
        if (config == 0)
        {
            Failed("fontconfig.init", $"FcInitLoadConfigAndFonts returned NULL after {Ms(start, end)}");
            return;
        }

        Probe("fontconfig.init", $"ok in {Ms(start, end)}; {ProcStatus.Memory()}");

        // FcFontSet starts with `int nfont`; the set belongs to the config and is not freed here.
        int* fonts = getFonts(config, FcSetSystem);
        Probe("fontconfig.system_fonts", fonts == null ? "(no set)" : (*fonts).ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach ((string key, string pattern) in Patterns)
        {
            Guard("fontconfig." + key, () => Match(library, config, key, pattern, repeat: false));
        }

        // The same patterns again: what a second fallback lookup costs once fontconfig's caches are warm (question Q18).
        foreach ((string key, string pattern) in Patterns)
        {
            Guard("fontconfig." + key + ".repeat", () => Match(library, config, key, pattern, repeat: true));
        }
    }

    private static void Match(nint library, nint config, string key, string pattern, bool repeat)
    {
        var parse = (delegate* unmanaged<byte*, nint>)Dl.Symbol(library, "FcNameParse");
        var substitute = (delegate* unmanaged<nint, nint, int, int>)Dl.Symbol(library, "FcConfigSubstitute");
        var defaultSubstitute = (delegate* unmanaged<nint, void>)Dl.Symbol(library, "FcDefaultSubstitute");
        var match = (delegate* unmanaged<nint, nint, int*, nint>)Dl.Symbol(library, "FcFontMatch");
        var getString = (delegate* unmanaged<nint, byte*, int, byte**, int>)Dl.Symbol(library, "FcPatternGetString");
        var getCharSet = (delegate* unmanaged<nint, byte*, int, nint*, int>)Dl.Symbol(library, "FcPatternGetCharSet");
        var hasChar = (delegate* unmanaged<nint, uint, int>)Dl.Symbol(library, "FcCharSetHasChar");
        var destroy = (delegate* unmanaged<nint, void>)Dl.Symbol(library, "FcPatternDestroy");

        byte[] patternText = Dl.Utf8Z(pattern);
        nint matched;
        int result;
        long start;
        long end;
        fixed (byte* text = patternText)
        {
            start = Stopwatch.GetTimestamp();
            nint query = parse(text);
            if (query == 0)
            {
                Failed("fontconfig." + key, $"FcNameParse(\"{pattern}\") returned NULL");
                return;
            }

            substitute(config, query, FcMatchPattern);
            defaultSubstitute(query);
            matched = match(config, query, &result);
            end = Stopwatch.GetTimestamp();
            destroy(query);
        }

        string probeName = "fontconfig." + key + (repeat ? ".repeat" : string.Empty);
        if (matched == 0)
        {
            Failed(probeName, $"FcFontMatch(\"{pattern}\") returned NULL, result {result}, after {Ms(start, end)}");
            return;
        }

        if (repeat)
        {
            Probe(probeName, $"{Ms(start, end)}");
            destroy(matched);
            return;
        }

        string family = GetString(getString, matched, "family"u8);
        string style = GetString(getString, matched, "style"u8);
        string file = GetString(getString, matched, "file"u8);
        string coverage = "charset unavailable";
        nint charSet;
        fixed (byte* charsetName = "charset"u8)
        {
            if (getCharSet(matched, charsetName, 0, &charSet) == FcResultMatch)
            {
                // U+3042 HIRAGANA LETTER A and U+6F22 (the Han character for "Han").
                coverage = $"has U+3042={hasChar(charSet, 0x3042) != 0} U+6F22={hasChar(charSet, 0x6F22) != 0}";
            }
        }

        Probe(probeName, $"pattern '{pattern}' -> family '{family}' style '{style}' file '{file}'; {coverage}; {Ms(start, end)}");
        destroy(matched);
    }

    private static string GetString(delegate* unmanaged<nint, byte*, int, byte**, int> getString, nint pattern, ReadOnlySpan<byte> objectName)
    {
        byte* value = null;
        // A u8 literal is followed by a zero in memory, so its pinned address is a C string. The value belongs to the pattern.
        fixed (byte* name = objectName)
        {
            return getString(pattern, name, 0, &value) == FcResultMatch ? Dl.Str(value) : "(none)";
        }
    }
}
