using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacProbe;

// Probe 4: Core Text font lookup, fallback (with and without a language), font file URLs, and a glyph rasterized to a bitmap.
internal static unsafe class CoreTextProbes
{
    private const uint UIFontSystem = 2; // kCTFontUIFontSystem
    private const string Han = "直"; // U+76F4: Japanese and Simplified Chinese glyph forms differ

    private static nint s_helvetica;
    private static nint s_uiFont;

    public static void Run()
    {
        Report.Step(
            "font_by_name",
            () =>
            {
                nint name = ObjC.CFStr("Helvetica");
                s_helvetica = CT.CTFontCreateWithName(name, 13, 0);
                CF.CFRelease(name);
                Report.Value("font_by_name", s_helvetica != 0 ? $"ok Helvetica: {Describe(s_helvetica)}" : "FAILED CTFontCreateWithName returned NULL");
            }
        );
        Report.Step(
            "ui_font",
            () =>
            {
                s_uiFont = CT.CTFontCreateUIFontForLanguage(UIFontSystem, 13, 0);
                Report.Value("ui_font", s_uiFont != 0 ? $"ok {Describe(s_uiFont)}" : "FAILED CTFontCreateUIFontForLanguage returned NULL");
            }
        );
        Report.Step("font_url", () => Report.Value("font_url", $"Helvetica: {UrlOf(s_helvetica)}; system UI font: {UrlOf(s_uiFont)}"));
        Report.Step("fallback_ja", () => Report.Value("fallback_ja", Fallback(s_helvetica, "日本語のテキスト")));
        Report.Step("fallback_emoji", () => Report.Value("fallback_emoji", Fallback(s_helvetica, "😀")));
        Report.Step(
            "fallback_han_by_language",
            () =>
            {
                var parts = new List<string> { $"no language: {Fallback(s_helvetica, Han)}" };
                if (!NativeLibrary.TryGetExport(Frameworks.Handle("CoreText"), "CTFontCreateForStringWithLanguage", out nint address))
                {
                    parts.Add("CTFontCreateForStringWithLanguage: not exported");
                }
                else
                {
                    var create = (delegate* unmanaged<nint, nint, CFRange, nint, nint>)address;
                    foreach (string language in new[] { "ja", "zh-Hans", "zh-Hant", "ko" })
                    {
                        nint text = ObjC.CFStr(Han);
                        nint tag = ObjC.CFStr(language);
                        nint font = create(s_helvetica, text, new CFRange(0, Han.Length), tag);
                        parts.Add($"{language}: {(font != 0 ? Describe(font) : "NULL")}");
                        CF.CFRelease(text);
                        CF.CFRelease(tag);
                        if (font != 0)
                        {
                            CF.CFRelease(font);
                        }
                    }
                }
                Report.Value("fallback_han_by_language", string.Join("; ", parts));
            }
        );
        Report.Step("rasterize", Rasterize);
    }

    private static void Rasterize()
    {
        const int Width = 32;
        const int Height = 32;
        const int Draws = 1000;
        nint name = ObjC.CFStr("Helvetica");
        nint font = CT.CTFontCreateWithName(name, 24, 0);
        CF.CFRelease(name);
        byte* pixels = (byte*)NativeMemory.AllocZeroed(Width * Height);
        nint gray = CG.CGColorSpaceCreateDeviceGray();
        nint context = CG.CGBitmapContextCreate(pixels, Width, Height, 8, Width, gray, 0); // kCGImageAlphaNone
        if (font == 0 || context == 0)
        {
            throw new ProbeException($"font {ObjC.Hex(font)}, bitmap context {ObjC.Hex(context)}");
        }
        CG.CGContextSetGrayFillColor(context, 1, 1);
        char character = 'A';
        ushort glyph = 0;
        byte mapped = CT.CTFontGetGlyphsForCharacters(font, &character, &glyph, 1);
        var at = new CGPoint { X = 6, Y = 8 };
        CT.CTFontDrawGlyphs(font, &glyph, &at, 1, context);
        int ink = 0;
        int max = 0;
        for (int i = 0; i < Width * Height; i++)
        {
            if (pixels[i] != 0)
            {
                ink++;
                max = Math.Max(max, pixels[i]);
            }
        }
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Draws; i++)
        {
            CT.CTFontDrawGlyphs(font, &glyph, &at, 1, context);
        }
        double us = Stopwatch.GetElapsedTime(start).TotalMicroseconds / Draws;
        Report.Value(
            "rasterize",
            $"{(ink > 0 ? "ok" : "FAILED")} 'A' -> glyph {glyph} (mapped {mapped}) at 24 pt into a 32x32 8-bit gray context: {ink} pixels inked, max {max}; "
                + $"{us:F2} us per CTFontDrawGlyphs ({Draws} draws)"
        );
    }

    private static string Describe(nint font) =>
        $"family=\"{Take(CT.CTFontCopyFamilyName(font))}\" postscript=\"{Take(CT.CTFontCopyPostScriptName(font))}\"";

    // Reads a +1 CFString and releases it.
    private static string? Take(nint str)
    {
        if (str == 0)
        {
            return null;
        }
        string? text = ObjC.Text(str);
        CF.CFRelease(str);
        return text;
    }

    private static string UrlOf(nint font)
    {
        if (font == 0)
        {
            return "no font";
        }
        nint attribute = ObjC.ReadExport(Frameworks.Handle("CoreText"), "kCTFontURLAttribute");
        nint url = CT.CTFontCopyAttribute(font, attribute);
        if (url == 0)
        {
            return "no URL";
        }
        string? path = Take(CF.CFURLCopyFileSystemPath(url, 0));
        CF.CFRelease(url);
        return path ?? "no path";
    }

    private static string Fallback(nint font, string text)
    {
        if (font == 0)
        {
            throw new ProbeException("no base font (see font_by_name)");
        }
        nint str = ObjC.CFStr(text);
        nint found = CT.CTFontCreateForString(font, str, new CFRange(0, text.Length));
        CF.CFRelease(str);
        if (found == 0)
        {
            return "FAILED CTFontCreateForString returned NULL";
        }
        string result = Describe(found);
        CF.CFRelease(found);
        return $"ok {result}";
    }
}
