# XYLEM130: For or VirtualFor is called on a box that has children or a For

Severity: Warning. Design: `docs/design.ja.md` §2.2, "Analyzers".

## What it reports

`For` or `VirtualFor` called on a box made with children, or a second one in the same chain. Both throw `ArgumentException` when
they run: the rows are the only children of their box.

```csharp
Column(header).For(items, i => i, Row);                  // XYLEM130: the box has a child
Column().For(a, i => i, Row).For(b, i => i, Row);        // XYLEM130: a second For
```

From the receiver of `For` the rule follows the chain of extension methods that take an `Element` and return one
(`.Width(50)`, `.Padding(4)`). The box has children when the chain starts at a call of a static method of `Elements` given at least
one `Element` argument (`Column(header)`, `Box(child)`). It is a second `For` when the chain passes through `For` or `VirtualFor`.

Put the fixed children and the rows in boxes of their own: `Column(header, Column().For(items, i => i, Row))`.

## What it does not see

- A box kept in a variable (`var list = Column(header);` and then `list.For(...)`).
- A box another method returned.
- A second `For` called in another statement.

## How to suppress it

- `#pragma warning disable XYLEM130` and `#pragma warning restore XYLEM130` around the call, with the reason in a comment.
- `[SuppressMessage("XylemUI", "XYLEM130", Justification = "...")]` on the member.
- In `.editorconfig`: `dotnet_diagnostic.XYLEM130.severity = none`.
