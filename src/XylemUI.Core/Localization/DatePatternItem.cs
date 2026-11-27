namespace XylemUI;

/// <summary>
/// One piece of a compiled date pattern (UTS #35 part 4, "Date Format Patterns"): a field, a run of one letter such as <c>MMM</c>, or
/// the literal text between fields with its quotes removed.
/// </summary>
/// <param name="Symbol">The field's letter; <c>'\0'</c> for literal text.</param>
/// <param name="Length">The field's length; 0 for literal text.</param>
/// <param name="Literal">The literal text; empty for a field.</param>
internal readonly record struct DatePatternItem(char Symbol, int Length, string Literal);
