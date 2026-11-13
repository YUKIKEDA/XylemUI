using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace XylemUI.Analyzers;

/// <summary>
/// Reports XYLEM100: a member that carries <c>[UiThreadOnly]</c>, called in the body of a lambda or an anonymous method written
/// directly as the argument of <c>Task.Run</c>, <c>ThreadPool.QueueUserWorkItem</c>, <c>ThreadPool.UnsafeQueueUserWorkItem</c>, or
/// <c>new Thread</c> (design §2.2, "Analyzers"). Lambdas inside the body are not looked into: they may be handed back to the UI
/// thread.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OffUiThreadAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.OffUiThread);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(Names.UiThreadOnlyAttribute) is not { } attribute)
            {
                return;
            }

            INamedTypeSymbol? task = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            INamedTypeSymbol? threadPool = start.Compilation.GetTypeByMetadataName("System.Threading.ThreadPool");
            INamedTypeSymbol? thread = start.Compilation.GetTypeByMetadataName("System.Threading.Thread");
            start.RegisterOperationAction(
                operation =>
                {
                    var invocation = (IInvocationOperation)operation.Operation;
                    IMethodSymbol method = invocation.TargetMethod;
                    bool starts =
                        (Names.Is(method.ContainingType, task) && method.Name == "Run")
                        || (
                            Names.Is(method.ContainingType, threadPool)
                            && method.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem"
                        );
                    if (starts)
                    {
                        Check(operation, invocation.Arguments, method.ContainingType.Name + "." + method.Name, attribute);
                    }
                },
                OperationKind.Invocation
            );
            start.RegisterOperationAction(
                operation =>
                {
                    var creation = (IObjectCreationOperation)operation.Operation;
                    if (Names.Is(creation.Type, thread))
                    {
                        Check(operation, creation.Arguments, "new Thread", attribute);
                    }
                },
                OperationKind.ObjectCreation
            );
        });
    }

    private static void Check(OperationAnalysisContext context, ImmutableArray<IArgumentOperation> arguments, string starter, INamedTypeSymbol attribute)
    {
        foreach (IArgumentOperation argument in arguments)
        {
            // Only a lambda written as the argument itself: a method group, a delegate kept in a variable, or a parenthesized
            // lambda is not looked at (design §2.2, "Analyzers").
            if (
                argument.Syntax is not ArgumentSyntax { Expression: AnonymousFunctionExpressionSyntax }
                || Operations.Unwrap(argument.Value) is not IAnonymousFunctionOperation function
            )
            {
                continue;
            }

            foreach (IOperation operation in Operations.OfFunction(function.Body))
            {
                ISymbol? called = UiThreadOnly.Called(operation, attribute);
                if (called is not null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rules.OffUiThread, operation.Syntax.GetLocation(), called.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat), starter)
                    );
                }
            }
        }
    }
}
