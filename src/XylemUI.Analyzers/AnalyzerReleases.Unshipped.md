; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XYLEM100 | XylemUI | Warning | A UI-thread-only API is called in a lambda that runs on another thread
XYLEM110 | XylemUI | Warning | A signal is read once for an argument that has a reactive overload
XYLEM111 | XylemUI | Warning | An async lambda is given to Effect.Create or Computed.Create
XYLEM120 | XylemUI | Warning | An effect, a cleanup, or an element is made after an await
XYLEM130 | XylemUI | Warning | For or VirtualFor is called on a box that has children or a For
