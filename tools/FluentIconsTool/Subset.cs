using System.Runtime.InteropServices;

namespace XylemUI.Tools.FluentIcons;

/// <summary>
/// Keeps the glyphs of some code points of a font with HarfBuzz's subset library (<c>hb_subset_or_fail</c>), built for the build
/// machine by vendor/build.ps1 (design §5.0.4). The kept glyphs draw as they did: spike #716 compared their outlines and advances.
/// </summary>
internal static unsafe partial class Subset
{
    private const string HarfBuzz = "harfbuzz";
    private const string HarfBuzzSubset = "harfbuzz-subset";

    /// <summary>
    /// Returns the font with only the glyphs of the code points (and the glyphs they need).
    /// </summary>
    /// <param name="font">The font file.</param>
    /// <param name="codePoints">The code points to keep.</param>
    /// <returns>The kept font file.</returns>
    /// <exception cref="InvalidOperationException">HarfBuzz could not make the subset.</exception>
    public static byte[] Keep(byte[] font, IReadOnlyList<int> codePoints)
    {
        fixed (byte* data = font)
        {
            nint blob = BlobCreate(data, (uint)font.Length, 1, null, null);
            nint face = FaceCreate(blob, 0);
            nint input = SubsetInputCreateOrFail();
            try
            {
                if (input == 0)
                {
                    throw new InvalidOperationException("HarfBuzz could not make a subset input.");
                }

                nint set = SubsetInputUnicodeSet(input);
                foreach (int codePoint in codePoints)
                {
                    SetAdd(set, (uint)codePoint);
                }

                nint kept = SubsetOrFail(face, input);
                if (kept == 0)
                {
                    throw new InvalidOperationException("HarfBuzz could not keep the glyphs of the font.");
                }

                nint keptBlob = FaceReferenceBlob(kept);
                try
                {
                    uint length;
                    byte* bytes = BlobGetData(keptBlob, &length);
                    return new ReadOnlySpan<byte>(bytes, (int)length).ToArray();
                }
                finally
                {
                    BlobDestroy(keptBlob);
                    FaceDestroy(kept);
                }
            }
            finally
            {
                SubsetInputDestroy(input);
                FaceDestroy(face);
                BlobDestroy(blob);
            }
        }
    }

    [LibraryImport(HarfBuzz, EntryPoint = "hb_blob_create")]
    private static partial nint BlobCreate(byte* data, uint length, int mode, void* userData, void* destroy);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_blob_destroy")]
    private static partial void BlobDestroy(nint blob);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_blob_get_data")]
    private static partial byte* BlobGetData(nint blob, uint* length);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_face_create")]
    private static partial nint FaceCreate(nint blob, uint index);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_face_destroy")]
    private static partial void FaceDestroy(nint face);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_face_reference_blob")]
    private static partial nint FaceReferenceBlob(nint face);

    [LibraryImport(HarfBuzz, EntryPoint = "hb_set_add")]
    private static partial void SetAdd(nint set, uint codePoint);

    [LibraryImport(HarfBuzzSubset, EntryPoint = "hb_subset_input_create_or_fail")]
    private static partial nint SubsetInputCreateOrFail();

    [LibraryImport(HarfBuzzSubset, EntryPoint = "hb_subset_input_destroy")]
    private static partial void SubsetInputDestroy(nint input);

    [LibraryImport(HarfBuzzSubset, EntryPoint = "hb_subset_input_unicode_set")]
    private static partial nint SubsetInputUnicodeSet(nint input);

    [LibraryImport(HarfBuzzSubset, EntryPoint = "hb_subset_or_fail")]
    private static partial nint SubsetOrFail(nint face, nint input);
}
