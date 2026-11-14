namespace XylemUI.Theme.Fluent;

/// <summary>
/// A color in CIE Lab, relative to the D50 white (design §13.2, "ブランドの 16 段の作り方", step 1).
/// </summary>
/// <param name="L">The lightness, L*, from 0 (black) to 100 (white).</param>
/// <param name="A">The a* axis, green to red.</param>
/// <param name="B">The b* axis, blue to yellow.</param>
internal readonly record struct Lab(double L, double A, double B);
