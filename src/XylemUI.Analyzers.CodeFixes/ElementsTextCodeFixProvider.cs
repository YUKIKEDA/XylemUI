using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XylemUI.Analyzers;

/// <summary>
/// Offers, for the compiler's CS0118 on a call of <c>Text</c> inside the namespace <c>XylemUI</c> or one under it, to write it
/// <c>Elements.Text(...)</c>: there the namespace <c>XylemUI.Text</c> is found before the <c>Text</c> of
/// <c>using static XylemUI.Elements;</c> (design §2.2, "Analyzers"; §2.13).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ElementsTextCodeFixProvider))]
[Shared]
public sealed class ElementsTextCodeFixProvider : CodeFixProvider
{
    private const string Title = "Call Elements.Text";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("CS0118");

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null || !HasText(model.Compilation))
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            // The name the compiler refused is the callee of a call, written as the bare name Text.
            if (
                root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is IdentifierNameSyntax { Identifier.ValueText: "Text" } name
                && name.Parent is InvocationExpressionSyntax invocation
                && invocation.Expression == name
                && InXylemNamespace(model.GetEnclosingSymbol(name.SpanStart, context.CancellationToken))
            )
            {
                context.RegisterCodeFix(CodeAction.Create(Title, _ => Fix(context.Document, root, name), equivalenceKey: Title), diagnostic);
            }
        }
    }

    // XylemUI.Elements has a Text: the fix names a method that exists.
    private static bool HasText(Compilation compilation) =>
        compilation.GetTypeByMetadataName("XylemUI.Elements") is { } elements && !elements.GetMembers("Text").IsEmpty;

    // The namespace XylemUI or one under it: there the name Elements finds XylemUI.Elements.
    private static bool InXylemNamespace(ISymbol? enclosing)
    {
        for (INamespaceSymbol? space = enclosing as INamespaceSymbol ?? enclosing?.ContainingNamespace; space is not null; space = space.ContainingNamespace)
        {
            if (space.Name == "XylemUI" && space.ContainingNamespace is { IsGlobalNamespace: true })
            {
                return true;
            }
        }

        return false;
    }

    private static Task<Document> Fix(Document document, SyntaxNode root, IdentifierNameSyntax name)
    {
        MemberAccessExpressionSyntax access = SyntaxFactory
            .MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("Elements"), name.WithoutTrivia())
            .WithTriviaFrom(name);
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(name, access)));
    }
}
