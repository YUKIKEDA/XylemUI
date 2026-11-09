using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Text;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.Windows.Windows;
using static XylemUI.Platform.Windows.Com;

namespace XylemUI.Platform.Windows;

/// <summary>
/// The fonts of Windows for a text, found by DirectWrite's system font fallback a range at a time (design §4.5, roadmap row F1).
/// </summary>
/// <remarks>
/// A font DirectWrite returns is opened once from its file and face index, mapped without a copy (<see cref="Font.FromFile"/>),
/// and kept until the provider is disposed. A font whose file is not on the disk (a cloud font Windows has not downloaded)
/// counts as no font, so its characters get the bundled font. Used on one thread, as <see cref="Font"/> is.
/// <para>
/// The fonts an app registered (<see cref="AppFontSet"/>, design §4.5, "App fonts") are a collection of their own, made from their
/// bytes in memory without a copy (<c>IDWriteInMemoryFontFileLoader</c> and a font set, as GPUI's <c>add_fonts</c> makes it). A
/// request for a family registered there passes that collection to <c>MapCharacters</c> as the base collection, so the registered
/// font comes before the OS's family of the same name, and the characters it lacks go to the system's fallback.
/// </para>
/// </remarks>
public sealed unsafe class DirectWriteFontProvider : IFontProvider, IDisposable
{
    // The fonts DirectWrite returned, by the reference key of the font's file (the local loader's key holds the path) and the
    // face index, compared in place so that finding one allocates nothing. DirectWrite returns a new IDWriteFont for every match
    // (a cache by the pointer missed every time and a split of a short text allocated 260 bytes, Windows 11 26300), so the
    // pointer cannot be the key. The list holds one entry per font file and face used, a few dozen at most.
    private readonly List<(byte[] Key, uint Face, Font Font)> _fonts = [];
    private readonly Dictionary<TextLanguage, char[]> _locales = [];
    private readonly char[] _defaultFamily;

    // The app font files given to the in-memory loader (IDWriteFontFile*) and their reference keys, by the index of the file in
    // AppFontSet.Files. Each file is given once: another reference to the same bytes gets another key.
    private readonly List<nint> _appFiles = [];
    private readonly List<byte[]> _appKeys = [];
    private IDWriteFactory5* _factory;
    private IDWriteInMemoryFontFileLoader* _loader;
    private IDWriteFontCollection1* _appCollection;
    private int _appGeneration;
    private IDWriteFontFallback* _fallback;
    private IDWriteFontCollection* _collection;
    private IDWriteTextAnalysisSource* _sourcePointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectWriteFontProvider"/> class with the system's font fallback and fonts.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// DirectWrite could not create the factory, the fallback, or the collection (Windows 10 version 1703 and later have them).
    /// </exception>
    public DirectWriteFontProvider()
    {
        DefaultFamily = SystemUiFontFamily();
        _defaultFamily = GC.AllocateArray<char>(DefaultFamily.Length + 1, pinned: true);
        DefaultFamily.CopyTo(_defaultFamily);
        try
        {
            IDWriteFactory5* factory;
            Guid iid = IID.IID_IDWriteFactory5;
            ThrowIfFailed(DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, &iid, (IUnknown**)&factory), "DWriteCreateFactory");
            _factory = factory;
            IDWriteFontFallback* fallback;
            ThrowIfFailed(_factory->GetSystemFontFallback(&fallback), "IDWriteFactory2::GetSystemFontFallback");
            _fallback = fallback;
            IDWriteFontCollection* collection;
            ThrowIfFailed(_factory->GetSystemFontCollection(&collection, BOOL.FALSE), "IDWriteFactory::GetSystemFontCollection");
            _collection = collection;
            _sourcePointer = TextAnalysisSource.Create();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the family of the OS's UI font, which a <see cref="FontRequest"/> without a family asks for: the icon title font of
    /// <c>SystemParametersInfoW(SPI_GETICONTITLELOGFONT)</c>, as GPUI takes it, or Segoe UI when the call fails (design §4.5).
    /// </summary>
    public string DefaultFamily { get; }

    /// <inheritdoc />
    public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request)
    {
        ObjectDisposedException.ThrowIf(_fallback is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, text.Length);

        uint mappedLength;
        IDWriteFont* mapped;
        float scale;
        char[] locale = LocaleOf(language);
        bool app = AppFontSet.Generation > 0 && AppFontSet.HasFamily(request.Family);
        IDWriteFontCollection* collection = app ? (IDWriteFontCollection*)AppCollection() : _collection;
        DWRITE_FONT_STYLE style = request.Slant switch
        {
            FontSlant.Italic => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC,
            FontSlant.Oblique => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_OBLIQUE,
            _ => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
        };

        // A fixed string is null-terminated (the C# specification for fixed on a string); the default family is kept so.
        fixed (char* textPointer = text)
        {
            fixed (char* localePointer = locale)
            {
                fixed (char* requested = request.Family)
                {
                    fixed (char* fallbackFamily = _defaultFamily)
                    {
                        char* family = request.Family is null ? fallbackFamily : requested;
                        TextAnalysisSource.Set(_sourcePointer, textPointer, (uint)text.Length, localePointer);
                        try
                        {
                            ThrowIfFailed(
                                _fallback->MapCharacters(
                                    _sourcePointer,
                                    (uint)start,
                                    (uint)(text.Length - start),
                                    collection,
                                    family,
                                    (DWRITE_FONT_WEIGHT)request.Weight.Value,
                                    style,
                                    (DWRITE_FONT_STRETCH)(int)request.Stretch,
                                    &mappedLength,
                                    &mapped,
                                    &scale
                                ),
                                "IDWriteFontFallback::MapCharacters"
                            );
                        }
                        finally
                        {
                            TextAnalysisSource.Clear(_sourcePointer);
                        }
                    }
                }
            }
        }

        // MapCharacters returns a null font with the number of characters no font renders (Microsoft Learn), at least 1.
        Font? font = mapped is null ? null : FontOf(mapped, app);
        return new FontMatch(font, (int)mappedLength, font is null ? 1 : scale);
    }

    /// <summary>
    /// Releases the fonts and the DirectWrite objects.
    /// </summary>
    public void Dispose()
    {
        foreach ((_, _, Font font) in _fonts)
        {
            font.Dispose();
        }

        _fonts.Clear();
        Release(ref _appCollection);
        foreach (nint file in _appFiles)
        {
            _ = ((IDWriteFontFile*)file)->Release();
        }

        _appFiles.Clear();
        _appKeys.Clear();
        if (_loader is not null)
        {
            _ = _factory->UnregisterFontFileLoader((IDWriteFontFileLoader*)_loader);
            Release(ref _loader);
        }

        if (_sourcePointer is not null)
        {
            TextAnalysisSource.Free(_sourcePointer);
            _sourcePointer = null;
        }

        Release(ref _collection);
        Release(ref _fallback);
        Release(ref _factory);
    }

    private static string SystemUiFontFamily()
    {
        LOGFONTW font;
        if (SystemParametersInfoW(SPI.SPI_GETICONTITLELOGFONT, (uint)sizeof(LOGFONTW), &font, 0) != BOOL.FALSE)
        {
            string name = new((char*)&font.lfFaceName);
            if (name.Length > 0)
            {
                return name;
            }
        }

        // https://learn.microsoft.com/windows/win32/uxguide/vis-fonts: Segoe UI is the font for user interface text.
        return "Segoe UI";
    }

    // The locale DirectWrite reads to pick a Han character's font: the language's BCP 47 tag, kept null-terminated and pinned.
    private char[] LocaleOf(TextLanguage language)
    {
        if (!_locales.TryGetValue(language, out char[]? locale))
        {
            string tag = language.Tag;
            locale = GC.AllocateArray<char>(tag.Length + 1, pinned: true);
            tag.CopyTo(locale);
            _locales.Add(language, locale);
        }

        return locale;
    }

    // Takes the reference MapCharacters returned, and releases it. A font whose file is not on the disk (a cloud font Windows has not downloaded: its loader is not the local one) cannot be
    // mapped, so it counts as no font, and the range goes to the bundled font: the fallback still ends (design §4.5), where
    // throwing would stop every split of such a text (PR #152's review). When the request's base collection was the app fonts'
    // (app), a registered file is mapped back to its registered font.
    private Font? FontOf(IDWriteFont* mapped, bool app)
    {
        IDWriteFontFace* face = null;
        IDWriteFontFile* file = null;
        try
        {
            ThrowIfFailed(mapped->CreateFontFace(&face), "IDWriteFont::CreateFontFace");
            uint files = 1;
            ThrowIfFailed(face->GetFiles(&files, &file), "IDWriteFontFace::GetFiles");
            void* key;
            uint keySize;
            ThrowIfFailed(file->GetReferenceKey(&key, &keySize), "IDWriteFontFile::GetReferenceKey");
            var keyBytes = new ReadOnlySpan<byte>(key, (int)keySize);
            uint index = face->GetIndex();

            // Only a request with the app fonts as its base collection can get one of them back: the system's fallback maps to
            // the OS's fonts, so a request of another family asks the file nothing more (PR #713's review).
            if (app && AppFontOf(file, keyBytes, index) is { } registered)
            {
                return registered;
            }

            foreach ((byte[] known, uint knownFace, Font font) in _fonts)
            {
                if (knownFace == index && keyBytes.SequenceEqual(known))
                {
                    return font;
                }
            }

            if (PathOf(file, key, keySize) is not { } path)
            {
                return null;
            }

            var opened = Font.FromFile(path, (int)index);
            _fonts.Add((keyBytes.ToArray(), index, opened));
            return opened;
        }
        finally
        {
            Release(ref file);
            Release(ref face);
            _ = mapped->Release();
        }
    }

    // The collection of the app fonts, made again when a file was registered since it was last made.
    private IDWriteFontCollection1* AppCollection()
    {
        if (_appGeneration == AppFontSet.Generation)
        {
            return _appCollection;
        }

        if (_loader is null)
        {
            IDWriteInMemoryFontFileLoader* loader;
            ThrowIfFailed(_factory->CreateInMemoryFontFileLoader(&loader), "IDWriteFactory5::CreateInMemoryFontFileLoader");
            _loader = loader;
            ThrowIfFailed(_factory->RegisterFontFileLoader((IDWriteFontFileLoader*)_loader), "IDWriteFactory::RegisterFontFileLoader");
        }

        IDWriteFontSetBuilder1* builder = null;
        IDWriteFontSet* set = null;
        try
        {
            ThrowIfFailed(_factory->CreateFontSetBuilder(&builder), "IDWriteFactory5::CreateFontSetBuilder");
            IReadOnlyList<AppFontFile> files = AppFontSet.Files;
            for (int i = 0; i < files.Count; i++)
            {
                AddAppFile(builder, files[i]);
            }

            ThrowIfFailed(builder->CreateFontSet(&set), "IDWriteFontSetBuilder::CreateFontSet");
            IDWriteFontCollection1* collection;
            ThrowIfFailed(_factory->CreateFontCollectionFromFontSet(set, &collection), "IDWriteFactory5::CreateFontCollectionFromFontSet");
            Release(ref _appCollection);
            _appCollection = collection;
            _appGeneration = files.Count;
            return _appCollection;
        }
        finally
        {
            Release(ref set);
            Release(ref builder);
        }
    }

    // Adds one registered file to the builder, giving the loader a reference to its pinned bytes the first time.
    private void AddAppFile(IDWriteFontSetBuilder1* builder, AppFontFile app)
    {
        if (app.Index == _appFiles.Count)
        {
            IDWriteFontFile* created;
            fixed (byte* bytes = app.Data)
            {
                // The set pinned the array for the process, so DirectWrite may keep its address.
                ThrowIfFailed(
                    _loader->CreateInMemoryFontFileReference(
                        (IDWriteFactory*)_factory,
                        bytes,
                        (uint)app.Data.Length,
                        FontDataOwner.Instance,
                        &created
                    ),
                    "IDWriteInMemoryFontFileLoader::CreateInMemoryFontFileReference"
                );
            }

            _appFiles.Add((nint)created);
            void* key;
            uint keySize;
            ThrowIfFailed(created->GetReferenceKey(&key, &keySize), "IDWriteFontFile::GetReferenceKey");
            _appKeys.Add(new ReadOnlySpan<byte>(key, (int)keySize).ToArray());
        }

        ThrowIfFailed(builder->AddFontFile((IDWriteFontFile*)_appFiles[app.Index]), "IDWriteFontSetBuilder1::AddFontFile");
    }

    // The registered font of a file the in-memory loader holds, or null for a file of another loader.
    private Font? AppFontOf(IDWriteFontFile* file, ReadOnlySpan<byte> key, uint faceIndex)
    {
        if (_loader is null)
        {
            return null;
        }

        IDWriteFontFileLoader* loader = null;
        try
        {
            ThrowIfFailed(file->GetLoader(&loader), "IDWriteFontFile::GetLoader");
            if (loader != (IDWriteFontFileLoader*)_loader)
            {
                return null;
            }
        }
        finally
        {
            Release(ref loader);
        }

        for (int index = 0; index < _appKeys.Count; index++)
        {
            if (key.SequenceEqual(_appKeys[index]))
            {
                foreach (AppFontFace face in AppFontSet.Faces)
                {
                    if (face.File.Index == index && face.Font.FaceIndex == (int)faceIndex)
                    {
                        return face.Font;
                    }
                }
            }
        }

        return null;
    }

    // The file's path, or null when its loader is not the local one.
    private static string? PathOf(IDWriteFontFile* file, void* key, uint keySize)
    {
        IDWriteFontFileLoader* loader = null;
        IDWriteLocalFontFileLoader* local = null;
        try
        {
            ThrowIfFailed(file->GetLoader(&loader), "IDWriteFontFile::GetLoader");
            Guid iid = IID.IID_IDWriteLocalFontFileLoader;
            if (loader->QueryInterface(&iid, (void**)&local) < 0)
            {
                return null;
            }

            uint length;
            ThrowIfFailed(local->GetFilePathLengthFromKey(key, keySize, &length), "IDWriteLocalFontFileLoader::GetFilePathLengthFromKey");
            string path = new('\0', (int)length);
            fixed (char* buffer = path)
            {
                ThrowIfFailed(local->GetFilePathFromKey(key, keySize, buffer, length + 1), "IDWriteLocalFontFileLoader::GetFilePathFromKey");
            }

            return path;
        }
        finally
        {
            Release(ref local);
            Release(ref loader);
        }
    }
}
