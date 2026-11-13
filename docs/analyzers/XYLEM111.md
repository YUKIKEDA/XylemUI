# XYLEM111: An async lambda is given to Effect.Create or Computed.Create

Severity: Warning. Design: `docs/design.ja.md` §2.2, "Analyzers".

## What it reports

An async lambda or async anonymous method written as the argument of `Effect.Create` or `Computed.Create`.

```csharp
Effect.Create(async () =>
{
    await LoadAsync();
    label.Set(name.Get()); // not tracked: read after the first await
});
```

The tracking records what a function reads while it runs synchronously. What an async lambda reads after its first `await` is not a
dependency, and a change of it does not run the function again. `Effect.Create` gets the lambda as an async void function, and
`Computed.Create` gets a value that is a `Task`.

Read what the function depends on before any `await`, or start the async work from a synchronous effect and set a signal with
its result.

`Text(Func<string>)` is not in the rule: the compiler refuses an async lambda there (CS4010).

## What it does not see

- An async method given as a method group (`Effect.Create(RunAsync)`).
- A synchronous lambda that calls an async method (`() => LoadAsync()`).

## How to suppress it

- `#pragma warning disable XYLEM111` and `#pragma warning restore XYLEM111` around the call, with the reason in a comment.
- `[SuppressMessage("XylemUI", "XYLEM111", Justification = "...")]` on the member.
- In `.editorconfig`: `dotnet_diagnostic.XYLEM111.severity = none`.
