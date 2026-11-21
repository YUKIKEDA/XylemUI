namespace XylemUI;

/// <summary>
/// The root wrapper of a process's windows (design §13.2, "根で置くもの"): the theme's function that the desktop host runs
/// around each window's root function, inside the locale and the OS's appearance. The code that the source generator writes for
/// the app's <c>[assembly: WindowTheme(typeof(T))]</c> sets it in a module initializer, before the first window opens; an app does
/// not call it itself. The headless app does not read it, and takes its wrapper from <c>HeadlessOptions.RootWrapper</c>.
/// </summary>
public static class WindowRoot
{
    // The wrapper and whether a window has read it, swapped as one value, so that a set and a read on two threads see one order.
    private static State _state = new(null, false);

    /// <summary>
    /// Gets a value indicating whether a desktop window has read the wrapper (<see cref="ReadWrapper"/>); from then on a wrapper
    /// other than the one set is refused.
    /// </summary>
    internal static bool IsRead => Volatile.Read(ref _state).Read;

    /// <summary>
    /// Sets the function that wraps each window's root function: it is given a function that runs the root function, calls it
    /// exactly once, and returns the element the window holds. It is set once in a process, before the first window opens; the
    /// same method set again does nothing (design §13.2, "包みは 1 つ").
    /// </summary>
    /// <param name="wrapper">The wrapper, such as <c>FluentRoot.Wrap</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wrapper"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A window has already read the wrapper, or another wrapper is already set; a wrapper equal to the one set (the same method
    /// and target) is not refused.
    /// </exception>
    public static void SetWrapper(Func<Func<Element>, Element> wrapper)
    {
        ArgumentNullException.ThrowIfNull(wrapper);

        // A module initializer runs on whichever thread first touches the app's assembly, not always the UI thread. An app's
        // executable referenced by a test executable runs the same initializer twice with the same method (Issue #907).
        while (true)
        {
            State state = Volatile.Read(ref _state);
            if (state.Wrapper is not null && state.Wrapper.Equals(wrapper))
            {
                return;
            }

            if (state.Read)
            {
                ThrowHelper.ThrowRootWrapperSetAfterAWindow();
            }

            if (state.Wrapper is not null)
            {
                ThrowHelper.ThrowRootWrapperAlreadySet();
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, new State(wrapper, false), state), state))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Returns the wrapper set with <see cref="SetWrapper"/>, and marks it read: a desktop window calls it once, when it mounts its
    /// root. After it, <see cref="SetWrapper"/> refuses another wrapper.
    /// </summary>
    /// <returns>The wrapper; <see langword="null"/> when none is set, and the window is not wrapped.</returns>
    internal static Func<Func<Element>, Element>? ReadWrapper()
    {
        while (true)
        {
            State state = Volatile.Read(ref _state);
            if (state.Read || ReferenceEquals(Interlocked.CompareExchange(ref _state, new State(state.Wrapper, true), state), state))
            {
                return state.Wrapper;
            }
        }
    }

    private sealed record State(Func<Func<Element>, Element>? Wrapper, bool Read);
}
