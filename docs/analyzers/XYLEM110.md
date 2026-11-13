# XYLEM110: A signal is read once for an argument that has a reactive overload

Severity: Warning. Design: `docs/design.ja.md` §2.2, "Analyzers".

## What it reports

A `Get()` of an `IReadOnlySignal<T>`, a `Signal<T>`, or a `Computed<T>` written directly in an argument of an API that has a
reactive overload, on the overload that does not react. The value is read once, when the element is made, and a later change of
the signal does not reach the element.

```csharp
Text(count.Get().ToString());       // XYLEM110: written once
Text(() => count.Get().ToString()); // written again whenever count changes
```

An API has a reactive overload when its type has a method of the same name whose parameter at the same position is `Func<T>` or
`IReadOnlySignal<T>`, where the one called takes `T`. In `XylemUI.Core` these are the six pairs:

| Not reactive | Reactive |
| --- | --- |
| `Text(string)` | `Text(Func<string>)` |
| `Image(ImageSource, …)` | `Image(Func<ImageSource>, …)` |
| `Background(Paint)` | `Background(IReadOnlySignal<Paint>)` |
| `Foreground(Color)` | `Foreground(IReadOnlySignal<Color>)` |
| `Disabled(bool)` | `Disabled(IReadOnlySignal<bool>)` |
| `Accessible(AccessibilityInfo)` | `Accessible(IReadOnlySignal<AccessibilityInfo>)` |

The rule finds the pairs from the overloads, so a pair added later, or an app's own, is covered too.

Code fix: for `Text(string)`, "Pass the text as a function" writes `Text(() => argument)`.

Reading a signal outside a computed value or an effect is not an error in itself: a click handler or a model's method reads the
current value on purpose. The rule reports only the argument that a reactive overload would follow.

## What it does not see

- A lambda inside the argument: that is the reactive form.
- A pair of generic overloads (`M<T>(T value)` and `M<T>(Func<T> value)`): the two type parameters are different symbols, so the pair is not found.
- A value kept in a variable first (`string s = count.Get().ToString();` and then `Text(s)`).
- A value another method read and returned.

## How to suppress it

- `#pragma warning disable XYLEM110` and `#pragma warning restore XYLEM110` around the call, with the reason in a comment (for
  example, a row of `For` keyed by its own value, whose item never changes).
- `[SuppressMessage("XylemUI", "XYLEM110", Justification = "...")]` on the member.
- In `.editorconfig`: `dotnet_diagnostic.XYLEM110.severity = none`.
