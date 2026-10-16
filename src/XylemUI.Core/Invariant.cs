using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace XylemUI;

/// <summary>
/// Stops on a broken internal invariant (design §0.1). Callers never catch it.
/// </summary>
internal static class Invariant
{
    /// <summary>
    /// Reports a broken invariant. Debug builds throw <see cref="InvariantViolationException"/>, so a debugger breaks there and a
    /// test can observe it. Release builds end the process with <see cref="Environment.FailFast(string)"/> and do not try to recover.
    /// </summary>
    [DoesNotReturn]
    [StackTraceHidden]
    public static void Fail(string message)
    {
#if DEBUG
        throw new InvariantViolationException(message);
#else
        Environment.FailFast("XylemUI invariant violated: " + message);
#endif
    }
}

/// <summary>
/// Thrown in debug builds when an internal invariant is broken. It signals a bug in XylemUI, not a caller error.
/// </summary>
internal sealed class InvariantViolationException(string message) : Exception(message);
