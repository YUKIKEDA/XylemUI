namespace XylemUI;

/// <summary>
/// The root wrapper of a process's windows (design §13.2, "根で置くもの"): the theme's function that the desktop host runs
/// around each window's root function, inside the locale and the OS's appearance. The code that the source generator writes for
/// the app's <c>[assembly: WindowTheme(typeof(T))]</c> sets it; an app does not call it itself. The headless app does not read it,
/// and takes its wrapper from <c>HeadlessOptions.RootWrapper</c>.
/// </summary>
public static class WindowRoot
{
    private static Func<Func<Element>, Element>? _wrapper;

    /// <summary>
    /// Gets the wrapper set with <see cref="SetWrapper"/>; <see langword="null"/> when none is, and the windows are not wrapped.
    /// </summary>
    internal static Func<Func<Element>, Element>? Wrapper => Volatile.Read(ref _wrapper);

    /// <summary>
    /// Sets the function that wraps each window's root function: it is given the root function, calls it, and returns the element
    /// the window holds. It is set once in a process.
    /// </summary>
    /// <param name="wrapper">The wrapper, such as <c>FluentRoot.Wrap</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wrapper"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A wrapper is already set.</exception>
    public static void SetWrapper(Func<Func<Element>, Element> wrapper)
    {
        ArgumentNullException.ThrowIfNull(wrapper);

        // A module initializer runs on whichever thread first touches the app's assembly, not always the UI thread.
        if (Interlocked.CompareExchange(ref _wrapper, wrapper, null) is not null)
        {
            ThrowHelper.ThrowRootWrapperAlreadySet();
        }
    }
}
