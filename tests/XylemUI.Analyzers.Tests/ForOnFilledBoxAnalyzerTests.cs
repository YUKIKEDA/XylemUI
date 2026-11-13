using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XylemUI.Analyzers.Tests;

// XYLEM130 (design §2.2, "Analyzers"): For or VirtualFor on a box that has children, or a second one in the same chain.
public sealed class ForOnFilledBoxAnalyzerTests
{
    /// <summary>
    /// <c>For</c> and <c>VirtualFor</c> on a box made with children, through the chain of extension methods, and a second one in
    /// the same chain, are reported at the method's name.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Column(header).For(...)</c>; <c>Box(header).For(...)</c>; <c>Column(header).Width(50).For(...)</c> (through an
    ///   extension method); <c>(Column(header)).For(...)</c> (in parentheses); <c>Column(header).VirtualFor(...)</c>;
    ///   <c>Column().For(...).For(...)</c>; <c>Column().For(...).Width(50).VirtualFor(...)</c>.
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - One XYLEM130, at the name of the last <c>For</c> or <c>VirtualFor</c>, and its message says which mistake it is.
    /// </remarks>
    [Theory]
    [InlineData("_ = Column(header).For(items, i => i, Row);", "For", "children")]
    [InlineData("_ = Box(header).For(items, i => i, Row);", "For", "children")]
    [InlineData("_ = Column(header).Width(50).For(items, i => i, Row);", "For", "children")]
    [InlineData("_ = (Column(header)).For(items, i => i, Row);", "For", "children")]
    [InlineData("_ = Column(header).VirtualFor(items, i => i, Row);", "VirtualFor", "children")]
    [InlineData("_ = Column().For(items, i => i, Row).For(items, i => i, Row);", "For", "already calls 'For'")]
    [InlineData("_ = Column().For(items, i => i, Row).Width(50).VirtualFor(items, i => i, Row);", "VirtualFor", "already calls 'For'")]
    public async Task For_OnAFilledBox_IsReported(string body, string name, string kind)
    {
        ImmutableArray<Diagnostic> diagnostics = await Harness.DiagnoseAsync(new ForOnFilledBoxAnalyzer(), Code(body));

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("XYLEM130", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(name, Harness.At(diagnostic));
        Assert.Contains(kind, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the rule does not look at is not reported: an empty box, a box kept in a variable, a box another method returned, a
    /// second <c>For</c> in another statement, and a box whose arguments are no elements.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Cases: <c>Column().For(...)</c>; <c>Column().Width(50).For(...)</c>; a <c>Column(header)</c> kept in a variable and then
    ///   given <c>For</c>; a box from the app's own method; <c>For</c> on a variable that already has one; <c>Text("a").For(...)</c>
    ///   (a string argument); the box with the rows given as a child of another (<c>Column(header, Column().For(...))</c>).
    ///
    /// Steps:
    /// - Run the analyzer on a method whose body is the case.
    ///
    /// Expected:
    /// - No diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("_ = Column().For(items, i => i, Row);")]
    [InlineData("_ = Column().Width(50).For(items, i => i, Row);")]
    [InlineData("Element list = Column(header); _ = list.For(items, i => i, Row);")]
    [InlineData("_ = Header().For(items, i => i, Row);")]
    [InlineData("Element list = Column().For(items, i => i, Row); _ = list.For(items, i => i, Row);")]
    [InlineData("_ = Text(\"a\").For(items, i => i, Row);")]
    [InlineData("_ = Column(header, Column().For(items, i => i, Row));")]
    public async Task For_TheRuleDoesNotLookAt_IsNotReported(string body)
    {
        Assert.Empty(await Harness.DiagnoseAsync(new ForOnFilledBoxAnalyzer(), Code(body)));
    }

    private static string Code(string body) =>
        $$"""
            using System.Collections.Generic;
            using XylemUI;
            using static XylemUI.Elements;

            namespace Demo;

            public static class Code
            {
                public static void Run(Element header, IReadOnlySignal<IReadOnlyList<int>> items)
                {
                    {{body}}
                }

                private static Element Header() => Column(Box());

                private static Element Row(IReadOnlySignal<int> item) => Box();
            }
            """;
}
