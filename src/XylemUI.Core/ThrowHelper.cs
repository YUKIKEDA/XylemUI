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
    /// Throws <see cref="ObjectDisposedException"/> when a disposed computed is used.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowComputedDisposed() => throw new ObjectDisposedException(null, "The computed value was disposed.");
}
