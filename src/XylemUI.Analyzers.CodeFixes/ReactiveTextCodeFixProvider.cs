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
/// Offers, for XYLEM110 on <c>Text(string)</c>, to pass the argument as a function: <c>Text(x)</c> becomes <c>Text(() =&gt; x)</c>,
/// which writes the text again when a signal it reads changes (design §2.2, "Analyzers").
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReactiveTextCodeFixProvider))]
[Shared]
public sealed class ReactiveTextCodeFixProvider : CodeFixProvider
{
    private const string Title = "Pass the text as a function";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("XYLEM110");

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        SemanticModel? model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            if (
                root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ArgumentSyntax>() is { } argument
                && argument.Parent?.Parent is InvocationExpressionSyntax invocation
                && IsTextOfString(model.GetSymbolInfo(invocation, context.CancellationToken).Symbol)
            )
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Fix(context.Document, root, argument), equivalenceKey: Title),
                    diagnostic
                );
            }
        }
    }

    // Elements.Text(string): its reactive overload takes Func<string>, so a lambda that returns the argument converts to it.
    private static bool IsTextOfString(ISymbol? symbol) =>
        symbol is IMethodSymbol { Name: "Text", Parameters.Length: 1 } method
        && method.Parameters[0].Type.SpecialType == SpecialType.System_String
        && method.ContainingType.ToDisplayString() == "XylemUI.Elements";

    private static Task<Document> Fix(Document document, SyntaxNode root, ArgumentSyntax argument)
    {
        // "() => " with the spaces the repository's format writes; the argument keeps its own trivia around the lambda.
        SyntaxToken arrow = SyntaxFactory.Token(
            SyntaxFactory.TriviaList(SyntaxFactory.Space),
            SyntaxKind.EqualsGreaterThanToken,
            SyntaxFactory.TriviaList(SyntaxFactory.Space)
        );
        ParenthesizedLambdaExpressionSyntax lambda = SyntaxFactory
            .ParenthesizedLambdaExpression(SyntaxFactory.ParameterList(), null, argument.Expression.WithoutTrivia())
            .WithArrowToken(arrow)
            .WithLeadingTrivia(argument.Expression.GetLeadingTrivia())
            .WithTrailingTrivia(argument.Expression.GetTrailingTrivia());
        return Task.FromResult(document.WithSyntaxRoot(root.ReplaceNode(argument.Expression, lambda)));
    }
}
