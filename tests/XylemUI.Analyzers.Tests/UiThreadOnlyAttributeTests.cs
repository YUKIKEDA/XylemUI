using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XylemUI.Analyzers.Tests;

// Keeps [UiThreadOnly] and the UI-thread checks of the code in step (design §2.2, "The attribute for UI-thread-only APIs"): a public
// API that calls UiThread.VerifyAccess at its entry, or through private or internal helpers of its assembly, carries the attribute,
// and a member that carries it does call it, or calls another member that carries it.
public sealed class UiThreadOnlyAttributeTests
{
    // The SDK's implicit usings, which the projects compile with and their sources do not write.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    /// <summary>
    /// In each assembly whose public APIs the design says carry the attribute, the members that reach <c>UiThread.VerifyAccess</c>
    /// are the members that carry it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: the sources of <c>XylemUI.Core</c>, <c>XylemUI.Platform.Windows</c>, <c>XylemUI.Rendering.D3D11</c>, and
    ///   <c>XylemUI.Testing</c> under <c>src</c>, each parsed into one compilation with the SDK's implicit usings and, but for Core,
    ///   a reference to the built <c>XylemUI.Core</c>. Errors of the compilation (the native interop types are not referenced) do
    ///   not matter: only the calls are bound.
    ///
    /// Steps:
    /// - For each method, constructor, and accessor with a body, find the members it calls outside the lambdas and local functions
    ///   in it. A member checks when it calls <c>UiThread.VerifyAccess</c>, or calls a private or internal member of the same
    ///   sources that checks.
    /// - List the members visible outside the assembly that check and carry no attribute (on the member, or on the property of an
    ///   accessor), and the members that carry it and neither check nor call a member that carries it.
    ///
    /// Expected:
    /// - Both lists are empty.
    /// </remarks>
    [Theory]
    [InlineData("XylemUI.Core")]
    [InlineData("XylemUI.Platform.Windows")]
    [InlineData("XylemUI.Rendering.D3D11")]
    [InlineData("XylemUI.Testing")]
    public void Attribute_OfAnAssembly_MatchesTheMembersThatCheckTheThread(string project)
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        string root = typeof(UiThreadOnlyAttributeTests)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static metadata => metadata.Key == "XylemUI.Analyzers.Tests.SourceRoot")
            .Value!;
        string folder = Path.Combine(root, project);
        string separator = Path.DirectorySeparatorChar.ToString();
        IEnumerable<SyntaxTree> trees = Directory
            .EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(separator + "obj" + separator, StringComparison.Ordinal)
                && !path.Contains(separator + "bin" + separator, StringComparison.Ordinal)
            )
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), Harness.Parse, path, cancellationToken: cancellation));
        ImmutableArray<MetadataReference> references =
            project == "XylemUI.Core"
                ? [.. Harness.References.Where(static reference => !reference.Display!.EndsWith("XylemUI.Core.dll", StringComparison.Ordinal))]
                : Harness.References;
        CSharpCompilation compilation = CSharpCompilation.Create(
            project,
            [.. trees, CSharpSyntaxTree.ParseText(ImplicitUsings, Harness.Parse, cancellationToken: cancellation)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        INamedTypeSymbol attribute = compilation.GetTypeByMetadataName("XylemUI.UiThreadOnlyAttribute")!;
        Assert.NotNull(attribute);

        var calls = new Dictionary<IMethodSymbol, CallsOf>(SymbolEqualityComparer.Default);
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (SyntaxNode body in tree.GetRoot(cancellation).DescendantNodes().Where(IsBodyOwner))
            {
                if (Declared(model, body, cancellation) is { } method)
                {
                    CallsOf found = Find(model, body, cancellation);
                    calls[method] = calls.TryGetValue(method, out CallsOf? earlier) ? earlier.With(found) : found;
                }
            }
        }

        var checks = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        bool Checks(IMethodSymbol method)
        {
            if (checks.TryGetValue(method, out bool known))
            {
                return known;
            }

            checks[method] = false; // a cycle of helpers does not check by itself
            bool result =
                calls.TryGetValue(method, out CallsOf? of)
                && (of.VerifiesAccess || of.Callees.Any(callee => calls.ContainsKey(callee) && !IsVisible(callee) && Checks(callee)));
            checks[method] = result;
            return result;
        }

        var missing = new List<string>();
        var extra = new List<string>();
        foreach ((IMethodSymbol method, CallsOf of) in calls)
        {
            bool carries = Carries(method, attribute);
            if (IsVisible(method) && Checks(method) && !carries)
            {
                missing.Add(Name(method));
            }

            if (carries && !Checks(method) && !of.Callees.Any(callee => Carries(callee, attribute)))
            {
                extra.Add(Name(method));
            }
        }

        missing.Sort(StringComparer.Ordinal);
        extra.Sort(StringComparer.Ordinal);
        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            "Reach VerifyAccess without [UiThreadOnly]:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, missing)
                + Environment.NewLine
                + "Carry [UiThreadOnly] and reach no check:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, extra)
        );
    }

    /// <summary>
    /// The attribute goes on constructors, methods, and properties (an accessor is a method), once, and is not inherited by derived
    /// types or overriding members, as the design declares it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - <c>XylemUI.UiThreadOnlyAttribute</c> of the built <c>XylemUI.Core</c>.
    ///
    /// Steps:
    /// - Read its <c>AttributeUsage</c>.
    ///
    /// Expected:
    /// - The targets are exactly <c>Constructor | Method | Property</c>, <c>Inherited</c> is false, and <c>AllowMultiple</c> is
    ///   false; the class is sealed.
    /// </remarks>
    [Fact]
    public void Attribute_Usage_IsTheDesigns()
    {
        AttributeUsageAttribute usage = typeof(UiThreadOnlyAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.Inherited);
        Assert.False(usage.AllowMultiple);
        Assert.True(typeof(UiThreadOnlyAttribute).IsSealed);
    }

    private static bool IsBodyOwner(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
        || node is PropertyDeclarationSyntax { ExpressionBody: not null } or IndexerDeclarationSyntax { ExpressionBody: not null };

    // The method a body belongs to: a method, a constructor, an operator, an accessor, or the getter of an expression-bodied
    // property or indexer.
    private static IMethodSymbol? Declared(SemanticModel model, SyntaxNode node, CancellationToken cancellation) =>
        model.GetDeclaredSymbol(node, cancellation) switch
        {
            IMethodSymbol method => method,
            IPropertySymbol property => property.GetMethod,
            _ => null,
        };

    private static CallsOf Find(SemanticModel model, SyntaxNode owner, CancellationToken cancellation)
    {
        bool verifies = false;
        var callees = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        IEnumerable<SyntaxNode> nodes = owner
            .ChildNodes()
            .Where(static child => child is BlockSyntax or ArrowExpressionClauseSyntax or ConstructorInitializerSyntax)
            .SelectMany(static body =>
                body.DescendantNodesAndSelf(static node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            );
        foreach (SyntaxNode node in nodes)
        {
            if (node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                continue;
            }

            // A call binds at the invocation or the creation; a property at its name, or at the member access that holds the name.
            bool call = node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax;
            bool memberName = node.Parent is MemberAccessExpressionSyntax access && access.Name == node;
            bool reference = node is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax || (node is IdentifierNameSyntax && !memberName);
            if (!call && !reference)
            {
                continue;
            }

            SymbolInfo info = model.GetSymbolInfo(node, cancellation);
            ISymbol? symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
            switch (symbol)
            {
                // A method group passed as a value is not a call.
                case IMethodSymbol { MethodKind: not MethodKind.LocalFunction } method when call:
                    IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
                    if (definition.Name == "VerifyAccess" && definition.ContainingType.ToDisplayString() == "XylemUI.UiThread")
                    {
                        verifies = true;
                    }

                    _ = callees.Add(definition);
                    break;
                case IPropertySymbol property:
                    IPropertySymbol original = property.OriginalDefinition;
                    bool sets = node.Parent is AssignmentExpressionSyntax assignment && assignment.Left == node;
                    bool gets = !(
                        node.Parent is AssignmentExpressionSyntax simple
                        && simple.Left == node
                        && simple.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    );
                    if (gets && original.GetMethod is { } getter)
                    {
                        _ = callees.Add(getter);
                    }

                    if (sets && original.SetMethod is { } setter)
                    {
                        _ = callees.Add(setter);
                    }

                    break;
            }
        }

        return new CallsOf(verifies, callees);
    }

    // Seen from outside the assembly: public or protected, in types that are too. An explicit implementation of an interface is
    // private, and seen only through the interface.
    private static bool IsVisible(IMethodSymbol method)
    {
        ISymbol member = method.AssociatedSymbol ?? method;
        for (ISymbol? symbol = method; symbol is not null and not INamespaceSymbol; symbol = symbol.ContainingSymbol)
        {
            Accessibility accessibility = ReferenceEquals(symbol, method) ? Effective(method, member) : symbol.DeclaredAccessibility;
            if (accessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    // An accessor without a modifier of its own has its property's accessibility.
    private static Accessibility Effective(IMethodSymbol method, ISymbol member) =>
        method.DeclaredAccessibility == Accessibility.NotApplicable ? member.DeclaredAccessibility : method.DeclaredAccessibility;

    private static bool Carries(IMethodSymbol method, INamedTypeSymbol attribute) =>
        Has(method, attribute) || (method.AssociatedSymbol is IPropertySymbol property && Has(property, attribute));

    private static bool Has(ISymbol symbol, INamedTypeSymbol attribute) =>
        symbol.GetAttributes().Any(data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute));

    private static string Name(IMethodSymbol method) => method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private sealed record CallsOf(bool VerifiesAccess, HashSet<IMethodSymbol> Callees)
    {
        // A partial method's two parts, or an accessor seen twice, add up.
        public CallsOf With(CallsOf other)
        {
            Callees.UnionWith(other.Callees);
            return this with { VerifiesAccess = VerifiesAccess || other.VerifiesAccess };
        }
    }
}
