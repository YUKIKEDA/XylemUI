# XYLEM100: A UI-thread-only API is called in a lambda that runs on another thread

Severity: Warning. Design: `docs/design.ja.md` §2.2, "Analyzers"; §2.12.

## What it reports

A call of a member that carries `[UiThreadOnly]` (namespace `XylemUI`) in the body of a lambda or an anonymous method written
directly as the argument of one of these:

- `Task.Run`
- `ThreadPool.QueueUserWorkItem`, `ThreadPool.UnsafeQueueUserWorkItem`
- `new Thread`

The body runs off the UI thread, and the member throws `InvalidOperationException` there.

```csharp
Task.Run(() => count.Set(1)); // XYLEM100: Signal<T>.Set carries [UiThreadOnly]
```

A call counts when the member called carries the attribute: a method (the original definition of a generic one), a constructor, or
a property whose accessor the call uses, when the property or that accessor carries it. A member that overrides or implements one
with the attribute does not get it; a call through an interface is reported when the interface's member carries it.

Hand the result to the UI thread instead: post a message to the dispatcher, or `await` the work on the UI thread and set the
signal after it.

## What it does not see

- A method group (`Task.Run(Work)`), and a delegate kept in a variable and then passed.
- A call inside another method that the body calls.
- A lambda inside the body: it may be handed back to the UI thread.
- Other ways to start work elsewhere: `Task.Factory.StartNew`, `Parallel`, `ContinueWith`, timers.
- A call after an `await` that resumes off the UI thread (after `ConfigureAwait(false)`, or in a method that a thread pool thread
  runs): the analyzer does not know which thread a continuation runs on.
- A public API that reaches a UI-thread-only API only through another public API and does not carry the attribute itself.

## How to suppress it

- `#pragma warning disable XYLEM100` and `#pragma warning restore XYLEM100` around the call, with the reason in a comment.
- `[SuppressMessage("XylemUI", "XYLEM100", Justification = "...")]` on the member.
- In `.editorconfig`: `dotnet_diagnostic.XYLEM100.severity = none`.
