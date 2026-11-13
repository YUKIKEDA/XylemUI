using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// The fonts an app or a theme gives from the bytes of their files (design §4.5, "App fonts"): a text whose family names one of
/// them is drawn with it before the OS's fonts are looked in, as in GPUI, Flutter, and Avalonia.
/// </summary>
/// <remarks>
/// <para>
/// A file's family, weight, slant, and stretch come from its own <c>name</c> and <c>OS/2</c> tables, and a family the OS also has
/// is drawn with the registered one. The characters a registered font lacks go on to the fallback as before: the OS's fonts, then
/// the bundled font.
/// </para>
/// <para>
/// A registered font stays until the process ends; GPUI and Flutter cannot remove one either. Registering one measures every text
/// of the open windows again in their next frame, since a text measured before keeps the font it chose then.
/// </para>
/// </remarks>
public static class AppFonts
{
    // The UI thread's trees that measure text, held weakly: a closed window's tree goes when nothing else holds it.
    [ThreadStatic]
    private static List<WeakReference<UiTree>>? _trees;

    /// <summary>
    /// Registers every font in a font file (each face of a <c>.ttc</c>). The array is kept and not copied, so it must not be
    /// changed afterwards; a file read from an embedded resource is copied once into the array that is passed.
    /// </summary>
    /// <param name="data">The bytes of a TrueType or OpenType file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The bytes are empty, or are not a font.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not the UI thread (design §2.12).</exception>
    [UiThreadOnly]
    public static void Add(byte[] data)
    {
        UiThread.VerifyAccess();
        AppFontSet.Add(data);
        if (_trees is not { } trees)
        {
            return;
        }

        for (int index = trees.Count - 1; index >= 0; index--)
        {
            if (trees[index].TryGetTarget(out UiTree? tree))
            {
                tree.MeasureTextAgain();
            }
            else
            {
                trees.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Keeps a tree that measures text, so that <see cref="Add"/> measures its text again; a tree of another thread than the UI
    /// thread is caught when its text is next measured (<see cref="TextLeaf.FontGeneration"/>).
    /// </summary>
    /// <param name="tree">The tree.</param>
    internal static void Track(UiTree tree)
    {
        List<WeakReference<UiTree>> trees = _trees ??= [];

        // The references of collected trees go before the list grows, so that it holds about the trees alive.
        if (trees.Count == trees.Capacity)
        {
            _ = trees.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        trees.Add(new WeakReference<UiTree>(tree));
    }
}
