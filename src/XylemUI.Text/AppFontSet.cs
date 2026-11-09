using System.Runtime.InteropServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// The fonts an app or a theme registered from bytes (design §4.5, "App fonts"), which a font provider looks in before the OS's
/// fonts. Core's <c>AppFonts.Add</c> is the public entry; it checks the UI thread, so this set has no lock.
/// </summary>
/// <remarks>
/// A file is pinned where the app gave it and kept until the process ends: nothing is copied, and nothing is removed, as in GPUI
/// and Flutter. <see cref="Generation"/> grows with each file, so that a provider that keeps a collection of the fonts (DirectWrite's)
/// sees when it must make it again, and a tree sees that its text is to be measured again.
/// </remarks>
internal static unsafe class AppFontSet
{
    private const uint Os2Tag = ('O' << 24) | ('S' << 16) | ('/' << 8) | '2';

    // The OS/2 table's usWeightClass, usWidthClass, and fsSelection (OpenType, "OS/2 — OS/2 and Windows Metrics Table").
    private const int WeightClassOffset = 4;
    private const int WidthClassOffset = 6;
    private const int FsSelectionOffset = 62;
    private const int ItalicBit = 1 << 0;
    private const int ObliqueBit = 1 << 9;

    private static readonly List<AppFontFile> _files = [];
    private static readonly List<AppFontFace> _faces = [];

    /// <summary>
    /// Gets the number of files registered: 0 until the first, then one more for each.
    /// </summary>
    public static int Generation => _files.Count;

    /// <summary>
    /// Gets the files registered, in the order they were added.
    /// </summary>
    public static IReadOnlyList<AppFontFile> Files => _files;

    /// <summary>
    /// Gets the faces of every file registered, in the order they were added.
    /// </summary>
    public static IReadOnlyList<AppFontFace> Faces => _faces;

    /// <summary>
    /// Registers every face of a font file (each face of a <c>.ttc</c>), reading its family, weight, slant, and stretch from the
    /// file's <c>name</c> and <c>OS/2</c> tables. The array is pinned and kept, not copied.
    /// </summary>
    /// <param name="data">The font file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The bytes are empty, or a face of them is not a font HarfBuzz can open.</exception>
    public static void Add(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The font data is empty.", nameof(data));
        }

        GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        var pointer = (byte*)pin.AddrOfPinnedObject();
        var opened = new List<AppFontFace>();
        try
        {
            nint blob = HarfBuzz.BlobCreate(pointer, (uint)data.Length, HarfBuzz.MemoryModeReadOnly, null, null);
            uint count = HarfBuzz.FaceCount(blob);
            HarfBuzz.BlobDestroy(blob);
            if (count == 0)
            {
                throw new ArgumentException("The bytes are not a font file.", nameof(data));
            }

            var file = new AppFontFile(data, _files.Count);
            for (int index = 0; index < count; index++)
            {
                Font font;
                try
                {
                    font = new Font(pointer, data.Length, index);
                }
                catch (InvalidOperationException e)
                {
                    throw new ArgumentException($"Face {index} of the bytes is not a font.", nameof(data), e);
                }

                opened.Add(Describe(file, font));
            }

            _files.Add(file);
            _faces.AddRange(opened);
        }
        catch
        {
            foreach (AppFontFace face in opened)
            {
                face.Font.Dispose();
            }

            pin.Free();
            throw;
        }
    }

    /// <summary>
    /// Returns a value indicating whether a face of <paramref name="family"/> is registered.
    /// </summary>
    /// <param name="family">The family, compared without case, as DirectWrite compares family names.</param>
    /// <returns><see langword="true"/> when one is.</returns>
    public static bool HasFamily(string? family) =>
        family is not null && Find(family, FontWeight.Normal, FontSlant.Normal, FontStretch.Normal) is not null;

    /// <summary>
    /// Finds the face of <paramref name="family"/> closest to a weight, slant, and stretch, by CSS's font matching (CSS Fonts 4,
    /// §5.2): the nearest stretch, then the slant (an italic asked for takes an oblique before a normal face, and the other way
    /// round), then the weight (for 400 to 500, the weights up to 500 first; above 500, the heavier first; below 400, the lighter
    /// first).
    /// </summary>
    /// <param name="family">The family, compared without case.</param>
    /// <param name="weight">The weight asked for.</param>
    /// <param name="slant">The slant asked for.</param>
    /// <param name="stretch">The stretch asked for.</param>
    /// <returns>The face, or <see langword="null"/> when no face of the family is registered.</returns>
    public static AppFontFace? Find(string family, FontWeight weight, FontSlant slant, FontStretch stretch)
    {
        AppFontFace? best = null;
        foreach (AppFontFace face in _faces)
        {
            if (
                string.Equals(face.Family, family, StringComparison.OrdinalIgnoreCase) && (best is null || Closer(face, best, weight, slant, stretch))
            )
            {
                best = face;
            }
        }

        return best;
    }

    // Whether a is a closer match than b.
    private static bool Closer(AppFontFace a, AppFontFace b, FontWeight weight, FontSlant slant, FontStretch stretch)
    {
        int stretchA = StretchRank(a.Stretch, stretch);
        int stretchB = StretchRank(b.Stretch, stretch);
        if (stretchA != stretchB)
        {
            return stretchA < stretchB;
        }

        int slantA = SlantRank(a.Slant, slant);
        int slantB = SlantRank(b.Slant, slant);
        if (slantA != slantB)
        {
            return slantA < slantB;
        }

        return WeightRank(a.Weight.Value, weight.Value) < WeightRank(b.Weight.Value, weight.Value);
    }

    // Narrower stretches first for a normal or narrower stretch asked for, wider ones first otherwise.
    private static int StretchRank(FontStretch face, FontStretch asked)
    {
        int distance = Math.Abs((int)face - (int)asked);
        bool preferred = asked <= FontStretch.Normal ? face <= asked : face >= asked;
        return preferred ? distance : 100 + distance;
    }

    private static int SlantRank(FontSlant face, FontSlant asked) =>
        face == asked ? 0
        : asked == FontSlant.Normal ? (face == FontSlant.Oblique ? 1 : 2)
        : face == FontSlant.Normal ? 2
        : 1;

    private static int WeightRank(int face, int asked)
    {
        if (face == asked)
        {
            return 0;
        }

        if (asked is >= 400 and <= 500)
        {
            // Up to 500 first, then lighter, then heavier.
            return face > asked && face <= 500 ? face - asked
                : face < asked ? 1000 + (asked - face)
                : 2000 + (face - asked);
        }

        return asked > 500 ? (face > asked ? face - asked : 1000 + (asked - face)) : (face < asked ? asked - face : 1000 + (face - asked));
    }

    private static AppFontFace Describe(AppFontFile file, Font font)
    {
        int weightClass = 400;
        int widthClass = 5;
        int selection = 0;
        nint table = HarfBuzz.FaceReferenceTable(font.HarfBuzzFace, Os2Tag);
        try
        {
            uint length;
            byte* data = HarfBuzz.BlobGetData(table, &length);
            if (length >= FsSelectionOffset + 2)
            {
                weightClass = Read16(data, WeightClassOffset);
                widthClass = Read16(data, WidthClassOffset);
                selection = Read16(data, FsSelectionOffset);
            }
        }
        finally
        {
            HarfBuzz.BlobDestroy(table);
        }

        FontSlant slant =
            (selection & ItalicBit) != 0 ? FontSlant.Italic
            : (selection & ObliqueBit) != 0 ? FontSlant.Oblique
            : FontSlant.Normal;
        return new AppFontFace(
            file,
            font,
            font.FamilyName,
            new FontWeight(Math.Clamp(weightClass, 1, 999)),
            slant,
            (FontStretch)Math.Clamp(widthClass, 1, 9)
        );
    }

    private static int Read16(byte* data, int offset) => (data[offset] << 8) | data[offset + 1];
}

/// <summary>
/// A font file an app registered (<see cref="AppFontSet"/>): the bytes, pinned for the process.
/// </summary>
/// <param name="Data">The file's bytes.</param>
/// <param name="Index">The file's place in <see cref="AppFontSet.Files"/>, a key a provider can give the OS for it.</param>
internal sealed record AppFontFile(byte[] Data, int Index);

/// <summary>
/// One face of a registered font file, with what the file's tables say of it.
/// </summary>
/// <param name="File">The file the face is in.</param>
/// <param name="Font">The face opened for shaping and drawing, kept for the process.</param>
/// <param name="Family">The family the face gives itself (<see cref="Font.FamilyName"/>).</param>
/// <param name="Weight">The weight (<c>OS/2.usWeightClass</c>).</param>
/// <param name="Slant">The slant (<c>OS/2.fsSelection</c>'s italic and oblique bits).</param>
/// <param name="Stretch">The stretch (<c>OS/2.usWidthClass</c>).</param>
internal sealed record AppFontFace(AppFontFile File, Font Font, string Family, FontWeight Weight, FontSlant Slant, FontStretch Stretch);
