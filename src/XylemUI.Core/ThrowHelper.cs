using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace XylemUI;

/// <summary>
/// Throws the engine's exceptions from methods that are never inlined, so that the callers stay small (.cursor/rules/perf.mdc).
/// </summary>
internal static class ThrowHelper
{
    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowNotOnUiThread() =>
        throw new InvalidOperationException(
            UiThread.IsInitialized
                ? "This API can only be called on the UI thread."
                : "This API can only be called on the UI thread, and no UI thread has been initialized."
        );

    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowUiThreadAlreadyInitialized() => throw new InvalidOperationException("Another thread is already the UI thread.");

    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowComputedReadsItself() =>
        throw new InvalidOperationException("A computed value was read while it computes its own value.");

    [DoesNotReturn]
    [StackTraceHidden]
    internal static void ThrowComputedDisposed() => throw new ObjectDisposedException(null, "The computed value was disposed.");
}
