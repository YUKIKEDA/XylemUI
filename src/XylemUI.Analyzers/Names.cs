using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers;

/// <summary>
/// Holds the metadata names of the XylemUI types the rules look for. The analyzers do not reference <c>XylemUI.Core</c>; they find
/// its types in the compilation by these names, and a rule whose types are missing does nothing.
/// </summary>
internal static class Names
{
    /// <summary>
    /// The attribute that marks a UI-thread-only API.
    /// </summary>
    internal const string UiThreadOnlyAttribute = "XylemUI.UiThreadOnlyAttribute";

    /// <summary>
    /// The handle of a node.
    /// </summary>
    internal const string Element = "XylemUI.Element";

    /// <summary>
    /// The functions that make elements.
    /// </summary>
    internal const string Elements = "XylemUI.Elements";

    /// <summary>
    /// The extension methods of elements.
    /// </summary>
    internal const string ElementExtensions = "XylemUI.ElementExtensions";

    /// <summary>
    /// The value that can be read and tracked.
    /// </summary>
    internal const string ReadOnlySignal = "XylemUI.IReadOnlySignal`1";

    /// <summary>
    /// The signal.
    /// </summary>
    internal const string Signal = "XylemUI.Signal`1";

    /// <summary>
    /// The computed value.
    /// </summary>
    internal const string ComputedValue = "XylemUI.Computed`1";

    /// <summary>
    /// The class that creates computed values.
    /// </summary>
    internal const string Computed = "XylemUI.Computed";

    /// <summary>
    /// The effect, which also creates effects.
    /// </summary>
    internal const string Effect = "XylemUI.Effect";

    /// <summary>
    /// The scopes (the ownership tree).
    /// </summary>
    internal const string Scope = "XylemUI.Scope";

    /// <summary>
    /// Returns whether a type is the type of a metadata name.
    /// </summary>
    /// <param name="type">The type, or <see langword="null"/>.</param>
    /// <param name="known">The type found by its name, or <see langword="null"/> when the compilation has none.</param>
    /// <returns><see langword="true"/> when both are the same type.</returns>
    internal static bool Is(ITypeSymbol? type, INamedTypeSymbol? known) =>
        type is not null && known is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, known);
}
