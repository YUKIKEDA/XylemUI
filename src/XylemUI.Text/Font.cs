using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// A font file in memory, opened for shaping (HarfBuzz) and rasterizing (FreeType) from the same bytes (design §4.1, §4.5).
/// </summary>
/// <remarks>
/// Bytes given in memory are copied once into a pinned array; a font file is mapped read-only (<see cref="FromFile"/>). Both
/// libraries read the bytes in place for the font's lifetime. A font belongs to one thread (the UI thread); HarfBuzz and FreeType
/// objects are not shared across threads here.
/// </remarks>
public sealed unsafe class Font : IDisposable
{
    private static int _nextId;

    // The bytes the libraries read: a pinned copy, or a read-only view of a mapped file.
    private readonly byte[]? _data;
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    private bool _acquired;
    private nint _blob;
    private nint _face;
    private nint _font;
    private FreeType.FaceRec* _ftFace;
    private uint _ftPixelSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="Font"/> class from the bytes of a TrueType or OpenType font.
    /// </summary>
    /// <param name="data">The font file.</param>
    /// <param name="faceIndex">
    /// The face to open in a font collection (a <c>.ttc</c> file, such as several Windows Japanese fonts); 0 for a single font.
    /// </param>
    /// <exception cref="ArgumentException">The bytes are empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">FreeType could not open the font, or the file has no such face.</exception>
    public Font(ReadOnlySpan<byte> data, int faceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        if (data.IsEmpty)
        {
            throw new ArgumentException("The font data is empty.", nameof(data));
        }

        _data = GC.AllocateUninitializedArray<byte>(data.Length, pinned: true);
        data.CopyTo(_data);

        // The array is pinned for its lifetime, so the pointer the libraries keep stays valid until Dispose.
        Id = Interlocked.Increment(ref _nextId);
        FamilyName = Open((byte*)Unsafe.AsPointer(ref _data[0]), _data.Length, faceIndex);
    }

    private Font(MemoryMappedFile mapping, MemoryMappedViewAccessor view, long length, int faceIndex)
    {
        _mapping = mapping;
        _view = view;
        Id = Interlocked.Increment(ref _nextId);
        byte* pointer = null;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _acquired = true;
        try
        {
            FamilyName = Open(pointer + view.PointerOffset, checked((int)length), faceIndex);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a font file by mapping it read-only, without copying it: the pages are the file's, shared with other mappings of it
    /// (DirectWrite maps the OS's fonts too), and only the parts the libraries read are loaded.
    /// </summary>
    /// <param name="path">The font file.</param>
    /// <param name="faceIndex">The face to open in a font collection (a <c>.ttc</c> file); 0 for a single font.</param>
    /// <returns>The font, which unmaps the file when disposed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    /// <exception cref="IOException">The file could not be opened or mapped.</exception>
    /// <exception cref="InvalidOperationException">FreeType could not open the font, or the file has no such face.</exception>
    public static Font FromFile(string path, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        long length = new FileInfo(path).Length;
        if (length == 0)
        {
            throw new ArgumentException("The font file is empty.", nameof(path));
        }

        MemoryMappedFile mapping = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        MemoryMappedViewAccessor? view = null;
        try
        {
            view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return new Font(mapping, view, length, faceIndex);
        }
        catch
        {
            view?.Dispose();
            mapping.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets an id unique to this font in the process, for caches keyed by font.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Gets the family name the font gives itself (FreeType's <c>family_name</c>); empty when it gives none.
    /// </summary>
    public string FamilyName { get; }

    /// <summary>
    /// Gets the font's units per em; shaped advances and offsets are in these units.
    /// </summary>
    public int UnitsPerEm { get; private set; }

    /// <summary>
    /// Gets the HarfBuzz font, scaled to font units.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The font was disposed.</exception>
    internal nint HarfBuzzFont
    {
        get
        {
            ObjectDisposedException.ThrowIf(_font == 0, this);
            return _font;
        }
    }

    /// <summary>
    /// Releases the HarfBuzz and FreeType objects.
    /// </summary>
    public void Dispose()
    {
        if (_ftFace is not null)
        {
            _ = FreeType.DoneFace(_ftFace);
            _ftFace = null;
        }

        if (_font != 0)
        {
            HarfBuzz.FontDestroy(_font);
            _font = 0;
        }

        if (_face != 0)
        {
            HarfBuzz.FaceDestroy(_face);
            _face = 0;
        }

        if (_blob != 0)
        {
            HarfBuzz.BlobDestroy(_blob);
            _blob = 0;
        }

        // The libraries are done with the bytes: the view can go.
        if (_acquired)
        {
            _view!.SafeMemoryMappedViewHandle.ReleasePointer();
            _acquired = false;
        }

        _view?.Dispose();
        _mapping?.Dispose();
    }

    /// <summary>
    /// Renders a glyph at a pixel size with FreeType and returns its slot, valid until the next render of this font.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The font was disposed.</exception>
    /// <exception cref="InvalidOperationException">FreeType could not render the glyph.</exception>
    internal FreeType.GlyphSlotRec* Render(uint glyphId, int pixelSize)
    {
        ObjectDisposedException.ThrowIf(_ftFace is null, this);
        if (_ftPixelSize != (uint)pixelSize)
        {
            int sizeError = FreeType.SetPixelSizes(_ftFace, 0, (uint)pixelSize);
            if (sizeError != 0)
            {
                throw new InvalidOperationException($"FreeType could not set the pixel size {pixelSize} (error {sizeError}).");
            }

            _ftPixelSize = (uint)pixelSize;
        }

        // No hinting: the coverage follows the designed outline, so its box is the outline's box rounded out to whole pixels. Whether
        // UI text is hinted is decided with the text's rendering quality, after S8b.
        int error = FreeType.LoadGlyph(_ftFace, glyphId, FreeType.LoadRender | FreeType.LoadNoHinting);
        if (error != 0)
        {
            throw new InvalidOperationException($"FreeType could not render glyph {glyphId} (error {error}).");
        }

        return _ftFace->Glyph;
    }

    // Opens HarfBuzz and FreeType over bytes that stay valid until Dispose, and returns the family name.
    private string Open(byte* pointer, int length, int faceIndex)
    {
        _blob = HarfBuzz.BlobCreate(pointer, (uint)length, HarfBuzz.MemoryModeReadOnly, null, null);
        _face = HarfBuzz.FaceCreate(_blob, (uint)faceIndex);
        UnitsPerEm = (int)HarfBuzz.FaceGetUpem(_face);
        _font = HarfBuzz.FontCreate(_face);

        // Positions in font units: the caller scales them to the size it draws at.
        HarfBuzz.FontSetScale(_font, UnitsPerEm, UnitsPerEm);

        FreeType.FaceRec* face;
        int error = FreeType.NewMemoryFace(FreeTypeLibrary.Handle, pointer, new CLong(length), new CLong(faceIndex), &face);
        if (error != 0)
        {
            Dispose();
            throw new InvalidOperationException($"FreeType could not open the font (error {error}).");
        }

        _ftFace = face;
        return face->FamilyName is null ? string.Empty : Marshal.PtrToStringUTF8((nint)face->FamilyName) ?? string.Empty;
    }
}
