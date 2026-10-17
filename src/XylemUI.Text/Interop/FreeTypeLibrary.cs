namespace XylemUI.Text.Interop;

/// <summary>
/// The process's FreeType library instance, created on first use and kept for the process's lifetime.
/// </summary>
internal static unsafe class FreeTypeLibrary
{
    private static nint _handle;

    /// <summary>
    /// Gets the library handle. The text pipeline runs on one thread (the UI thread), so creating it lazily needs no lock.
    /// </summary>
    /// <exception cref="InvalidOperationException">FreeType could not start.</exception>
    public static nint Handle
    {
        get
        {
            if (_handle == 0)
            {
                Create();
            }

            return _handle;
        }
    }

    private static void Create()
    {
        nint handle;
        int error = FreeType.InitFreeType(&handle);
        if (error != 0)
        {
            throw new InvalidOperationException($"FreeType could not start (error {error}).");
        }

        _handle = handle;
    }
}
