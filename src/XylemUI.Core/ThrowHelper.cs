using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace XylemUI;

/// <summary>
/// Throws the engine's exceptions from methods that are never inlined, so that the callers stay small (.cursor/rules/perf.mdc).
/// </summary>
internal static class ThrowHelper
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> for a call from a thread that is not the UI thread.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNotOnUiThread() =>
        throw new InvalidOperationException(
            UiThread.IsInitialized
                ? "This API can only be called on the UI thread."
                : "This API can only be called on the UI thread, and no UI thread has been initialized."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a thread tries to become the UI thread and another thread already is.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowUiThreadAlreadyInitialized() => throw new InvalidOperationException("Another thread is already the UI thread.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a computed is read while it computes its own value.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowComputedReadsItself() =>
        throw new InvalidOperationException("A computed value was read while it computes its own value.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a signal is set while a computed computes.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowSetInsideComputed() =>
        throw new InvalidOperationException("A signal was set while a computed value computes; a computed value has no side effects.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a cleanup is registered with no current scope.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNoCurrentScope() =>
        throw new InvalidOperationException("A cleanup was registered with no current scope; register it inside Scope.Create or an effect.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a scope, an effect, or a cleanup is made while a computed computes.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowScopeInsideComputed() =>
        throw new InvalidOperationException(
            "A scope, an effect, or a cleanup was made while a computed value computes; a computed value has no side effects."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a context is looked up while a computed computes.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowContextInsideComputed() =>
        throw new InvalidOperationException(
            "A context was looked up while a computed value computes; look it up where the computed value is made and read the result inside."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a context is looked up with no current scope.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowContextWithNoScope() =>
        throw new InvalidOperationException(
            "A context was looked up with no current scope; keep Scope.Current before an await and look it up inside Scope.Run."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a context has neither a provided value nor a default.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowContextHasNoValue() =>
        throw new InvalidOperationException("No value is provided for the context, and the context has no default value.");

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when a <see cref="UiMessage"/> is posted as a type that is not its own.
    /// </summary>
    /// <param name="parameterName">The name of the message's parameter.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowMessagePostedAsBaseType(string parameterName) =>
        throw new ArgumentException(
            "A message is posted as its own type, which chooses its receivers; this one was posted as a base type.",
            parameterName
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <see cref="UiDispatcher.Current"/> is read before a host set it.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNoCurrentDispatcher() =>
        throw new InvalidOperationException(
            "No UI dispatcher is set: a host sets UiDispatcher.Current when it starts (XylemApp.Run, or HeadlessSession's UI thread)."
        );

    /// <summary>
    /// Throws <see cref="ObjectDisposedException"/> when a disposed computed is used.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowComputedDisposed() => throw new ObjectDisposedException(null, "The computed value was disposed.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the process's root wrapper is set a second time.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowRootWrapperAlreadySet() =>
        throw new InvalidOperationException(
            "The root wrapper of the windows is set once in a process; an app chooses one theme with one [assembly: WindowTheme]."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the process's root wrapper is set after a window read it.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowRootWrapperSetAfterAWindow() =>
        throw new InvalidOperationException(
            "The root wrapper of the windows is set before the first window opens, as the module initializer of [assembly: WindowTheme] does."
        );

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a wrapper calls the window's root function a second time.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowRootCalledTwice() =>
        throw new InvalidOperationException("The root wrapper calls the window's root function once; this is the second call.");

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when a wrapper returned without calling the window's root function.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowRootNotCalled() =>
        throw new InvalidOperationException("The root wrapper calls the window's root function once; it returned without calling it.");

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for an appearance of the OS whose high contrast and high-contrast colors disagree.
    /// </summary>
    /// <param name="parameterName">The name of the appearance's parameter.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowContrastColorsDisagree(string parameterName) =>
        throw new ArgumentException("The appearance has high-contrast colors exactly while its high contrast is on.", parameterName);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for a currency code that is not in the form of ISO 4217.
    /// </summary>
    /// <param name="isoCode">The code.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNotACurrencyCode(string isoCode) =>
        throw new ArgumentException("The currency code '" + isoCode + "' is not three upper-case ASCII letters (ISO 4217).", nameof(isoCode));

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> for <see cref="NumberOptions"/> whose digits a format cannot show.
    /// </summary>
    /// <param name="parameterName">The name of the options' parameter.</param>
    /// <param name="message">The condition that failed.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNumberOptionOutOfRange(string parameterName, string message) =>
        throw new ArgumentOutOfRangeException(parameterName, message);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when text is parsed with a format that has no parsing.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNumberParseNotSupported() =>
        throw new InvalidOperationException("Only a decimal or a percent format parses text; a currency or a compact format does not.");

    /// <summary>
    /// Throws <see cref="OverflowException"/> when a rounded number is outside the range of <see cref="decimal"/>.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowRoundedNumberOverflow() => throw new OverflowException("The number the format shows is outside the range of decimal.");

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for a skeleton that is not one of the date formats a <see cref="DateFormatter"/> takes.
    /// </summary>
    /// <param name="skeleton">The skeleton.</param>
    /// <param name="kind">What the skeleton had to be: "one of Flutter's 41 date skeletons", "one of the 28 date skeletons of
    /// Flutter's 41", or "one of the 13 time skeletons of Flutter's 41".</param>
    /// <param name="parameterName">The name of the skeleton's parameter.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNotASkeleton(string skeleton, string kind, string parameterName) =>
        throw new ArgumentException("The skeleton '" + skeleton + "' is not " + kind + ".", parameterName);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> for a value whose type a date format does not take: a date for a format with time fields,
    /// a time for a format with date fields, or a value with no offset for a format with a time zone.
    /// </summary>
    /// <param name="message">The condition that failed.</param>
    /// <param name="parameterName">The name of the value's parameter.</param>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowDateValueNotTaken(string message, string parameterName) => throw new ArgumentException(message, parameterName);
}
