using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace XylemUI.Analyzers;

/// <summary>
/// Reads <c>[UiThreadOnly]</c> from the member an operation calls (design §2.2, "The attribute for UI-thread-only APIs").
/// </summary>
internal static class UiThreadOnly
{
    /// <summary>
    /// Returns the member that <paramref name="operation"/> calls when that member carries the attribute: the method itself (the
    /// original definition of a generic one), the constructor, or a property whose accessor the operation calls, when the
    /// property or that accessor carries it. A member that overrides or implements one with the attribute does not get it.
    /// </summary>
    /// <param name="operation">An operation of a function's body.</param>
    /// <param name="attribute">The attribute's type in the compilation.</param>
    /// <returns>The member, or <see langword="null"/> when the operation calls none that carries the attribute.</returns>
    internal static ISymbol? Called(IOperation operation, INamedTypeSymbol attribute)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                IMethodSymbol method = (invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod).OriginalDefinition;
                return Carries(method, attribute) ? method : null;
            case IObjectCreationOperation { Constructor: { } constructor }:
                return Carries(constructor.OriginalDefinition, attribute) ? constructor.OriginalDefinition : null;
            case IPropertyReferenceOperation reference:
                IPropertySymbol property = reference.Property.OriginalDefinition;
                if (Carries(property, attribute))
                {
                    return property;
                }

                (bool gets, bool sets) = Accessors(reference);
                return (gets && property.GetMethod is { } getter && Carries(getter, attribute))
                    || (sets && property.SetMethod is { } setter && Carries(setter, attribute))
                    ? property
                    : null;
            default:
                return null;
        }
    }

    private static bool Carries(ISymbol symbol, INamedTypeSymbol attribute)
    {
        foreach (AttributeData data in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute))
            {
                return true;
            }
        }

        return false;
    }

    // Which accessors a reference calls: an assignment's target sets, a compound assignment or an increment gets and sets, and
    // anything else gets.
    private static (bool Gets, bool Sets) Accessors(IPropertyReferenceOperation reference) =>
        reference.Parent switch
        {
            ISimpleAssignmentOperation assignment when assignment.Target == reference => (false, true),
            ICompoundAssignmentOperation compound when compound.Target == reference => (true, true),
            ICoalesceAssignmentOperation coalesce when coalesce.Target == reference => (true, true),
            IIncrementOrDecrementOperation => (true, true),
            _ => (true, false),
        };
}
