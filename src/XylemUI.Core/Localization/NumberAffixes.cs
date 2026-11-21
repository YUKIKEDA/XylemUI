namespace XylemUI;

/// <summary>
/// The text a number pattern writes before and after the digits, with its symbols in place (design §4.11, "Numbers").
/// </summary>
/// <param name="Prefix">The text before the digits.</param>
/// <param name="Suffix">The text after the digits.</param>
/// <param name="SpaceAfterPrefix">Whether a no-break space goes between the prefix and the digits: the prefix ends with a currency
/// symbol whose last character is neither a symbol nor a space (CLDR's <c>currencySpacing</c>).</param>
/// <param name="SpaceBeforeSuffix">Whether a no-break space goes between the digits and the suffix, for a suffix that starts with
/// such a currency symbol.</param>
internal readonly record struct NumberAffixes(string Prefix, string Suffix, bool SpaceAfterPrefix, bool SpaceBeforeSuffix);
