using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace XylemUI.Analyzers;

/// <summary>
/// Reports XYLEM110: a <c>Get()</c> of a signal written directly in the argument of an API that has a reactive overload, on the side
/// that does not react (design §2.2, "Analyzers"). An API has a reactive overload when its type has a method of the same name whose
/// parameter at the same position is <c>Func&lt;T&gt;</c> or <c>IReadOnlySignal&lt;T&gt;</c> where this one takes <c>T</c>. A
/// lambda inside the argument is not looked into: that is the reactive form.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReadOnceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rules.ReadOnce);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (
                start.Compilation.GetTypeByMetadataName(Names.ReadOnlySignal) is not { } readOnlySignal
                || start.Compilation.GetTypeByMetadataName(Names.Signal) is not { } signal
                || start.Compilation.GetTypeByMetadataName(Names.ComputedValue) is not { } computed
                || start.Compilation.GetTypeByMetadataName("System.Func`1") is not { } func
            )
            {
                return;
            }

            var types = new Types(readOnlySignal, signal, computed, func);
            start.RegisterOperationAction(operation => Analyze(operation, types), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Types types)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (
                argument.ArgumentKind != ArgumentKind.Explicit
                || argument.Parameter is not { } parameter
                || !HasReactiveOverload(method, parameter, types)
            )
            {
                continue;
            }

            foreach (IOperation operation in Operations.OfFunction(argument.Value))
            {
                if (operation is IInvocationOperation read && IsGet(read.TargetMethod, types))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rules.ReadOnce, argument.Syntax.GetLocation(), method.Name));
                    break;
                }
            }
        }
    }

    private static bool HasReactiveOverload(IMethodSymbol method, IParameterSymbol parameter, Types types)
    {
        int ordinal = parameter.Ordinal;
        ITypeSymbol type = parameter.Type;
        foreach (ISymbol member in method.ContainingType.GetMembers(method.Name))
        {
            if (
                member is IMethodSymbol other
                && other.Parameters.Length > ordinal
                && other.Parameters[ordinal].Type is INamedTypeSymbol { TypeArguments.Length: 1 } wrapper
                && (Names.Is(wrapper, types.Func) || Names.Is(wrapper, types.ReadOnlySignal))
                && SymbolEqualityComparer.Default.Equals(wrapper.TypeArguments[0], type)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGet(IMethodSymbol method, Types types) =>
        method.Name == "Get"
        && method.Parameters.Length == 0
        && (
            Names.Is(method.ContainingType, types.ReadOnlySignal)
            || Names.Is(method.ContainingType, types.Signal)
            || Names.Is(method.ContainingType, types.Computed)
        );

    private sealed class Types(INamedTypeSymbol readOnlySignal, INamedTypeSymbol signal, INamedTypeSymbol computed, INamedTypeSymbol func)
    {
        public INamedTypeSymbol ReadOnlySignal { get; } = readOnlySignal;

        public INamedTypeSymbol Signal { get; } = signal;

        public INamedTypeSymbol Computed { get; } = computed;

        public INamedTypeSymbol Func { get; } = func;
    }
}
