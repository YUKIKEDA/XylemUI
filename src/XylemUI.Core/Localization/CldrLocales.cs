namespace XylemUI;

/// <summary>
/// The CLDR tables of every locale the package holds, one property each (design §4.11, "Declaration and reference"): the modern
/// locales of CLDR without those whose tag has a variant, and the languages of the widgets' translations that they lack. The tables
/// of a property no code reads do not stay in a NativeAOT binary.
/// </summary>
/// <remarks>
/// A property's name joins the subtags of its tag (<c>En</c>, <c>EnGB</c>, <c>ZhHantHK</c>, <c>Es419</c>). For a locale CLDR does not
/// have, take another locale's tables under its tag with <see cref="LocaleData.WithLocale"/>.
/// </remarks>
public static partial class CldrLocales
{
    /// <summary>
    /// Gets the tables of every property, in ordinal order of their tags, for an app whose user may choose any language. Reading it
    /// keeps every table in the binary.
    /// </summary>
    public static IReadOnlyList<LocaleData> All => CldrAll.Locales;
}
