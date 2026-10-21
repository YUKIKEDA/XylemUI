using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// Reads a font's family name from its <c>name</c> table with HarfBuzz, in the order FreeType picks it, so that
/// <see cref="Font.FamilyName"/> did not change when FreeType left the Windows build (design §5.0.3).
/// </summary>
/// <remarks>
/// FreeType 2.14.3 (<c>src/sfnt/sfobjs.c</c>, lines 1048 to 1068): when the OS/2 table's <c>fsSelection</c> has bit 8 (the
/// names follow the WWS model), the typographic family (name id 16), then the family (1); otherwise the WWS family (21), the
/// typographic family, then the family. For each id, FreeType takes an English name, else the first Windows name in another
/// language (<c>tt_face_get_name</c>, lines 195 to 208); this reads the English name HarfBuzz finds, else the first entry of the id
/// HarfBuzz lists.
/// </remarks>
internal static unsafe class FontNames
{
    private const uint Family1 = 1;
    private const uint TypographicFamily = 16;
    private const uint WwsFamily = 21;

    // The OS/2 table and the offset of fsSelection in it.
    private const uint Os2Tag = ('O' << 24) | ('S' << 16) | ('/' << 8) | '2';
    private const int FsSelectionOffset = 62;
    private const int WwsBit = 1 << 8;

    /// <summary>
    /// Returns the family name of a HarfBuzz face; empty when the font gives none.
    /// </summary>
    /// <param name="face">The face.</param>
    /// <returns>The name.</returns>
    public static string Family(nint face)
    {
        ReadOnlySpan<uint> order = FollowsWws(face) ? [TypographicFamily, Family1] : [WwsFamily, TypographicFamily, Family1];
        foreach (uint id in order)
        {
            if (Read(face, id) is { Length: > 0 } name)
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static bool FollowsWws(nint face)
    {
        nint blob = HarfBuzz.FaceReferenceTable(face, Os2Tag);
        try
        {
            uint length;
            byte* data = HarfBuzz.BlobGetData(blob, &length);
            return length >= FsSelectionOffset + 2 && (((data[FsSelectionOffset] << 8) | data[FsSelectionOffset + 1]) & WwsBit) != 0;
        }
        finally
        {
            HarfBuzz.BlobDestroy(blob);
        }
    }

    // The name with this id: English (HarfBuzz's default for no language), else the first entry of the id in any language.
    private static string? Read(nint face, uint id)
    {
        if (ReadIn(face, id, 0) is { } english)
        {
            return english;
        }

        uint count;
        HarfBuzz.NameEntry* entries = HarfBuzz.OtNameListNames(face, &count);
        for (uint i = 0; i < count; i++)
        {
            if (entries[i].NameId == id && ReadIn(face, id, entries[i].Language) is { } name)
            {
                return name;
            }
        }

        return null;
    }

    private static string? ReadIn(nint face, uint id, nint language)
    {
        uint size = 0;
        uint length = HarfBuzz.OtNameGetUtf16(face, id, language, &size, null);
        if (length == 0)
        {
            return null;
        }

        // The size in and out counts the terminating NUL, which the returned string leaves out.
        size = length + 1;
        char[] text = new char[size];
        fixed (char* pointer = text)
        {
            _ = HarfBuzz.OtNameGetUtf16(face, id, language, &size, pointer);
        }

        return new string(text, 0, (int)size);
    }
}
