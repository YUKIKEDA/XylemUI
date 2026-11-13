using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace XylemUI.Analyzers;

/// <summary>
/// Reports XYLEM120: in an async method, an async lambda, or an async local function, a call written after the first <c>await</c>
/// of <c>Effect.Create</c>, <c>Scope.OnCleanup</c>, or a function that makes an element (a static method that returns
/// <c>Element</c> and is not an extension method) (design §2.2, "Analyzers"). "After" is the order in which the code is written.
/// Calls inside a lambda or a local function are not this function's: they run when they are called, inside the scope of their
/// caller, as a lambda given to <c>Scope.Run</c> does.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AfterAwaitAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.AfterAwait);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (
                start.Compilation.GetTypeByMetadataName(Names.Element) is not { } element
                || start.Compilation.GetTypeByMetadataName(Names.Effect) is not { } effect
                || start.Compilation.GetTypeByMetadataName(Names.Scope) is not { } scope
            )
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                node => Analyze(node, element, effect, scope),
                SyntaxKind.MethodDeclaration,
                SyntaxKind.LocalFunctionStatement,
                SyntaxKind.ParenthesizedLambdaExpression,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.AnonymousMethodExpression
            );
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol element, INamedTypeSymbol effect, INamedTypeSymbol scope)
    {
        SyntaxNode? body = context.Node switch
        {
            MethodDeclarationSyntax method when method.Modifiers.Any(SyntaxKind.AsyncKeyword) => (SyntaxNode?)method.Body ?? method.ExpressionBody,
            LocalFunctionStatementSyntax local when local.Modifiers.Any(SyntaxKind.AsyncKeyword) => (SyntaxNode?)local.Body ?? local.ExpressionBody,
            AnonymousFunctionExpressionSyntax function when function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) => function.Body,
            _ => null,
        };
        if (body is null)
        {
            return;
        }

        // The nodes of this function alone: a nested lambda or local function is analyzed as a function of its own.
        ImmutableArray<SyntaxNode> nodes = body.DescendantNodesAndSelf(descendant =>
                descendant == body || descendant is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            )
            .ToImmutableArray();
        int firstAwait = int.MaxValue;
        foreach (SyntaxNode node in nodes)
        {
            int after = node switch
            {
                // The operand runs before the await; what is written after the expression runs in the continuation.
                AwaitExpressionSyntax awaited => awaited.Span.End,

                // The body of an await foreach runs after the first MoveNextAsync is awaited.
                CommonForEachStatementSyntax loop when loop.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) => loop.Expression.Span.End,
                _ => int.MaxValue,
            };
            if (after < firstAwait)
            {
                firstAwait = after;
            }
        }

        if (firstAwait == int.MaxValue)
        {
            return;
        }

        foreach (SyntaxNode node in nodes)
        {
            if (node is not InvocationExpressionSyntax invocation || invocation.SpanStart < firstAwait)
            {
                continue;
            }

            if (
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called
                && NeedsScope(called, element, effect, scope)
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rules.AfterAwait, invocation.GetLocation(), called.ContainingType.Name + "." + called.Name)
                );
            }
        }
    }

    // Effect.Create, Scope.OnCleanup, and the functions that make elements. The extension methods that need a scope (For, Popup,
    // Background with a signal) are not counted (design §2.2, "Analyzers").
    private static bool NeedsScope(IMethodSymbol method, INamedTypeSymbol element, INamedTypeSymbol effect, INamedTypeSymbol scope) =>
        (method.Name == "Create" && Names.Is(method.ContainingType, effect))
        || (method.Name == "OnCleanup" && Names.Is(method.ContainingType, scope))
        || (
            method.IsStatic
            && !method.IsExtensionMethod
            && method.MethodKind == MethodKind.Ordinary
            && Names.Is(method.OriginalDefinition.ReturnType, element)
        );
}
