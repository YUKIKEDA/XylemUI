using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers;

/// <summary>
/// Holds the descriptors of the analyzers' rules (design §2.2, "Analyzers"). Each rule's page under <c>docs/analyzers</c> says what
/// it looks at, what it does not, and how to suppress it.
/// </summary>
internal static class Rules
{
    /// <summary>
    /// The category of every rule, as the generators' diagnostics have.
    /// </summary>
    internal const string Category = "XylemUI";

    /// <summary>
    /// The rule that reports a call of a UI-thread-only API in a lambda that runs on another thread.
    /// </summary>
    internal static readonly DiagnosticDescriptor OffUiThread = new(
        "XYLEM100",
        "A UI-thread-only API is called in a lambda that runs on another thread",
        "'{0}' carries [UiThreadOnly] and is called in a lambda given to '{1}', which runs it off the UI thread, where it throws InvalidOperationException",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The body of a lambda written directly as the argument of Task.Run, ThreadPool.QueueUserWorkItem, ThreadPool.UnsafeQueueUserWorkItem, or new Thread runs off the UI thread. A member that carries [UiThreadOnly] throws InvalidOperationException there. Hand the work to the UI thread through the dispatcher instead.",
        helpLinkUri: HelpLink("XYLEM100")
    );

    /// <summary>
    /// The rule that reports a signal read once for an argument whose API has a reactive overload.
    /// </summary>
    internal static readonly DiagnosticDescriptor ReadOnce = new(
        "XYLEM110",
        "A signal is read once for an argument that has a reactive overload",
        "The argument of '{0}' reads a signal with Get() once, when the element is made, and does not follow its changes; pass a function or a signal to the reactive overload instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An API with an overload that takes Func<T> or IReadOnlySignal<T> at the same position writes again when the value changes. The overload that takes T writes the value it is given once. A Get() written directly in that argument reads the signal once, so a later change is not shown.",
        helpLinkUri: HelpLink("XYLEM110")
    );

    /// <summary>
    /// The rule that reports an async lambda given to <c>Effect.Create</c> or <c>Computed.Create</c>.
    /// </summary>
    internal static readonly DiagnosticDescriptor AsyncTracked = new(
        "XYLEM111",
        "An async lambda is given to Effect.Create or Computed.Create",
        "'{0}' is given an async lambda; only what it reads before its first await is tracked, and a change of what it reads after it does not run it again",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The tracking records what a function reads while it runs synchronously. The reads of an async lambda after its first await are not dependencies. Effect.Create gets the lambda as an async void function, and Computed.Create gets a value that is a Task.",
        helpLinkUri: HelpLink("XYLEM111")
    );

    /// <summary>
    /// The rule that reports an effect, a cleanup, or an element made after an <c>await</c>.
    /// </summary>
    internal static readonly DiagnosticDescriptor AfterAwait = new(
        "XYLEM120",
        "An effect, a cleanup, or an element is made after an await",
        "'{0}' is called after an await, where the scope of the call that started the method is gone; run it in a lambda given to Scope.Run",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The current scope lasts only while a call runs, and the continuation after an await has none. There Scope.OnCleanup and the functions that make elements throw InvalidOperationException, and Effect.Create makes an effect that no scope owns, which stays after the view is gone.",
        helpLinkUri: HelpLink("XYLEM120")
    );

    /// <summary>
    /// The rule that reports <c>For</c> or <c>VirtualFor</c> on a box that has children or already has one.
    /// </summary>
    internal static readonly DiagnosticDescriptor ForOnFilledBox = new(
        "XYLEM130",
        "For or VirtualFor is called on a box that has children or a For",
        "'{0}' is called on {1}, and throws ArgumentException when it runs",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The rows of For and VirtualFor are the only children of their box. A box made with children, or one whose chain already called For or VirtualFor, throws ArgumentException.",
        helpLinkUri: HelpLink("XYLEM130")
    );

    private static string HelpLink(string id) => "https://github.com/YUKIKEDA/XylemUI/blob/main/docs/analyzers/" + id + ".md";
}
