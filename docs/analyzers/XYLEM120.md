# XYLEM120: An effect, a cleanup, or an element is made after an await

Severity: Warning. Design: `docs/design.ja.md` §2.2, "Analyzers"; §2.3.

## What it reports

In an async method, an async lambda, or an async local function, a call written after the first `await` of one of these:

- `Effect.Create`
- `Scope.OnCleanup`
- a function that makes an element: a static method that returns `Element` and is not an extension method (the functions of
  `Elements`, the primitives' `Button`, an app's own)

```csharp
async Task OpenAsync()
{
    await LoadAsync();
    Effect.Create(() => title.Set(name.Get())); // XYLEM120: no scope owns it
}
```

The current scope lasts only while a call runs, and the continuation after an `await` has none. There `Scope.OnCleanup` and the
functions that make elements throw `InvalidOperationException`, and `Effect.Create` makes an effect that no scope owns, which stays
after the view is gone.

Run the code in a lambda given to `Scope.Run` with the scope it belongs to, or set a signal and let the view react to it.

"After the first `await`" is the order in which the code is written: the operand of an `await` comes before it, and the body of an
`await foreach` comes after it.

## What it does not see

- A call inside a lambda or a local function, a lambda given to `Scope.Run` included: it runs when it is called, in its caller's
  scope. An async lambda or local function is checked as a function of its own.
- A call in a loop written before the `await` (from the second time round, it runs after the `await`).
- A call inside another method.
- An extension method that needs a scope (`For`, `Popup`, `Menu`, `Background` with an `IReadOnlySignal<T>`, and others).

## How to suppress it

- `#pragma warning disable XYLEM120` and `#pragma warning restore XYLEM120` around the call, with the reason in a comment.
- `[SuppressMessage("XylemUI", "XYLEM120", Justification = "...")]` on the member.
- In `.editorconfig`: `dotnet_diagnostic.XYLEM120.severity = none`.
