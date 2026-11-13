using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace XylemUI.Analyzers;

/// <summary>
/// Reports XYLEM111: an async lambda or async anonymous method written as the argument of <c>Effect.Create</c> or
/// <c>Computed.Create</c> (design §2.2, "Analyzers"). An async method given as a method group, and a synchronous lambda that calls
/// an async method, are not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncTrackedAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.AsyncTracked);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            INamedTypeSymbol? effect = start.Compilation.GetTypeByMetadataName(Names.Effect);
            INamedTypeSymbol? computed = start.Compilation.GetTypeByMetadataName(Names.Computed);
            if (effect is null || computed is null)
            {
                return;
            }

            start.RegisterOperationAction(
                operation =>
                {
                    var invocation = (IInvocationOperation)operation.Operation;
                    IMethodSymbol method = invocation.TargetMethod;
                    if (method.Name != "Create" || !(Names.Is(method.ContainingType, effect) || Names.Is(method.ContainingType, computed)))
                    {
                        return;
                    }

                    foreach (IArgumentOperation argument in invocation.Arguments)
                    {
                        if (argument.Syntax is ArgumentSyntax { Expression: AnonymousFunctionExpressionSyntax function } && function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
                        {
                            operation.ReportDiagnostic(
                                Diagnostic.Create(Rules.AsyncTracked, function.AsyncKeyword.GetLocation(), method.ContainingType.Name + "." + method.Name)
                            );
                        }
                    }
                },
                OperationKind.Invocation
            );
        });
    }
}
