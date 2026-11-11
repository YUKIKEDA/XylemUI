; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
XYLEM001 | XylemUI | Error | A type with [Action] does not implement IAction
XYLEM002 | XylemUI | Error | An action has no constructor without parameters
XYLEM003 | XylemUI | Error | Two actions have one name
XYLEM004 | XylemUI | Error | An action's name is empty
XYLEM005 | XylemUI | Error | An action's type cannot be named from the table
