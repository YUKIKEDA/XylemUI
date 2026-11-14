namespace XylemUI;

/// <summary>
/// Selects the plural category of a value with one set of CLDR rules: a method that tools/CldrTableGen generated in
/// <c>CldrPluralRules</c> (design §4.11, "Plural rules").
/// </summary>
/// <param name="operands">The operands of the value.</param>
/// <returns>The category.</returns>
internal delegate PluralCategory PluralRule(in PluralOperands operands);
