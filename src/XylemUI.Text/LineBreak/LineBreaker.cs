using XylemUI.Text.Unicode;
using static XylemUI.Text.Unicode.LineBreakClass;

namespace XylemUI.Text.LineBreak;

/// <summary>
/// A place where a line may end (UAX #14): before the character at <see cref="Position"/>, or at the end of the text.
/// </summary>
/// <param name="Position">The position, in UTF-16 code units, after the last character of the line.</param>
/// <param name="Mandatory">Whether the line must end here (rules LB3, LB4, LB5).</param>
internal readonly record struct LineBreakOpportunity(int Position, bool Mandatory);

/// <summary>
/// Finds the line break opportunities of a text with the default rules of UAX #14 (Unicode 18.0.0; design §4.1, "Line breaking and
/// grapheme clusters"). Small kana (CJ) resolve to NS, as rule LB1 does by default (strict).
/// </summary>
/// <remarks>
/// The characters are first taken into units: a base character and the combining marks and ZWJs rule LB9 attaches to it, or a mark
/// rule LB10 makes a letter of. The rules LB4 to LB31 are then tried in order at each boundary between units, where some of them
/// read a few units before or after. The arrays are kept and grow only when a text needs more, so a steady state allocates
/// nothing; the opportunities are valid until the next call.
/// </remarks>
internal sealed class LineBreaker
{
    private const int DottedCircle = 0x25CC;
    private const byte Pictographic = 1;
    private const byte EastAsian = 2;
    private const byte Dotted = 4;
    private const byte EndsWithZwj = 8;

    private int[] _starts = new int[64];
    private LineBreakClass[] _classes = new LineBreakClass[64];
    private CategoryOfInterest[] _categories = new CategoryOfInterest[64];
    private byte[] _flags = new byte[64];
    private LineBreakOpportunity[] _breaks = new LineBreakOpportunity[64];
    private int _count;

    /// <summary>
    /// Returns the line break opportunities of a text, in order, the end of the text last.
    /// </summary>
    /// <param name="text">The text, in UTF-16.</param>
    /// <returns>The opportunities; empty for an empty text. Valid until the next call.</returns>
    public ReadOnlySpan<LineBreakOpportunity> Find(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return default;
        }

        TakeUnits(text);
        int breaks = 0;
        for (int i = 1; i < _count; i++)
        {
            Decision decision = Decide(i);
            if (decision != Decision.None)
            {
                if (breaks == _breaks.Length)
                {
                    Array.Resize(ref _breaks, breaks * 2);
                }

                _breaks[breaks++] = new LineBreakOpportunity(_starts[i], decision == Decision.Mandatory);
            }
        }

        if (breaks == _breaks.Length)
        {
            Array.Resize(ref _breaks, breaks * 2);
        }

        // LB3: always break at the end of the text.
        _breaks[breaks++] = new LineBreakOpportunity(text.Length, true);
        return _breaks.AsSpan(0, breaks);
    }

    private enum Decision : byte
    {
        None,
        Allowed,
        Mandatory,
    }

    // The units of the text, with rules LB1, LB9, and LB10 applied.
    private void TakeUnits(ReadOnlySpan<char> text)
    {
        _count = 0;
        for (int position = 0; position < text.Length; )
        {
            int start = position;
            int codePoint = UnicodeData.CodePointAt(text, position, out int length);
            position += length;
            uint properties = UnicodeData.Properties(codePoint);
            CategoryOfInterest category = UnicodeData.Category(properties);
            LineBreakClass resolved = Resolve(UnicodeData.LineBreak(properties), category);
            if (resolved is CM or ZWJ)
            {
                // LB9: X (CM | ZWJ)* is X, for any X but BK, CR, LF, NL, SP, and ZW.
                if (_count > 0 && _classes[_count - 1] is not (BK or CR or LF or NL or SP or ZW))
                {
                    _flags[_count - 1] = (byte)((_flags[_count - 1] & ~EndsWithZwj) | (resolved == ZWJ ? EndsWithZwj : 0));
                    continue;
                }

                // LB10: a remaining CM or ZWJ has the properties of U+0041 A: AL, Lu, East_Asian_Width Na, not pictographic.
                Add(start, AL, CategoryOfInterest.Other, resolved == ZWJ ? EndsWithZwj : (byte)0);
                continue;
            }

            byte flags = (byte)(
                (UnicodeData.IsPictographic(properties) ? Pictographic : 0)
                | (UnicodeData.IsEastAsian(properties) ? EastAsian : 0)
                | (codePoint == DottedCircle ? Dotted : 0)
            );
            Add(start, resolved, category, flags);
        }
    }

    // LB1, with no criteria from outside the algorithm: AI, SG, and XX are AL; SA is CM for Mn and Mc and AL otherwise; CJ is NS.
    private static LineBreakClass Resolve(LineBreakClass original, CategoryOfInterest category) =>
        original switch
        {
            AI or SG or XX => AL,
            SA => category is CategoryOfInterest.Mn or CategoryOfInterest.Mc ? CM : AL,
            CJ => NS,
            _ => original,
        };

    private void Add(int start, LineBreakClass resolved, CategoryOfInterest category, byte flags)
    {
        if (_count == _starts.Length)
        {
            int size = _count * 2;
            Array.Resize(ref _starts, size);
            Array.Resize(ref _classes, size);
            Array.Resize(ref _categories, size);
            Array.Resize(ref _flags, size);
        }

        _starts[_count] = start;
        _classes[_count] = resolved;
        _categories[_count] = category;
        _flags[_count] = flags;
        _count++;
    }

    // The rules LB4 to LB31, in order, at the boundary before unit `i`, between `a` (unit i - 1) and `b` (unit i).
    private Decision Decide(int i)
    {
        LineBreakClass a = _classes[i - 1];
        LineBreakClass b = _classes[i];

        // LB4, LB5: hard line breaks.
        if (a == BK)
        {
            return Decision.Mandatory;
        }

        if (a == CR && b == LF)
        {
            return Decision.None;
        }

        if (a is CR or LF or NL)
        {
            return Decision.Mandatory;
        }

        // LB6, LB7.
        if (b is BK or CR or LF or NL or SP or ZW)
        {
            return Decision.None;
        }

        // LB8: ZW SP* ÷
        if (LastBeforeSpaces(i - 1) is int z && _classes[z] == ZW)
        {
            return Decision.Allowed;
        }

        // LB8a: ZWJ ×
        if ((_flags[i - 1] & EndsWithZwj) != 0)
        {
            return Decision.None;
        }

        // LB11, LB12, LB12a.
        if (a == WJ || b == WJ || a == GL || (b == GL && a is not (SP or HY or HH)))
        {
            return Decision.None;
        }

        // LB13: × CL, × CP, × EX, × SY.
        if (b is CL or CP or EX or SY)
        {
            return Decision.None;
        }

        int beforeSpaces = LastBeforeSpaces(i - 1) ?? -1;
        LineBreakClass? beforeSpacesClass = beforeSpaces >= 0 ? _classes[beforeSpaces] : null;

        // LB14: OP SP* ×
        if (beforeSpacesClass == OP)
        {
            return Decision.None;
        }

        // LB15a: (sot | BK | CR | LF | NL | OP | QU | GL | SP | ZW) [\p{Pi}&QU] SP* ×
        if (beforeSpacesClass == QU && _categories[beforeSpaces] == CategoryOfInterest.Pi)
        {
            if (beforeSpaces == 0 || _classes[beforeSpaces - 1] is BK or CR or LF or NL or OP or QU or GL or SP or ZW)
            {
                return Decision.None;
            }
        }

        // LB15b: × [\p{Pf}&QU] ( SP | GL | WJ | CL | QU | CP | EX | IS | SY | BK | CR | LF | NL | ZW | eot )
        if (b == QU && _categories[i] == CategoryOfInterest.Pf)
        {
            if (i + 1 == _count || _classes[i + 1] is SP or GL or WJ or CL or QU or CP or EX or IS or SY or BK or CR or LF or NL or ZW)
            {
                return Decision.None;
            }
        }

        // LB15c: SP ÷ IS NU
        if (a == SP && b == IS && i + 1 < _count && _classes[i + 1] == NU)
        {
            return Decision.Allowed;
        }

        // LB15d: × IS
        if (b == IS)
        {
            return Decision.None;
        }

        // LB16: (CL | CP) SP* × NS; LB17: B2 SP* × B2
        if ((b == NS && beforeSpacesClass is CL or CP) || (b == B2 && beforeSpacesClass == B2))
        {
            return Decision.None;
        }

        // LB18: SP ÷
        if (a == SP)
        {
            return Decision.Allowed;
        }

        // LB19: × [ QU - \p{Pi} ], [ QU - \p{Pf} ] ×
        if ((b == QU && _categories[i] != CategoryOfInterest.Pi) || (a == QU && _categories[i - 1] != CategoryOfInterest.Pf))
        {
            return Decision.None;
        }

        // LB19a: unless surrounded by East Asian characters, do not break on either side of a quotation mark.
        if (b == QU)
        {
            if (!IsEastAsian(i - 1) || i + 1 == _count || !IsEastAsian(i + 1))
            {
                return Decision.None;
            }
        }

        if (a == QU)
        {
            if (!IsEastAsian(i) || i - 1 == 0 || !IsEastAsian(i - 2))
            {
                return Decision.None;
            }
        }

        // LB20: ÷ CB, CB ÷
        if (a == CB || b == CB)
        {
            return Decision.Allowed;
        }

        // LB20a: ( sot | BK | CR | LF | NL | SP | ZW | CB | GL ) ( HY | HH ) × ( AL | HL )
        if (a is HY or HH && b is AL or HL && (i - 1 == 0 || _classes[i - 2] is BK or CR or LF or NL or SP or ZW or CB or GL))
        {
            return Decision.None;
        }

        // LB21: × BA, × HH, × HY, × NS, BB ×
        if (b is BA or HH or HY or NS || a == BB)
        {
            return Decision.None;
        }

        // LB21a: HL (HY | HH) × [^HL]; LB21b: SY × HL; LB22: × IN
        if ((a is HY or HH && i >= 2 && _classes[i - 2] == HL && b != HL) || (a == SY && b == HL) || b == IN)
        {
            return Decision.None;
        }

        // LB23, LB23a, LB24.
        bool letters = (a is AL or HL && b == NU) || (a == NU && b is AL or HL);
        bool ideographic = (a == PR && b is ID or EB or EM) || (a is ID or EB or EM && b == PO);
        bool prefixed = (a is PR or PO && b is AL or HL) || (a is AL or HL && b is PR or PO);
        if (letters || ideographic || prefixed || IsNumber(i, a, b))
        {
            return Decision.None;
        }

        // LB26, LB27: Korean syllable blocks.
        bool hangul =
            (a == JL && b is JL or JV or H2 or H3)
            || (a is JV or H2 && b is JV or JT)
            || (a is JT or H3 && b == JT)
            || (a is JL or JV or JT or H2 or H3 && b == PO)
            || (a == PR && b is JL or JV or JT or H2 or H3);
        if (hangul)
        {
            return Decision.None;
        }

        // LB28: (AL | HL) × (AL | HL)
        if (a is AL or HL && b is AL or HL)
        {
            return Decision.None;
        }

        // LB28a: the orthographic syllables of Brahmic scripts.
        if (IsBrahmic(i))
        {
            return Decision.None;
        }

        // LB29: IS × (AL | HL); LB30: (AL | HL | NU) × [OP-$EastAsian], [CP-$EastAsian] × (AL | HL | NU)
        if (
            (a == IS && b is AL or HL)
            || (a is AL or HL or NU && b == OP && !IsEastAsian(i))
            || (a == CP && !IsEastAsian(i - 1) && b is AL or HL or NU)
        )
        {
            return Decision.None;
        }

        // LB30a: sot (RI RI)* RI × RI, [^RI] (RI RI)* RI × RI
        if (a == RI && b == RI)
        {
            int run = 0;
            for (int k = i - 1; k >= 0 && _classes[k] == RI; k--)
            {
                run++;
            }

            if (run % 2 == 1)
            {
                return Decision.None;
            }
        }

        // LB30b: EB × EM, [\p{Extended_Pictographic}&\p{Cn}] × EM
        if (b == EM && (a == EB || ((_flags[i - 1] & Pictographic) != 0 && _categories[i - 1] == CategoryOfInterest.Cn)))
        {
            return Decision.None;
        }

        // LB31: break everywhere else.
        return Decision.Allowed;
    }

    // The last unit at or before `k` that is not SP, or null when only spaces lie between it and the start.
    private int? LastBeforeSpaces(int k)
    {
        while (k >= 0 && _classes[k] == SP)
        {
            k--;
        }

        return k >= 0 ? k : null;
    }

    private bool IsEastAsian(int k) => (_flags[k] & EastAsian) != 0;

    // LB25: do not break numbers.
    private bool IsNumber(int i, LineBreakClass a, LineBreakClass b)
    {
        // NU ( SY | IS )* ( CL | CP )? × ( PO | PR ), and NU ( SY | IS )* × NU
        if (b is PO or PR or NU)
        {
            int k = i - 1;
            if (b != NU && _classes[k] is CL or CP)
            {
                k--;
            }

            while (k >= 0 && _classes[k] is SY or IS)
            {
                k--;
            }

            if (k >= 0 && _classes[k] == NU && (b != NU || _classes[i - 1] is SY or IS or NU))
            {
                return true;
            }
        }

        // ( PO | PR ) × OP NU, ( PO | PR ) × OP IS NU, ( PO | PR ) × NU
        if (a is PO or PR)
        {
            if (b == NU)
            {
                return true;
            }

            if (b == OP && i + 1 < _count)
            {
                LineBreakClass next = _classes[i + 1];
                if (next == NU || (next == IS && i + 2 < _count && _classes[i + 2] == NU))
                {
                    return true;
                }
            }
        }

        // HY × NU, IS × NU
        return b == NU && a is HY or IS;
    }

    // LB28a: AP × (AK | ◌ | AS); (AK | ◌ | AS) × (VF | VI); (AK | ◌ | AS) VI × (AK | ◌); (AK | ◌ | AS) × (AK | ◌ | AS) VF
    private bool IsBrahmic(int i)
    {
        bool aksara = IsAksara(i - 1, withStacker: true);
        LineBreakClass b = _classes[i];
        if (_classes[i - 1] == AP && IsAksara(i, withStacker: true))
        {
            return true;
        }

        if (aksara && b is VF or VI)
        {
            return true;
        }

        if (_classes[i - 1] == VI && i >= 2 && IsAksara(i - 2, withStacker: true) && IsAksara(i, withStacker: false))
        {
            return true;
        }

        return aksara && IsAksara(i, withStacker: true) && i + 1 < _count && _classes[i + 1] == VF;
    }

    // AK, the dotted circle U+25CC, or (with the stacker) AS.
    private bool IsAksara(int k, bool withStacker) => _classes[k] == AK || (_flags[k] & Dotted) != 0 || (withStacker && _classes[k] == AS);
}
