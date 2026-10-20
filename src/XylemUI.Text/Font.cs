using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using XylemUI.Text.Interop;

namespace XylemUI.Text;

/// <summary>
/// A font file in memory, opened for shaping (HarfBuzz); a rasterizer (<see cref="IGlyphRasterizer"/>) draws its glyphs from the
/// same bytes (<see cref="Data"/>, design §4.1, §4.5, §5.0.3).
/// </summary>
/// <remarks>
/// Bytes given in memory are copied once into a pinned array; a font file is mapped read-only (<see cref="FromFile"/>). HarfBuzz
/// and the rasterizer read the bytes in place for the font's lifetime. A font belongs to one thread (the UI thread); its HarfBuzz
/// objects are not shared across threads here.
/// </remarks>
public sealed unsafe class Font : IDisposable
{
    private static int _nextId;

    // The bytes the libraries read: a pinned copy, or a read-only view of a mapped file.
    private readonly byte[]? _data;
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    private byte* _bytes;
    private int _length;
    private bool _acquired;
    private nint _blob;
    private nint _face;
    private nint _font;

    /// <summary>
    /// Initializes a new instance of the <see cref="Font"/> class from the bytes of a TrueType or OpenType font.
    /// </summary>
    /// <param name="data">The font file.</param>
    /// <param name="faceIndex">
    /// The face to open in a font collection (a <c>.ttc</c> file, such as several Windows Japanese fonts); 0 for a single font.
    /// </param>
    /// <exception cref="ArgumentException">The bytes are empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="faceIndex"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">HarfBuzz could not open the font, or the file has no such face.</exception>
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
    /// <exception cref="InvalidOperationException">HarfBuzz could not open the font, or the file has no such face.</exception>
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
    /// Gets the family name the font gives itself, read from its <c>name</c> table in FreeType's order (the WWS family, the
    /// typographic family, then the family, design §5.0.3); empty when it gives none.
    /// </summary>
    public string FamilyName { get; }

    /// <summary>
    /// Gets the face of the font file this font opened; 0 for a single font.
    /// </summary>
    public int FaceIndex { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the font was disposed.
    /// </summary>
    public bool IsDisposed => _font == 0;

    /// <summary>
    /// Gets the bytes of the font file. They are pinned or mapped, so they stay at the same address until the font is disposed:
    /// a rasterizer may hand that address to the OS without a copy, and must not read it after <see cref="Dispose"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The font was disposed.</exception>
    public ReadOnlySpan<byte> Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(_font == 0, this);
            return new ReadOnlySpan<byte>(_bytes, _length);
        }
    }

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
    /// Releases the HarfBuzz objects and the bytes.
    /// </summary>
    public void Dispose()
    {
        _bytes = null;
        _length = 0;
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

    // Opens HarfBuzz over bytes that stay valid until Dispose, and returns the family name.
    private string Open(byte* pointer, int length, int faceIndex)
    {
        _bytes = pointer;
        _length = length;
        FaceIndex = faceIndex;
        _blob = HarfBuzz.BlobCreate(pointer, (uint)length, HarfBuzz.MemoryModeReadOnly, null, null);

        // HarfBuzz opens any bytes and any index: an empty face for what it cannot read, and the only face of a single font for
        // every index. So the index is checked against the file's faces, and a face without glyphs is no font.
        if ((uint)faceIndex >= HarfBuzz.FaceCount(_blob))
        {
            Dispose();
            throw new InvalidOperationException($"The font file has no face {faceIndex}.");
        }

        _face = HarfBuzz.FaceCreate(_blob, (uint)faceIndex);
        if (HarfBuzz.FaceGetGlyphCount(_face) == 0)
        {
            Dispose();
            throw new InvalidOperationException($"HarfBuzz could not open face {faceIndex} of the font.");
        }

        UnitsPerEm = (int)HarfBuzz.FaceGetUpem(_face);
        _font = HarfBuzz.FontCreate(_face);

        // Positions in font units: the caller scales them to the size it draws at.
        HarfBuzz.FontSetScale(_font, UnitsPerEm, UnitsPerEm);
        return FontNames.Family(_face);
    }
}
