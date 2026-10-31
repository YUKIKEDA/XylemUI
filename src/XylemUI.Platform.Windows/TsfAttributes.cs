using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads a composition's range and the display attributes of its clauses (design §4.7, "Drawing the text being composed"): the
/// attribute property (<c>GUID_PROP_ATTRIBUTE</c>) holds a GUID atom on each range, which the category manager turns into the
/// GUID and the display attribute manager into a <c>TF_DISPLAYATTRIBUTE</c>.
/// </summary>
internal static unsafe class TsfAttributes
{
    /// <summary>
    /// A window's managers that turn an attribute's atom into a display attribute: none in a test's document.
    /// </summary>
    /// <param name="categories">The category manager, or null.</param>
    /// <param name="displayAttributes">The display attribute manager, or null.</param>
    internal readonly struct TsfManagers(ITfCategoryMgr* categories, ITfDisplayAttributeMgr* displayAttributes)
    {
        /// <summary>
        /// Gets the category manager, or null.
        /// </summary>
        public ITfCategoryMgr* Categories { get; } = categories;

        /// <summary>
        /// Gets the display attribute manager, or null.
        /// </summary>
        public ITfDisplayAttributeMgr* DisplayAttributes { get; } = displayAttributes;
    }

    // msctf.h; checked in spike #510.
    private static readonly Guid PropAttribute = new(0x34b45670, 0x7526, 0x11d2, 0xa1, 0x47, 0x00, 0x10, 0x5a, 0x27, 0x99, 0xb5);

    /// <summary>
    /// Reads where a range starts and how long it is.
    /// </summary>
    /// <param name="range">The range.</param>
    /// <param name="start">Receives its start.</param>
    /// <param name="length">Receives its length.</param>
    /// <returns>Whether the range could be read.</returns>
    public static bool Extent(ITfRange* range, out int start, out int length)
    {
        (start, length) = (0, 0);
        ITfRangeACP* acp;
        if (range->QueryInterface(__uuidof<ITfRangeACP>(), (void**)&acp) < 0)
        {
            return false;
        }

        int anchor;
        int count;
        int result = acp->GetExtent(&anchor, &count);
        _ = acp->Release();
        (start, length) = (anchor, count);
        return result >= 0;
    }

    /// <summary>
    /// Reads the clauses of a composition from start to start + length: each range of the attribute property inside it, as the
    /// run its display attribute asks for, in the composition's positions.
    /// </summary>
    /// <param name="managers">The window's category and display attribute managers; without them the clauses have no attributes.</param>
    /// <param name="context">The document's context.</param>
    /// <param name="readOnly">The edit cookie to read with.</param>
    /// <param name="start">The composition's start in the document.</param>
    /// <param name="length">Its length.</param>
    /// <returns>The clauses, in order.</returns>
    public static ImeAttributeRun[] Clauses(TsfManagers managers, ITfContext* context, uint readOnly, int start, int length)
    {
        if (managers.Categories is null || managers.DisplayAttributes is null)
        {
            return [];
        }

        ITfProperty* property = null;
        Guid id = PropAttribute;
        if (context->GetProperty(&id, &property) < 0 || property is null)
        {
            return [];
        }

        var clauses = new List<ImeAttributeRun>();
        IEnumTfRanges* ranges = null;
        try
        {
            if (property->EnumRanges(readOnly, &ranges, null) < 0 || ranges is null)
            {
                return [];
            }

            ITfRange* range = null;
            uint fetched = 0;
            while (ranges->Next(1, &range, &fetched) == S.S_OK && fetched == 1)
            {
                try
                {
                    if (Read(managers, property, readOnly, range, start, length) is { } clause)
                    {
                        clauses.Add(clause);
                    }
                }
                finally
                {
                    _ = range->Release();
                    range = null;
                }
            }
        }
        finally
        {
            if (ranges is not null)
            {
                _ = ranges->Release();
            }

            _ = property->Release();
        }

        return [.. clauses];
    }

    /// <summary>
    /// Returns the run a display attribute asks for over a clause (design §4.7): the line's shape (a squiggle drawn solid until row
    /// R5), the bold line as thick, and the line's and the text's colors.
    /// </summary>
    /// <param name="attribute">The display attribute.</param>
    /// <param name="start">The clause's start in the composition.</param>
    /// <param name="end">Its end.</param>
    /// <returns>The run.</returns>
    public static ImeAttributeRun RunOf(in TF_DISPLAYATTRIBUTE attribute, int start, int end)
    {
        DecorationStyle? line = attribute.lsStyle switch
        {
            TF_DA_LINESTYLE.TF_LS_NONE => null,
            TF_DA_LINESTYLE.TF_LS_DOT => DecorationStyle.Dotted,
            TF_DA_LINESTYLE.TF_LS_DASH => DecorationStyle.Dashed,
            _ => DecorationStyle.Solid,
        };
        return new ImeAttributeRun(start, end, line, attribute.fBoldLine != false, ColorOf(attribute.crLine), ColorOf(attribute.crText));
    }

    // The color of a TF_DA_COLOR: none, a system color, or a COLORREF (0x00BBGGRR).
    private static Color? ColorOf(in TF_DA_COLOR color)
    {
        uint value;
        switch (color.type)
        {
            case TF_DA_COLORTYPE.TF_CT_COLORREF:
                value = color.Anonymous.cr;
                break;
            case TF_DA_COLORTYPE.TF_CT_SYSCOLOR:
                value = GetSysColor(color.Anonymous.nIndex);
                break;
            default:
                return null;
        }

        return new Color((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
    }

    // One range of the attribute property: its display attribute over the part of it inside the composition.
    private static ImeAttributeRun? Read(TsfManagers managers, ITfProperty* property, uint readOnly, ITfRange* range, int start, int length)
    {
        if (!Extent(range, out int at, out int count))
        {
            return null;
        }

        int from = Math.Clamp(at - start, 0, length);
        int to = Math.Clamp(at + count - start, from, length);
        if (to == from)
        {
            return null;
        }

        VARIANT value;
        if (((ITfReadOnlyProperty*)property)->GetValue(readOnly, range, &value) < 0)
        {
            return null;
        }

        try
        {
            if (value.vt != (ushort)VARENUM.VT_I4)
            {
                return null;
            }

            Guid guid;
            if (managers.Categories->GetGUID((uint)value.lVal, &guid) < 0)
            {
                return null;
            }

            ITfDisplayAttributeInfo* info = null;
            Guid owner;
            if (managers.DisplayAttributes->GetDisplayAttributeInfo(&guid, &info, &owner) < 0 || info is null)
            {
                return null;
            }

            TF_DISPLAYATTRIBUTE attribute;
            int result = info->GetAttributeInfo(&attribute);
            _ = info->Release();
            return result < 0 ? null : RunOf(attribute, from, to);
        }
        finally
        {
            _ = VariantClear(&value);
        }
    }
}
