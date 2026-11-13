using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XylemUI.Analyzers;

/// <summary>
/// Reports XYLEM130: <c>For</c> or <c>VirtualFor</c> called on a box that has children, or a second one in the same chain (design
/// §2.2, "Analyzers"). From the receiver it follows the chain of extension methods that take an <c>Element</c> and return one; a
/// box has children when the chain starts at a call of a static method of <c>Elements</c> given at least one <c>Element</c>
/// argument (<c>Column(header)</c>, <c>Box(child)</c>). A box kept in a variable, one another method returned, and a second
/// <c>For</c> in another statement are not seen.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForOnFilledBoxAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.ForOnFilledBox);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (
                start.Compilation.GetTypeByMetadataName(Names.Element) is not { } element
                || start.Compilation.GetTypeByMetadataName(Names.Elements) is not { } elements
                || start.Compilation.GetTypeByMetadataName(Names.ElementExtensions) is not { } extensions
            )
            {
                return;
            }

            start.RegisterSyntaxNodeAction(node => Analyze(node, element, elements, extensions), SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol element, INamedTypeSymbol elements, INamedTypeSymbol extensions)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (
            invocation.Expression is not MemberAccessExpressionSyntax access
            || access.Name.Identifier.ValueText is not ("For" or "VirtualFor")
            || !IsFor(Method(context, invocation), extensions)
        )
        {
            return;
        }

        string? box = null;
        ExpressionSyntax receiver = Unparenthesize(access.Expression);
        while (box is null && receiver is InvocationExpressionSyntax call && Method(context, call) is { } method)
        {
            if (method.MethodKind == MethodKind.ReducedExtension && call.Expression is MemberAccessExpressionSyntax inner)
            {
                IMethodSymbol definition = method.ReducedFrom ?? method;
                if (IsFor(method, extensions))
                {
                    box = "a box whose chain already calls '" + method.Name + "'";
                }
                else if (definition.Parameters.Length > 0 && Names.Is(definition.Parameters[0].Type, element) && Names.Is(method.ReturnType, element))
                {
                    receiver = Unparenthesize(inner.Expression);
                    continue;
                }

                break;
            }

            if (method.IsStatic && Names.Is(method.ContainingType, elements) && HasElementArgument(context, call, element))
            {
                box = "a box made with children by '" + method.Name + "'";
            }

            break;
        }

        if (box is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rules.ForOnFilledBox, access.Name.GetLocation(), access.Name.Identifier.ValueText, box));
        }
    }

    private static IMethodSymbol? Method(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation) =>
        context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;

    private static bool IsFor(IMethodSymbol? method, INamedTypeSymbol extensions) =>
        method is not null && method.Name is ("For" or "VirtualFor") && Names.Is(method.ContainingType, extensions);

    private static bool HasElementArgument(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax call, INamedTypeSymbol element)
    {
        foreach (ArgumentSyntax argument in call.ArgumentList.Arguments)
        {
            if (Names.Is(context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type, element))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
